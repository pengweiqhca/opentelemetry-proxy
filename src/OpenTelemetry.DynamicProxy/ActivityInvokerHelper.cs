using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace OpenTelemetry.DynamicProxy;

internal static class ActivityInvokerHelper
{
    private static readonly ConcurrentDictionary<Type, Func<object, ObjectMethodExecutorAwaitable>?> Factories = [];

    public static IActivityInvoker Noop { get; } = new NoopActivityInvoker();

    /// <returns>0: Not an activity</returns>
    public static IProxyMethod? GetProxyMethod(MethodInfo method, Type type)
    {
        if (method.GetCustomAttribute<NonActivityAttribute>() is { } naa)
            return naa.SuppressInstrumentation
                ? SuppressInstrumentationMethod.Instance
                : null;

        if (method.GetCustomAttribute<ActivityAttribute>() is { } attr)
            return new ActivityMethod(GetActivityName(method, type, attr.ActivityName), attr.Kind,
                attr.SuppressInstrumentation);

        if (method.GetCustomAttribute<ActivityNameAttribute>() is not { } ana || ana.MaxUsableTimes == 0)
        {
            if (type.GetCustomAttribute<ActivitySourceAttribute>() is { } asa)
            {
                if (asa.IncludeNonAsyncStateMachineMethod ||
                    (type.IsInterface || method.IsDefined(typeof(AsyncStateMachineAttribute), false)) &&
                    CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out _))
                    return new ActivityMethod(GetActivityName(method, type, null, asa.ActivitySourceName), asa.Kind,
                        asa.SuppressInstrumentation);

                return null;
            }

            ana = type.GetCustomAttribute<ActivityNameAttribute>();

            return ana == null || ana.MaxUsableTimes == 0
                ? null
                : new ActivityNameMethod(GetActivityName(method, type, null, ana.ActivityName), ana.MaxUsableTimes);
        }

        return new ActivityNameMethod(GetActivityName(method, type, ana.ActivityName,
            type.GetCustomAttribute<ActivitySourceAttribute>() == null
                ? type.GetCustomAttribute<ActivityNameAttribute>()?.ActivityName
                : null), ana.MaxUsableTimes);
    }

    public static string GetActivityName(MethodInfo method, Type type, string? activityName,
        string? activityBaseName = null)
    {
        if (!string.IsNullOrWhiteSpace(activityName)) return activityName!;

        if (string.IsNullOrWhiteSpace(activityBaseName)) activityBaseName = type.Name;

        return $"{activityBaseName}.{method.Name}";
    }

    public static Func<object, ObjectMethodExecutorAwaitable>? Convert(Type returnType) =>
        Factories.GetOrAdd(returnType, static type =>
        {
            if (!CoercedAwaitableInfo.IsTypeAwaitable(type, out var coercedAwaitableInfo)) return null;

            var param = Expression.Parameter(typeof(object));

            return Expression.Lambda<Func<object, ObjectMethodExecutorAwaitable>>(
                ObjectMethodExecutor.ConvertToObjectMethodExecutorAwaitable(coercedAwaitableInfo,
                    Expression.Convert(param, type)), param).Compile();
        });
}

internal interface IProxyMethod;

internal sealed class SuppressInstrumentationMethod : IProxyMethod
{
    private SuppressInstrumentationMethod() { }

    public static SuppressInstrumentationMethod Instance { get; } = new();
}

internal sealed class ActivityNameMethod(string activityName, int maxUsableTimes) : IProxyMethod
{
    public string ActivityName { get; } = activityName;

    public int MaxUsableTimes { get; } = maxUsableTimes;
}

internal sealed class ActivityMethod(
    string activityName,
    ActivityKind kind,
    bool suppressInstrumentation) : IProxyMethod
{
    public string ActivityName { get; } = activityName;

    public ActivityKind Kind { get; } = kind;

    public bool SuppressInstrumentation { get; } = suppressInstrumentation;
}
