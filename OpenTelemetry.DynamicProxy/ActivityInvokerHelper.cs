namespace OpenTelemetry.DynamicProxy;

internal static class ActivityInvokerHelper
{
    public static IActivityInvoker Noop { get; } = new NoopActivityInvoker();

    public static bool TryGetActivityName(MethodInfo method, Type type, out string? activityName, out ActivityKind kind)
    {
        activityName = null;
        kind = ActivityKind.Internal;

        if (method.GetCustomAttribute<NonActivityAttribute>(true) != null) return false;

        var attr = method.GetCustomAttribute<ActivityAttribute>(true);

        if (attr != null)
        {
            activityName = attr.ActivityName;

            kind = attr.Kind;
        }
        else if (type.GetCustomAttribute<ActivitySourceAttribute>(true) == null)
            return false;

        return true;
    }

    public static Type GetActivityInvokerType(Type returnType)
    {
        var type = returnType;

        if (type == typeof(Task)) return typeof(TaskActivityInvoker);
        if (type == typeof(ValueTask)) return typeof(ValueTaskActivityInvoker);
        if (!type.IsGenericType) return typeof(ActivityInvoker);

        type = type.GetGenericTypeDefinition();

        if (type == typeof(Task<>)) type = typeof(TaskActivityInvoker<>);
        else if (type == typeof(ValueTask<>)) type = typeof(ValueTaskActivityInvoker<>);
        else if (type == typeof(IAsyncEnumerable<>)) type = typeof(AsyncStreamActivityInvoker<>);
        else return typeof(ActivityInvoker);

        return type.MakeGenericType(returnType.GetGenericArguments()[0]);
    }
}
