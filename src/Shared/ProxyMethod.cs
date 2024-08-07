﻿namespace OpenTelemetry.Proxy;

internal record struct ProxyMethod(ActivitySettings Settings, string? ActivityName = null, int Kind = 0, int MaxUsableTimes = 0);

internal record ProxyType<T> where T : notnull
{
    private readonly Dictionary<T, ProxyMethod> _methods;

    public ProxyType() => _methods = [];

    public string? ActivitySourceName { get; init; }

    public IReadOnlyDictionary<T, ProxyMethod> Methods => _methods;

    public void AddMethod(T key, ProxyMethod method)
    {
        if (method.Settings != ActivitySettings.None) _methods[key] = method;
    }
}
