namespace OpenTelemetry.DynamicProxy;

internal static class ActivityInvokerHelper
{
    public static IActivityInvoker Noop { get; } = new NoopActivityInvoker();

    /// <returns>0: Not a activity</returns>
    public static ActivitySettings GetActivityName(MethodInfo method, Type type, out string? activityName, out ActivityKind kind, out int maxUseableTimes)
    {
        activityName = null;
        kind = ActivityKind.Internal;
        maxUseableTimes = 0;

        var naa = method.GetCustomAttribute<NonActivityAttribute>(true);

        if (naa != null)
            return naa.SuppressInstrumentation
                ? ActivitySettings.NonActivityAndSuppressInstrumentation
                : ActivitySettings.NonActivity;

        var attr = method.GetCustomAttribute<ActivityAttribute>(true);

        if (attr != null)
        {
            activityName = attr.ActivityName;

            kind = attr.Kind;

            return ActivitySettings.Activity;
        }

        var asa = type.GetCustomAttribute<ActivitySourceAttribute>(true);

        if (asa != null)
        {
            kind = asa.Kind;

            return ActivitySettings.Activity;
        }

        var ana = method.GetCustomAttribute<ActivityNameAttribute>(true);
        if (ana == null)
        {
            ana = type.GetCustomAttribute<ActivityNameAttribute>(true);

            if (ana == null) return ActivitySettings.NonActivity;

            if (!string.IsNullOrWhiteSpace(ana.ActivityName))
                activityName = $"{ana.ActivityName}.{method.Name}";
        }
        else
            activityName = ana.ActivityName;

        maxUseableTimes = ana.MaxUseableTimes;

        return ActivitySettings.ActivityNameOnly;
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

    public static Type GetActivityNameInvokerType(Type returnType)
    {
        var type = returnType;

        if (type == typeof(Task)) return typeof(TaskActivityNameInvoker);
        if (type == typeof(ValueTask)) return typeof(ValueTaskActivityNameInvoker);
        if (!type.IsGenericType) return typeof(ActivityNameInvoker);

        type = type.GetGenericTypeDefinition();

        if (type == typeof(Task<>)) type = typeof(TaskActivityNameInvoker<>);
        else if (type == typeof(ValueTask<>)) type = typeof(ValueTaskActivityNameInvoker<>);
        else if (type == typeof(IAsyncEnumerable<>)) type = typeof(AsyncStreamActivityNameInvoker<>);
        else return typeof(ActivityNameInvoker);

        return type.MakeGenericType(returnType.GetGenericArguments()[0]);
    }
}
