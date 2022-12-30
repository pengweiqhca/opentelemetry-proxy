namespace OpenTelemetry.Proxy;

internal record ProxyMethod(ActivitySettings ActivitySettings, string? ActivityName = null, int ActivityKind = 0,
    int MaxUseableTimes = 0);

internal record ProxyType<T>
{
    private readonly Dictionary<T, ProxyMethod> _methods;

    public ProxyType() => _methods = new();

    public ProxyType(IEqualityComparer<T> comparer) => _methods = new(comparer);

    public string? ActivitySourceName { get; init; }

    public IReadOnlyDictionary<T, ProxyMethod> Methods => _methods;

    public void AddMethod(T key, ProxyMethod method)
    {
        if (method.ActivitySettings != ActivitySettings.NonActivity) _methods[key] = method;
    }
}
