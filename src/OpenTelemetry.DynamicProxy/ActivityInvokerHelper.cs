using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace OpenTelemetry.DynamicProxy;

internal static class ActivityInvokerHelper
{
    private static readonly ConcurrentDictionary<Type, Func<object, ObjectMethodExecutorAwaitable>?> Factories = [];

    public static IActivityInvoker Noop { get; } = new NoopActivityInvoker();

    /// <returns>0: Not a activity</returns>
    public static ActivitySettings GetActivityName(MethodInfo method, Type type, out string? activityName,
        out ActivityKind kind, out int maxUsableTimes)
    {
        activityName = null;
        kind = ActivityKind.Internal;
        maxUsableTimes = 0;

        if (method.GetCustomAttribute<NonActivityAttribute>(true) is { } naa)
            return naa.SuppressInstrumentation
                ? ActivitySettings.SuppressInstrumentation
                : ActivitySettings.None;

        if (method.GetCustomAttribute<ActivityAttribute>(true) is { } attr)
        {
            activityName = attr.ActivityName;

            kind = attr.Kind;

            return attr.SuppressInstrumentation
                ? ActivitySettings.ActivityAndSuppressInstrumentation
                : ActivitySettings.Activity;
        }

        if (method.GetCustomAttribute<ActivityNameAttribute>(true) is not { } ana || ana.MaxUsableTimes == 0)
        {
            if (type.GetCustomAttribute<ActivitySourceAttribute>(true) is { } asa)
            {
                kind = asa.Kind;

                return asa.IncludeNonAsyncStateMachineMethod ||
                    (type.IsInterface || method.IsDefined(typeof(AsyncStateMachineAttribute), false)) &&
                    CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out _)
                        ? asa.SuppressInstrumentation
                            ? ActivitySettings.ActivityAndSuppressInstrumentation
                            : ActivitySettings.Activity
                        : ActivitySettings.None;
            }

            ana = type.GetCustomAttribute<ActivityNameAttribute>(true);

            if (ana == null || ana.MaxUsableTimes == 0) return ActivitySettings.None;

            if (!string.IsNullOrWhiteSpace(ana.ActivityName))
                activityName = $"{ana.ActivityName}.{method.Name}";
        }
        else activityName = ana.ActivityName;

        maxUsableTimes = ana.MaxUsableTimes;

        return maxUsableTimes == 0 ? ActivitySettings.None : ActivitySettings.ActivityName;
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
