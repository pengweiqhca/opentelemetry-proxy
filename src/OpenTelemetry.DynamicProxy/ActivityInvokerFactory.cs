using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Diagnostics.CodeAnalysis;
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

    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<MethodInfo, IActivityInvoker?>> _activityInvokers =
        [];

    private readonly ConcurrentDictionary<Type, ActivitySource> _activitySources = [];

    public IActivityInvoker Create(IInvocation invocation, ImplicitActivityContext context)
    {
        IActivityInvoker? invoker = null;

        if (invocation.Method.IsSpecialName) invoker = ActivityInvokerHelper.Noop;
        else if ((invocation.Method.DeclaringType ??
                     invocation.Method.ReflectedType ?? invocation.TargetType) is { } type)
        {
            if (!_activityInvokers.TryGetValue(type, out var activityInvokers))
                _activityInvokers[type] = activityInvokers = [];

            // CreateInterfaceProxyWithTarget, CreateInterfaceProxyWithTargetInterface, CreateClassProxyWithTarget
            if (invocation.TargetType != null && invocation.MethodInvocationTarget != null)
                invoker = GetOrCreateActivityInvoker(activityInvokers,
                    invocation.MethodInvocationTarget.DeclaringType ??
                    invocation.MethodInvocationTarget.ReflectedType ?? invocation.TargetType,
                    invocation.MethodInvocationTarget, invocation.MethodInvocationTarget, context);

            invoker ??= GetOrCreateActivityInvoker(activityInvokers, type,
                invocation.MethodInvocationTarget ?? invocation.Method, invocation.Method, context);
        }

        return invoker ?? ActivityInvokerHelper.Noop;
    }

    IActivityInvoker? GetOrCreateActivityInvoker(ConcurrentDictionary<MethodInfo, IActivityInvoker?> activityInvokers,
        Type type, MethodInfo targetMethod, MethodInfo method, ImplicitActivityContext context)
    {
        if (activityInvokers.TryGetValue(targetMethod, out var invoker) && targetMethod == method ||
            targetMethod != method && activityInvokers.TryGetValue(method, out invoker) && invoker != null)
            return invoker;

        invoker = CreateActivityInvoker(type, method, context);

        try
        {
            activityInvokers[targetMethod] = invoker;
        }
        catch (NullReferenceException) { }

        return invoker;
    }

    private IActivityInvoker? CreateActivityInvoker(Type type, MethodInfo method, ImplicitActivityContext context)
    {
        // If it has been processed by fody, invoke directly.
        if (type.Assembly.IsDefined(typeof(ProxyHasGeneratedAttribute))) return null;

        var proxyMethod = ActivityInvokerHelper.GetProxyMethod(method, type);

        if (proxyMethod is ActivityMethod activityMethod)
            return new ActivityInvoker(GetActivitySource(type), activityMethod.ActivityName, activityMethod.Kind,
                activityMethod.SuppressInstrumentation,
                SetActivityTags(type, method, out var returnValueTagName), returnValueTagName);

        if (proxyMethod is ActivityNameMethod activityNameMethod)
            return new ActivityNameInvoker(activityNameMethod.ActivityName, activityNameMethod.MaxUsableTimes,
                CreateActivityTags(type, method));

        if (proxyMethod is SuppressInstrumentationMethod) return new ActivityNameInvoker();

        if (context.Type == ImplicitActivityType.Activity)
            return context.BeforeProceed != null || context.AfterProceed != null
                ? new ActivityInvoker(GetActivitySource(type, context.ActivitySourceName),
                    ActivityInvokerHelper.GetActivityName(method, type, null, context.ActivityBaseName),
                    context.ActivityKind, context.SuppressInstrumentation,
                    (context.BeforeProceed, context.AfterProceed), context.ReturnValueTagName)
                : new(GetActivitySource(type, context.ActivitySourceName),
                    ActivityInvokerHelper.GetActivityName(method, type, null, context.ActivityBaseName),
                    context.ActivityKind, context.SuppressInstrumentation,
                    SetActivityTags(type, method, out var returnValueTagName),
                    context.ReturnValueTagName ?? returnValueTagName);

        return context.Type == ImplicitActivityType.ActivityName
            ? new ActivityNameInvoker(ActivityInvokerHelper.GetActivityName(method, type, null), 1,
                CreateActivityTags(type, method))
            : null;
    }

    private ActivitySource GetActivitySource(Type type, string? activitySourceName = null) =>
        _activitySources.GetOrAdd(type, static (type, name) =>
            new(ActivitySourceAttribute.GetActivitySourceName(type, name),
                type.Assembly.GetName().Version?.ToString()), activitySourceName);

    private static (Action<IInvocation, Activity>?, Action<IInvocation, Activity>?) SetActivityTags(Type type,
        MethodInfo method, out string? returnValueTagName)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");
        var activity = Expression.Parameter(typeof(Activity), "activity");

        var activityTags = GetActivityTags(type, method,
            GetTags(method.GetCustomAttribute<ActivityTagsAttribute>(),
                type.GetCustomAttribute<ActivityTagsAttribute>()),
            invocation, out returnValueTagName);

        var activityInTags = activityTags.Where(static t => t.Value.Direction.HasFlag(TagPosition.Start)).ToList();
        var activityOutTags = activityTags.Where(static t => t.Value.Direction.HasFlag(TagPosition.End)).ToList();

        var start = activityInTags.Count < 1
            ? null
            : Expression.Lambda<Action<IInvocation, Activity>>(Expression.Block(activityInTags.Select(kv =>
            {
                var value = kv.Value.Value;
                if (value.Type.IsValueType || value.Type.IsGenericParameter)
                    value = Expression.Convert(value, typeof(object));

                return Expression.Call(SetTagEnumerable, activity, Expression.Constant(kv.Key), value);
            })), invocation, activity);

        var end = activityOutTags.Count < 1
            ? null
            : Expression.Lambda<Action<IInvocation, Activity>>(Expression.Block(activityOutTags.Select(kv =>
            {
                var value = kv.Value.Value;
                if (value.Type.IsValueType || value.Type.IsGenericParameter)
                    value = Expression.Convert(value, typeof(object));

                var key = kv.Value.Direction.HasFlag(TagPosition.Start) ? kv.Key + "$out" : kv.Key;

                return Expression.Call(SetTagEnumerable, activity, Expression.Constant(key), value);
            })), invocation, activity);

        return (start?.Compile(), end?.Compile());
    }

    private static GetTags? CreateActivityTags(Type type, MethodInfo method)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var activityTags = GetActivityTags(type, method, GetTags(method.GetCustomAttribute<ActivityTagsAttribute>(),
                method.DeclaringType?.GetCustomAttribute<ActivityTagsAttribute>()), invocation, out _)
            .Where(static activityTag => activityTag.Value.Direction.HasFlag(TagPosition.Start)).ToList();

        var start = activityTags.Count < 1
            ? null
            : Expression.Lambda<GetTags>(Expression.NewArrayInit(typeof(KeyValuePair<string, object>),
                activityTags.Select(static kv =>
                {
                    var value = kv.Value.Value;
                    if (value.Type.IsValueType || value.Type.IsGenericParameter)
                        value = Expression.Convert(value, typeof(object));

                    return Expression.New(KeyValuePairCtor, Expression.Constant(kv.Key), value);
                })), invocation);

        return start?.Compile();
    }

    public static HashSet<string> GetTags(ActivityTagsAttribute? methodTags, ActivityTagsAttribute? typeTags)
    {
        var tags1 = methodTags?.Tags;
        var tags2 = typeTags?.Tags;

        if (tags1 == null || tags1.Length < 1) return tags2 == null ? [] : [..tags2];

        return tags2 == null || tags2.Length < 1 ? [..tags1] : [..tags1, ..tags2];
    }

    internal static Dictionary<string, ActivityTagValue> GetActivityTags(Type type, MethodInfo method,
        HashSet<string> tags, Expression invocation, out string? returnValueTagName)
    {
        var isVoid = !(method.ReturnType != typeof(void) &&
            (!CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo) ||
                awaitableInfo.AwaitableInfo.AwaiterGetResultMethod.ReturnType != typeof(void)));

        if (isVoid) returnValueTagName = null;
        else
        {
            isVoid = false;

            var attr = method.ReturnParameter?.GetCustomAttribute<ActivityTagAttribute>();

            if (attr == null) returnValueTagName = null;
            else
            {
                returnValueTagName = string.IsNullOrWhiteSpace(attr.Name) ? "$returnvalue" : attr.Name!;

                tags.Remove(returnValueTagName);
            }
        }

        var list = new Dictionary<string, ActivityTagValue>();

        const BindingFlags bindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var target = Expression.Convert(Expression.Property(invocation, "InvocationTarget"), type);

        var index = 0;
        foreach (var parameter in method.GetParameters())
        {
            if (TryGetName(tags, parameter.GetCustomAttribute<ActivityTagAttribute>(),
                    parameter.Name, out var name))
                list[name] = new(Expression.Call(invocation, GetArgumentValue, Expression.Constant(index)),
                    GetTagPosition(parameter));

            index++;
        }

        foreach (var tag in tags.ToArray())
            if (type.GetProperty(tag, bindingFlags) is { } property)
            {
                if (property.GetMethod != null && TryGetName(tags, null, property.Name, out var name))
                    list[name] = new(Expression.Property(property.GetMethod.IsStatic ? null : target, property));
            }
            else if (type.GetField(tag, bindingFlags) is { } field)
                if (TryGetName(tags, null, field.Name, out var name))
                    list[name] = new(Expression.Field(field.IsStatic ? null : target, field));

        if (!isVoid && string.IsNullOrWhiteSpace(returnValueTagName) && tags.Remove("$returnvalue"))
            returnValueTagName = "$returnvalue";

        return list;

        static TagPosition GetTagPosition(ParameterInfo parameter) => parameter.IsOut
            ? TagPosition.End
            : parameter is { IsIn: false, ParameterType.IsByRef: true }
                ? TagPosition.All
                : TagPosition.Start;
    }

    private static bool TryGetName(HashSet<string> tags, ActivityTagAttribute? attr, string? memberName,
        [NotNullWhen(true)] out string? name)
    {
        name = null;

        if (attr == null)
        {
            if (string.IsNullOrWhiteSpace(memberName) || !tags.Contains(memberName!)) return false;

            name = memberName;
        }
        else name = string.IsNullOrWhiteSpace(attr.Name) ? memberName : attr.Name!;

        if (!string.IsNullOrWhiteSpace(memberName)) tags.Remove(memberName!);

        return !string.IsNullOrWhiteSpace(name);
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
internal enum TagPosition { Start = 1, End = 2, All = Start | End }

internal record struct ActivityTagValue(Expression Value, TagPosition Direction = TagPosition.Start);
