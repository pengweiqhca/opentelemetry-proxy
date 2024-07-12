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

    private static readonly MethodInfo SetTagEnumerable =
        typeof(ActivityExtensions).GetMethod(nameof(ActivityExtensions.SetTagEnumerable))!;

    private static readonly MethodInfo GetArgumentValue =
        typeof(IInvocation).GetMethod(nameof(IInvocation.GetArgumentValue))!;

    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<MethodInfo, IActivityInvoker>> _activityInvokers =
        [];

    private readonly ConcurrentDictionary<Type, ActivitySource> _activitySources = [];

    public void Invoke(IInvocation invocation, ImplicitActivityContext context)
    {
        IActivityInvoker invoker;

        if (invocation.Method.IsSpecialName) invoker = ActivityInvokerHelper.Noop;
        else
        {
            if (!_activityInvokers.TryGetValue(invocation.TargetType, out var activityInvokers))
                _activityInvokers[invocation.TargetType] = activityInvokers = [];

            if (!activityInvokers.TryGetValue(invocation.Method, out invoker))
            {
                invoker = CreateActivityInvoker(invocation, context);

                try
                {
                    activityInvokers[invocation.Method] = invoker;
                }
                catch (NullReferenceException) { }
            }
        }

        invoker.Invoke(invocation);
    }

    private IActivityInvoker CreateActivityInvoker(IInvocation invocation, ImplicitActivityContext context)
    {
        if (invocation.Method.IsSpecialName) return ActivityInvokerHelper.Noop;

        var type = invocation.Method.ReflectedType ?? invocation.Method.DeclaringType ?? invocation.TargetType;

        // If it has been processed by fody, invoke directly.
        if (type.Assembly.IsDefined(typeof(ProxyHasGeneratedAttribute)) ||
            type.IsDefined(typeof(ProxyHasGeneratedAttribute))) return ActivityInvokerHelper.Noop;

        var settings = ActivityInvokerHelper.GetActivityName(invocation.Method, type, out var activityName,
            out var kind, out var maxUsableTimes);

        if (settings == ActivitySettings.Activity)
            return new ActivityInvoker(GetActivitySource(type), GetActivityName(invocation, activityName), kind,
                false, SetActivityTags(invocation.TargetType, invocation.Method, out activityName), activityName);

        if (settings == ActivitySettings.ActivityAndSuppressInstrumentation)
            return new ActivityInvoker(GetActivitySource(type), GetActivityName(invocation, activityName), kind,
                true, SetActivityTags(invocation.TargetType, invocation.Method, out activityName), activityName);

        if (settings == ActivitySettings.ActivityName)
            return new ActivityNameInvoker(GetActivityName(invocation, activityName), maxUsableTimes,
                CreateActivityTags(invocation.TargetType, invocation.Method));

        if (settings == ActivitySettings.SuppressInstrumentation)
            return new ActivityNameInvoker();

        if (context.Type == ImplicitActivityType.Activity)
            return context.BeforeProceed != null || context.AfterProceed != null
                ? new ActivityInvoker(GetActivitySource(type, context.ActivitySourceName),
                    GetActivityName(invocation, activityName, context.ActivitySourceName),
                    context.ActivityKind, context.SuppressInstrumentation,
                    (context.BeforeProceed, context.AfterProceed), context.ReturnValueTagName)
                : new ActivityInvoker(GetActivitySource(type, context.ActivitySourceName),
                    GetActivityName(invocation, activityName, context.ActivitySourceName),
                    context.ActivityKind, context.SuppressInstrumentation,
                    SetActivityTags(invocation.TargetType, invocation.Method, out activityName),
                    context.ReturnValueTagName ?? activityName);

        if (context.Type == ImplicitActivityType.ActivityName)
            return new ActivityNameInvoker(GetActivityName(invocation, activityName), 1,
                CreateActivityTags(invocation.TargetType, invocation.Method));

        return ActivityInvokerHelper.Noop;
    }

    private static string GetActivityName(IInvocation invocation, string? activityName,
        string? implicitActivitySourceName = null)
    {
        if (!string.IsNullOrWhiteSpace(activityName)) return activityName!;

        if (string.IsNullOrWhiteSpace(implicitActivitySourceName))
            implicitActivitySourceName = invocation.TargetType.Name;

        return $"{implicitActivitySourceName}.{invocation.Method.Name}";
    }

    private ActivitySource GetActivitySource(Type type, string? implicitActivitySourceName = null) =>
        _activitySources.GetOrAdd(type, static (type, name) =>
            new(ActivitySourceAttribute.GetActivitySourceName(type, name),
                type.Assembly.GetName().Version?.ToString() ?? ""), implicitActivitySourceName);

    private static (Action<IInvocation, Activity>?, Action<IInvocation, Activity>?) SetActivityTags(Type type,
        MethodInfo method, out string? returnValueTagName)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");
        var activity = Expression.Parameter(typeof(Activity), "activity");

        var activityTags = GetActivityTags(type, method,
            method.GetCustomAttribute<ActivityAttribute>()?.Tags?.ToList(), invocation, out returnValueTagName);

        var activityInTags = activityTags.Where(static t => t.Direction.HasFlag(SetTagPosition.Start)).ToList();
        var activityOutTags = activityTags.Where(static t => t.Direction.HasFlag(SetTagPosition.End)).ToList();

        var start = activityInTags.Count < 1
            ? null
            : Expression.Lambda<Action<IInvocation, Activity>>(Expression.Block(activityInTags.Select(kv =>
            {
                var value = kv.Value;
                if (value.Type.IsValueType || value.Type.IsGenericParameter)
                    value = Expression.Convert(value, typeof(object));

                return Expression.Call(SetTagEnumerable, activity, Expression.Constant(kv.Key), value);
            })), invocation, activity);

        var end = activityOutTags.Count < 1
            ? null
            : Expression.Lambda<Action<IInvocation, Activity>>(Expression.Block(activityOutTags.Select(kv =>
            {
                var value = kv.Value;
                if (value.Type.IsValueType || value.Type.IsGenericParameter)
                    value = Expression.Convert(value, typeof(object));

                var key = kv.Direction.HasFlag(SetTagPosition.Start) ? kv.Key + "$out" : kv.Key;

                return Expression.Call(SetTagEnumerable, activity, Expression.Constant(key), value);
            })), invocation, activity);

        return (start?.Compile(), end?.Compile());
    }

    private static GetTags? CreateActivityTags(Type type, MethodInfo method)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var activityTags = GetActivityTags(type, method, (method.GetCustomAttribute<ActivityNameAttribute>() ??
                method.DeclaringType?.GetCustomAttribute<ActivityNameAttribute>())?.Tags?.ToList(), invocation, out _)
            .Where(static activityTag => activityTag.Direction.HasFlag(SetTagPosition.Start)).ToList();

        var start = activityTags.Count < 1
            ? null
            : Expression.Lambda<GetTags>(Expression.NewArrayInit(typeof(KeyValuePair<string, object>),
                activityTags.Select(static kv =>
                {
                    var value = kv.Value;
                    if (value.Type.IsValueType || value.Type.IsGenericParameter)
                        value = Expression.Convert(value, typeof(object));

                    return Expression.New(KeyValuePairCtor, Expression.Constant(kv.Key), value);
                })), invocation);

        return start?.Compile();
    }

    internal static List<ActivityTag> GetActivityTags(Type type, MethodInfo method, List<string>? tags,
        Expression invocation, out string? returnValueTagName)
    {
        if (method.ReturnType != typeof(void) &&
            (!CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo) ||
                awaitableInfo.AwaitableInfo.AwaiterGetResultMethod.ReturnType != typeof(void)))
            TryGetName(tags, method.ReturnParameter?.GetCustomAttribute<ActivityTagAttribute>(true),
                ActivityTagAttribute.ReturnValueTagName, out returnValueTagName);
        else returnValueTagName = null;

        var list = new List<ActivityTag>();

        const BindingFlags bindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var target = Expression.Convert(Expression.Property(invocation, "InvocationTarget"), type);

        foreach (var field in type.GetFields(bindingFlags))
            if (TryGetName(tags, field.GetCustomAttribute<ActivityTagAttribute>(true),
                    field.Name, out var name))
                list.Add(new(name, Expression.Field(field.IsStatic ? null : target, field)));

        foreach (var property in type.GetProperties(bindingFlags))
            if (property.GetMethod != null && TryGetName(tags, property.GetCustomAttribute<ActivityTagAttribute>(true),
                    property.Name, out var name))
                list.Add(new(name, Expression.Property(property.GetMethod.IsStatic ? null : target, property)));

        var index = 0;
        foreach (var parameter in method.GetParameters())
        {
            if (TryGetName(tags, parameter.GetCustomAttribute<ActivityTagAttribute>(true),
                    parameter.Name ?? index.ToString(CultureInfo.InvariantCulture), out var name))
            {
                list.Add(new(name, Expression.Call(invocation, GetArgumentValue, Expression.Constant(index)),
                    parameter.IsOut
                        ? SetTagPosition.End
                        : parameter is { IsIn: false, ParameterType.IsByRef: true }
                            ? SetTagPosition.All
                            : SetTagPosition.Start));
            }

            index++;
        }

        return list;
    }

    private static bool TryGetName(List<string>? tags, ActivityTagAttribute? attr, string memberName,
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

[Flags]
internal enum SetTagPosition { Start = 1, End = 2, All = Start | End }

internal record struct ActivityTag(string Key, Expression Value, SetTagPosition Direction = SetTagPosition.Start);
