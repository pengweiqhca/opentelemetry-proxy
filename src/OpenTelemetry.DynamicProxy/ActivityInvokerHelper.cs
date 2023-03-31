using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace OpenTelemetry.DynamicProxy;

internal static class ActivityInvokerHelper
{
    private static readonly ConcurrentDictionary<Type, Func<object, ObjectMethodExecutorAwaitable>?> Factories = new();

    public static IActivityInvoker Noop { get; } = new NoopActivityInvoker();

    private static readonly ConcurrentDictionary<Assembly, bool> IsFodyProcessedAssembly = new();

    /// <returns>0: Not a activity</returns>
    public static ActivitySettings GetActivityName(MethodInfo method, Type type, out string? activityName,
        out ActivityKind kind, out int maxUsableTimes)
    {
        activityName = null;
        kind = ActivityKind.Internal;
        maxUsableTimes = 0;

        // If has processed by fody, invoke directly.
        if (IsFodyProcessedAssembly.GetOrAdd(method.Module.Assembly, static assembly => assembly.GetType(
                $"{assembly.ToString().Split(new[] { ',' }, 2)[0].Replace(".", "")}_ProcessedByFody") != null))
            return ActivitySettings.NonActivity;

        if (method.GetCustomAttribute<NonActivityAttribute>(true) is { } naa)
            return naa.SuppressInstrumentation
                ? ActivitySettings.NonActivityAndSuppressInstrumentation
                : ActivitySettings.NonActivity;

        if (method.GetCustomAttribute<ActivityAttribute>(true) is { } attr)
        {
            activityName = attr.ActivityName;

            kind = attr.Kind;

            return ActivitySettings.Activity;
        }

        if (method.GetCustomAttribute<ActivityNameAttribute>(true) is not { } ana || ana.MaxUsableTimes < 1)
        {
            if (type.GetCustomAttribute<ActivitySourceAttribute>(true) is { } asa)
            {
                kind = asa.Kind;

                return asa.IncludeNonAsyncStateMachineMethod ||
                    method.IsDefined(typeof(AsyncStateMachineAttribute), false) &&
                    CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out _)
                        ? ActivitySettings.Activity
                        : ActivitySettings.NonActivity;
            }

            ana = type.GetCustomAttribute<ActivityNameAttribute>(true);

            if (ana == null || ana.MaxUsableTimes < 1) return ActivitySettings.NonActivity;

            if (!string.IsNullOrWhiteSpace(ana.ActivityName))
                activityName = $"{ana.ActivityName}.{method.Name}";
        }
        else activityName = ana.ActivityName;

        maxUsableTimes = ana.MaxUsableTimes;

        return maxUsableTimes == 0 ? ActivitySettings.NonActivity : ActivitySettings.ActivityNameOnly;
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
