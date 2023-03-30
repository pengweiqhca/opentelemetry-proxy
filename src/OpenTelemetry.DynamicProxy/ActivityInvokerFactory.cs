using OpenTelemetry.Proxy;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using GetTags = System.Func<Castle.DynamicProxy.IInvocation, System.Collections.Generic.IReadOnlyCollection<System.Collections.Generic.KeyValuePair<string, object?>>?>;

namespace OpenTelemetry.DynamicProxy;

/// <summary>Instance should be singleton.</summary>
public class ActivityInvokerFactory : IActivityInvokerFactory, IDisposable
{
    private static readonly ConstructorInfo KeyValuePairCtor =
        typeof(KeyValuePair<string, object?>).GetConstructors().Single();

    private static readonly MethodInfo GetArgumentValue =
        typeof(IInvocation).GetMethod(nameof(IInvocation.GetArgumentValue))!;

    private readonly IDictionary<MethodInfo, IActivityInvoker> _activityInvokers =
        new Dictionary<MethodInfo, IActivityInvoker>();

    private readonly ConcurrentDictionary<Type, ActivitySource> _activitySources = new();

    public void Invoke(IInvocation invocation, ActivityType activityType)
    {
        if (!_activityInvokers.TryGetValue(invocation.Method, out var invoker))
        {
            invoker = CreateActivityInvoker(invocation, activityType);

            try
            {
                _activityInvokers[invocation.Method] = invoker;
            }
            catch (NullReferenceException) { }
        }

        invoker.Invoke(invocation);
    }

    private IActivityInvoker CreateActivityInvoker(IInvocation invocation, ActivityType activityType)
    {
        var type = invocation.Method.ReflectedType ?? invocation.Method.DeclaringType ?? invocation.TargetType;

        var settings = ActivityInvokerHelper.GetActivityName(invocation.Method, type, out var activityName,
            out var kind, out var maxUsableTimes);

        if (string.IsNullOrWhiteSpace(activityName))
            activityName = $"{invocation.TargetType.FullName}.{invocation.Method.Name}";

        return (IActivityInvoker)(settings switch
        {
            ActivitySettings.Activity => Activator.CreateInstance(
                ActivityInvokerHelper.GetActivityInvokerType(invocation.Method.ReturnType),
                GetActivitySource(type), activityName, kind,
                CreateActivityGetTags(invocation.TargetType, invocation.Method))!,
            ActivitySettings.ActivityNameOnly => Activator.CreateInstance(
                ActivityInvokerHelper.GetActivityNameInvokerType(invocation.Method.ReturnType), activityName,
                maxUsableTimes, CreateActivityNameGetTags(invocation.TargetType, invocation.Method))!,
            ActivitySettings.NonActivityAndSuppressInstrumentation => Activator.CreateInstance(
                ActivityInvokerHelper.GetActivityNameInvokerType(invocation.Method.ReturnType), true)!,
            _ => activityType switch
            {
                ActivityType.ImplicitActivity => Activator.CreateInstance(
                    ActivityInvokerHelper.GetActivityInvokerType(invocation.Method.ReturnType),
                    GetActivitySource(type), activityName, ActivityKind.Internal,
                    CreateActivityGetTags(invocation.TargetType, invocation.Method))!,
                ActivityType.ImplicitActivityName => Activator.CreateInstance(
                        ActivityInvokerHelper.GetActivityNameInvokerType(invocation.Method.ReturnType),
                        activityName, 1,
                        CreateActivityNameGetTags(invocation.TargetType, invocation.Method))
                    !,
                _ => ActivityInvokerHelper.Noop
            }
        });
    }

    private ActivitySource GetActivitySource(Type type) => _activitySources.GetOrAdd(type, static type =>
        new(ActivitySourceAttribute.GetActivitySourceName(type), type.Assembly.GetName().Version?.ToString() ?? ""));

    private static GetTags? CreateActivityGetTags(Type type, MethodInfo method) =>
        CreateGetTags(type, method, method.GetCustomAttribute<ActivityAttribute>()?.Tags?.ToList());

    private static GetTags? CreateActivityNameGetTags(Type type, MethodInfo method) =>
        CreateGetTags(type, method, (method.GetCustomAttribute<ActivityNameAttribute>() ??
            method.DeclaringType?.GetCustomAttribute<ActivityNameAttribute>())?.Tags?.ToList());

    private static GetTags? CreateGetTags(Type type, MethodInfo method, ICollection<string>? tags)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");
        var elementInits = new List<Expression>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            if (TryGetName(tags, field.GetCustomAttribute<ActivityTagAttribute>(true),
                    field.Name, out var name))
                elementInits.Add(Expression.New(KeyValuePairCtor, Expression.Constant(name),
                    Expression.Convert(Expression.Field(field.IsStatic
                        ? null
                        : Expression.Convert(Expression.Property(invocation,
                            "InvocationTarget"), type), field), typeof(object))));

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            if (property.GetMethod != null && TryGetName(tags,
                    property.GetCustomAttribute<ActivityTagAttribute>(true),
                    property.Name, out var name))
                elementInits.Add(Expression.New(KeyValuePairCtor, Expression.Constant(name),
                    Expression.Convert(Expression.Property(property.GetMethod.IsStatic
                        ? null
                        : Expression.Convert(Expression.Property(invocation,
                            "InvocationTarget"), type), property), typeof(object))));

        var index = 0;
        foreach (var parameter in method.GetParameters())
        {
            if (TryGetName(tags, parameter.GetCustomAttribute<ActivityTagAttribute>(true),
                    parameter.Name ?? index.ToString(CultureInfo.InvariantCulture), out var name))
                elementInits.Add(Expression.New(KeyValuePairCtor, Expression.Constant(name),
                    Expression.Call(invocation, GetArgumentValue, Expression.Constant(index))));

            index++;
        }

        return elementInits.Count < 1
            ? null
            : Expression.Lambda<GetTags>(Expression.NewArrayInit(typeof(KeyValuePair<string, object>), elementInits),
                invocation).Compile();
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
        else name = attr.TagName ?? memberName;

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
