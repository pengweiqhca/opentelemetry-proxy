namespace OpenTelemetry.Proxy;

internal record ProxyMethod(ActivitySettings Settings, string? Name = null, int Kind = 0, int MaxUsableTimes = 0);

internal record ProxyType<T> where T : notnull
{
    private readonly Dictionary<T, ProxyMethod> _methods;

    public ProxyType() => _methods = new();

    public ProxyType(IEqualityComparer<T> comparer) => _methods = new(comparer);

    public string? ActivitySourceName { get; init; }

    public IReadOnlyDictionary<T, ProxyMethod> Methods => _methods;

    public void AddMethod(T key, ProxyMethod method)
    {
        if (method.Settings != ActivitySettings.NonActivity) _methods[key] = method;
    }
}
