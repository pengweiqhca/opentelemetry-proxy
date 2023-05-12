using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using GetTags =
    System.Func<Castle.DynamicProxy.IInvocation, System.Collections.Generic.IReadOnlyCollection<
        System.Collections.Generic.KeyValuePair<string, object?>>?>;

namespace OpenTelemetry.DynamicProxy;

/// <summary>Instance should be singleton.</summary>
public class ActivityInvokerFactory : IActivityInvokerFactory, IDisposable
{
    private static readonly ConstructorInfo KeyValuePairCtor =
        typeof(KeyValuePair<string, object?>).GetConstructors().Single();

    private static readonly MethodInfo SetTag = typeof(Activity).GetMethod(nameof(Activity.SetTag))!;

    private static readonly MethodInfo GetArgumentValue =
        typeof(IInvocation).GetMethod(nameof(IInvocation.GetArgumentValue))!;

    private readonly Dictionary<Type, Dictionary<MethodInfo, IActivityInvoker>> _activityInvokers = new();

    private readonly ConcurrentDictionary<Type, ActivitySource> _activitySources = new();

    public void Invoke(IInvocation invocation, ActivityType activityType)
    {
        if (!_activityInvokers.TryGetValue(invocation.TargetType, out var activityInvokers))
            _activityInvokers[invocation.TargetType] = activityInvokers = new();

        if (!activityInvokers.TryGetValue(invocation.Method, out var invoker))
        {
            invoker = CreateActivityInvoker(invocation, activityType);

            try
            {
                activityInvokers[invocation.Method] = invoker;
            }
            catch (NullReferenceException) { }
        }

        invoker.Invoke(invocation);
    }

    private IActivityInvoker CreateActivityInvoker(IInvocation invocation, ActivityType activityType)
    {
        var type = invocation.Method.ReflectedType ?? invocation.Method.DeclaringType ?? invocation.TargetType;

        // If has processed by fody, invoke directly.
        if (type.IsDefined(typeof(ProxyHasGeneratedAttribute))) return ActivityInvokerHelper.Noop;

        var settings = ActivityInvokerHelper.GetActivityName(invocation.Method, type, out var activityName,
            out var kind, out var maxUsableTimes);

        if (string.IsNullOrWhiteSpace(activityName))
            activityName = $"{invocation.TargetType.FullName}.{invocation.Method.Name}";

        return (IActivityInvoker)(settings switch
        {
            ActivitySettings.Activity => Activator.CreateInstance(typeof(ActivityInvoker),
                GetActivitySource(type), activityName, kind,
                SetActivityTags(invocation.TargetType, invocation.Method, out activityName), activityName),
            ActivitySettings.ActivityNameOnly => Activator.CreateInstance(typeof(ActivityNameInvoker), activityName,
                maxUsableTimes, CreateActivityTags(invocation.TargetType, invocation.Method))!,
            ActivitySettings.NonActivityAndSuppressInstrumentation => Activator.CreateInstance(
                typeof(ActivityNameInvoker), true),
            _ => activityType switch
            {
                ActivityType.ImplicitActivity => Activator.CreateInstance(typeof(ActivityInvoker),
                    GetActivitySource(type), activityName, ActivityKind.Internal,
                    SetActivityTags(invocation.TargetType, invocation.Method, out activityName), activityName),
                ActivityType.ImplicitActivityName => Activator.CreateInstance(typeof(ActivityNameInvoker),
                    activityName, 1, CreateActivityTags(invocation.TargetType, invocation.Method)),
                _ => ActivityInvokerHelper.Noop
            }
        });
    }

    private ActivitySource GetActivitySource(Type type) => _activitySources.GetOrAdd(type, static type =>
        new(ActivitySourceAttribute.GetActivitySourceName(type), type.Assembly.GetName().Version?.ToString() ?? ""));

    private static Action<IInvocation, Activity>? SetActivityTags(Type type, MethodInfo method,
        out string? returnValueTagName)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");
        var activity = Expression.Parameter(typeof(Activity), "activity");

        var keyValuePairs = GetActivityTags(type, method,
            method.GetCustomAttribute<ActivityAttribute>()?.Tags?.ToList(),
            invocation, out returnValueTagName);

        return keyValuePairs.Count < 1
            ? null
            : Expression.Lambda<Action<IInvocation, Activity>>(Expression.Block(keyValuePairs.Select(kv =>
            {
                var value = kv.Value;
                if (value.Type.IsValueType || value.Type.IsGenericParameter)
                    value = Expression.Convert(value, typeof(object));

                return Expression.Call(activity, SetTag, Expression.Constant(kv.Key), value);
            })), invocation, activity).Compile();
    }

    private static GetTags? CreateActivityTags(Type type, MethodInfo method)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var keyValuePairs = GetActivityTags(type, method, (method.GetCustomAttribute<ActivityNameAttribute>() ??
            method.DeclaringType?.GetCustomAttribute<ActivityNameAttribute>())?.Tags?.ToList(), invocation, out _);

        return keyValuePairs.Count < 1
            ? null
            : Expression.Lambda<GetTags>(Expression.NewArrayInit(typeof(KeyValuePair<string, object>),
                keyValuePairs.Select(static kv =>
                {
                    var value = kv.Value;
                    if (value.Type.IsValueType || value.Type.IsGenericParameter)
                        value = Expression.Convert(value, typeof(object));

                    return Expression.New(KeyValuePairCtor, Expression.Constant(kv.Key), value);
                })), invocation).Compile();
    }

    private static IReadOnlyList<KeyValuePair<string, Expression>> GetActivityTags(Type type, MethodInfo method,
        ICollection<string>? tags, Expression invocation, out string? returnValueTagName)
    {
        if (method.ReturnType != typeof(void) &&
            (!CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo) ||
                awaitableInfo.AwaitableInfo.AwaiterGetResultMethod.ReturnType != typeof(void)))
            TryGetName(tags, method.ReturnParameter?.GetCustomAttribute<ActivityTagAttribute>(true),
                ActivityTagAttribute.ReturnValueTagName, out returnValueTagName);
        else returnValueTagName = null;

        var list = new List<KeyValuePair<string, Expression>>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                     BindingFlags.Static))
            if (TryGetName(tags, field.GetCustomAttribute<ActivityTagAttribute>(true),
                    field.Name, out var name))
                list.Add(new(name, Expression.Field(field.IsStatic
                    ? null
                    : Expression.Convert(Expression.Property(invocation,
                        "InvocationTarget"), type), field)));

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.Static))
            if (property.GetMethod != null && TryGetName(tags,
                    property.GetCustomAttribute<ActivityTagAttribute>(true),
                    property.Name, out var name))
                list.Add(new(name, Expression.Property(property.GetMethod.IsStatic
                    ? null
                    : Expression.Convert(Expression.Property(invocation,
                        "InvocationTarget"), type), property)));

        var index = 0;
        foreach (var parameter in method.GetParameters())
            if (TryGetName(tags, parameter.GetCustomAttribute<ActivityTagAttribute>(true),
                    parameter.Name ?? index.ToString(CultureInfo.InvariantCulture), out var name))
            {
                list.Add(new(name, Expression.Call(invocation, GetArgumentValue, Expression.Constant(index))));

                index++;
            }

        return list;
    }

    private static bool TryGetName(ICollection<string>? tags, ActivityTagAttribute? attr, string memberName,
        [NotNullWhen(true)] out string? name)
    {
        name = null;

        if (attr == null)
        {
            if (tags == null || !tags.Contains(memberName)) return false;

            name = memberName;
        }
        else name = string.IsNullOrWhiteSpace(attr.Name) ? memberName : attr.Name!;

        tags?.Remove(memberName);

        return true;
    }

    public void Dispose()
    {
        _activityInvokers.Clear();

        foreach (var activitySource in _activitySources.Values) activitySource.Dispose();

        _activitySources.Clear();

        GC.SuppressFinalize(this);
    }
}
