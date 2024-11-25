using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using GetTags =
    System.Func<Castle.DynamicProxy.IInvocation, System.Collections.Generic.IReadOnlyCollection<
        System.Collections.Generic.KeyValuePair<string, object?>>?>;
using Tags =
    (System.Collections.Generic.Dictionary<OpenTelemetry.DynamicProxy.ActivityTag, System.Linq.Expressions.Expression> In,
    System.Collections.Generic.Dictionary<OpenTelemetry.DynamicProxy.ActivityTag, System.Linq.Expressions.Expression> Out,
    System.Tuple<System.Collections.Generic.HashSet<OpenTelemetry.DynamicProxy.ActivityTag>, System.Type>? Return);

namespace OpenTelemetry.DynamicProxy;

/// <summary>Instance should be singleton.</summary>
public class ActivityInvokerFactory(IExpressionParser? parser = null) : IActivityInvokerFactory, IDisposable
{
    private readonly IExpressionParser _parser = parser ?? new DefaultExpressionParser();

    private static readonly ConstructorInfo KeyValuePairCtor =
        typeof(KeyValuePair<string, object?>).GetConstructors().Single();

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
        // If it has been processed by metalama, invoke directly.
        if (type.IsDefined(typeof(ProxyHasGeneratedAttribute)) && !type.IsInterface) return null;

        var proxyMethod = ActivityInvokerHelper.GetProxyMethod(method, type);

        if (proxyMethod is ActivityMethod activityMethod)
            return new ActivityInvoker(GetActivitySource(type), activityMethod.ActivityName, activityMethod.Kind,
                activityMethod.SuppressInstrumentation,
                SetActivityTags(type, method, out var setReturnValueTag), setReturnValueTag);

        if (proxyMethod is ActivityNameMethod activityNameMethod)
            return new ActivityNameInvoker(activityNameMethod.ActivityName, activityNameMethod.AdjustStartTime,
                CreateActivityTags(type, method));

        if (proxyMethod is SuppressInstrumentationMethod) return new ActivityNameInvoker();

        if (context.Type == ImplicitActivityType.Activity)
            return context.BeforeProceed != null || context.AfterProceed != null
                ? new ActivityInvoker(GetActivitySource(type, context.ActivitySourceName),
                    ActivityInvokerHelper.GetActivityName(method, type, null, context.ActivityBaseName),
                    context.ActivityKind, context.SuppressInstrumentation,
                    (context.BeforeProceed, context.AfterProceed), context.SetReturnValueTag)
                : new(GetActivitySource(type, context.ActivitySourceName),
                    ActivityInvokerHelper.GetActivityName(method, type, null, context.ActivityBaseName),
                    context.ActivityKind, context.SuppressInstrumentation,
                    SetActivityTags(type, method, out var setReturnValueTag),
                    context.SetReturnValueTag ?? setReturnValueTag);

        return context.Type == ImplicitActivityType.ActivityName
            ? new ActivityNameInvoker(ActivityInvokerHelper.GetActivityName(method, type, null), false,
                CreateActivityTags(type, method))
            : null;
    }

    private ActivitySource GetActivitySource(Type type, string? activitySourceName = null) =>
        _activitySources.GetOrAdd(type, static (type, name) =>
            new(ActivitySourceAttribute.GetActivitySourceName(type, name),
                type.Assembly.GetName().Version?.ToString()), activitySourceName);

    private (Action<IInvocation, Activity>?, Action<IInvocation, Activity>?) SetActivityTags(Type type,
        MethodInfo method, out Action<Activity, object>? setReturnValueTag)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");
        var activity = Expression.Parameter(typeof(Activity), "activity");

        var (activityInTags, activityOutTags, returnTags) =
            GetActivityTags(type, method, GetTags(method, type), invocation);

        var start = activityInTags.Count < 1
            ? null
            : Expression.Lambda<Action<IInvocation, Activity>>(Expression.Block(activityInTags.Select(kv =>
                    ExpressionHelper.SetTag(_parser, activity, kv.Key.TagName, kv.Key.Expression, kv.Value))),
                invocation, activity);

        var end = activityOutTags.Count < 1
            ? null
            : Expression.Lambda<Action<IInvocation, Activity>>(Expression.Block(activityOutTags.Select(kv =>
                ExpressionHelper.SetTag(_parser, activity,
                    activityInTags.ContainsKey(kv.Key) ? kv.Key.TagName + "$out" : kv.Key.TagName,
                    kv.Key.Expression, kv.Value))), invocation, activity);

        if (returnTags?.Item1 == null || returnTags.Item1.Count < 1) setReturnValueTag = null;
        else
        {
            var ret = Expression.Parameter(typeof(object));

            setReturnValueTag = Expression.Lambda<Action<Activity, object>>(Expression.Block(returnTags.Item1.Select(
                tag => ExpressionHelper.SetTag(activity, tag.TagName, ExpressionHelper.IsExpression(tag.Expression)
                    ? ExpressionHelper.ConvertToObject(_parser.Parse(Expression.Convert(ret, returnTags.Item2),
                        tag.Expression))
                    : ret))), activity, ret).Compile();
        }

        return (start?.Compile(), end?.Compile());
    }

    private GetTags? CreateActivityTags(Type type, MethodInfo method)
    {
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var (activityTags, _, _) = GetActivityTags(type, method, GetTags(method, type), invocation);

        var start = activityTags.Count < 1
            ? null
            : Expression.Lambda<GetTags>(Expression.NewArrayInit(typeof(KeyValuePair<string, object>),
                activityTags.Select(kv => Expression.New(KeyValuePairCtor, Expression.Constant(kv.Key.TagName),
                    ExpressionHelper.ParseExpression(_parser, kv.Key.Expression, kv.Value)))), invocation);

        return start?.Compile();
    }

    internal static Dictionary<ActivityTag, string> GetTags(MethodInfo method, Type? type)
    {
        var tags1 = method.GetCustomAttribute<ActivityTagsAttribute>()?.Tags;
        var tags2 = type?.GetCustomAttribute<ActivityTagsAttribute>()?.Tags;

        var dic = new Dictionary<ActivityTag, string>();

        return tags1 == null || tags1.Length < 1
            ? tags2 == null ? dic : Convert(dic, ActivityTag.Parse(tags2))
            : tags2 == null || tags2.Length < 1
                ? Convert(dic, ActivityTag.Parse(tags1))
                : Convert(Convert(dic, ActivityTag.Parse(tags1)), ActivityTag.Parse(tags2));

        static Dictionary<ActivityTag, string> Convert(Dictionary<ActivityTag, string> dic,
            IEnumerable<Tuple<ActivityTag, string>> tags)
        {
            foreach (var (tag, value) in tags) dic[tag] = value;

            return dic;
        }
    }

    internal static Tags GetActivityTags(Type type, MethodInfo method, Dictionary<ActivityTag, string> tags,
        Expression invocation)
    {
        Type? returnType = null;
        if (method.ReturnType != typeof(void))
        {
            if (CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo))
            {
                returnType = awaitableInfo.AwaitableInfo.AwaiterGetResultMethod.ReturnType;

                if (returnType == typeof(void)) returnType = null;
            }
            else returnType = method.ReturnType;
        }

        var inTags = new Dictionary<ActivityTag, Expression>();
        var outTags = new Dictionary<ActivityTag, Expression>();
        var returnTags = returnType == null ? null : new HashSet<ActivityTag>();

        if (returnTags != null)
            foreach (var tag in GetActivityTags("$returnvalue",
                         method.ReturnParameter?.GetCustomAttributes<ActivityTagAttribute>() ?? []))
            {
                tags.Remove(tag);
                returnTags.Add(tag);
            }

        const BindingFlags bindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var target = Expression.Convert(Expression.Property(invocation, "InvocationTarget"), type);

        var index = 0;
        foreach (var parameter in method.GetParameters())
        {
            var parameterName = parameter.Name;

            var expression = Expression.Call(invocation, GetArgumentValue, Expression.Constant(index));

            foreach (var tag in GetActivityTags(string.IsNullOrEmpty(parameterName) ? "p@" + index : parameterName,
                             parameter.GetCustomAttributes<ActivityTagAttribute>())
                         .Union(tags.Where(kv => kv.Value == parameterName).Select(kv => kv.Key)).ToList())
            {
                if (parameter.IsOut) outTags[tag] = expression;
                else if (parameter is { IsIn: false, ParameterType.IsByRef: true })
                {
                    inTags[tag] = expression;
                    outTags[tag] = expression;
                }
                else inTags[tag] = expression;

                tags.Remove(tag);
            }

            index++;
        }

        foreach (var tag in tags.ToArray())
            if (type.GetProperty(tag.Value, bindingFlags) is { } property)
            {
                if (property.GetMethod != null)
                    inTags[tag.Key] = Expression.Property(property.GetMethod.IsStatic ? null : target, property);
            }
            else if (type.GetField(tag.Value, bindingFlags) is { } field)
                inTags[tag.Key] = Expression.Field(field.IsStatic ? null : target, field);

        returnTags?.UnionWith(tags.Where(kv => kv.Value == "$returnvalue").Select(x => x.Key));

        return (inTags, outTags, returnType == null ? null : new(returnTags!, returnType));
    }

    private static IEnumerable<ActivityTag> GetActivityTags(string memberName,
        IEnumerable<ActivityTagAttribute> attributes) => attributes.Select(attr =>
        new ActivityTag(string.IsNullOrWhiteSpace(attr.Name) ? memberName : attr.Name!, attr.Expression));

    public void Dispose()
    {
        _activityInvokers.Clear();

        foreach (var activitySource in _activitySources.Values) activitySource.Dispose();

        _activitySources.Clear();

        GC.SuppressFinalize(this);
    }
}
