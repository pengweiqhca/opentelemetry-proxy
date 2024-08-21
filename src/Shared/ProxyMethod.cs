namespace OpenTelemetry;

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
#if Fody
    int kind,
#else
    ActivityKind kind,
#endif
    bool suppressInstrumentation) : IProxyMethod
{
    public string ActivityName { get; } = activityName;
#if Fody
    public int Kind { get; } = kind;
#else
    public ActivityKind Kind { get; } = kind;
#endif
    public bool SuppressInstrumentation { get; } = suppressInstrumentation;
}

internal record ProxyType<T> where T : notnull
{
    private readonly Dictionary<T, IProxyMethod> _methods;

    public ProxyType() => _methods = [];

    public string? ActivitySourceName { get; init; }

    public IReadOnlyDictionary<T, IProxyMethod> Methods => _methods;

    public void AddMethod(T key, IProxyMethod? method)
    {
        if (method != null) _methods[key] = method;
    }
}
