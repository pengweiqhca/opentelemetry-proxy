﻿#if DynamicProxy
namespace OpenTelemetry.DynamicProxy;
#else
namespace OpenTelemetry.StaticProxy;
#endif

internal interface IProxyMethod;

internal sealed class SuppressInstrumentationMethod : IProxyMethod
{
    private SuppressInstrumentationMethod() { }

    public static SuppressInstrumentationMethod Instance { get; } = new();
}

internal sealed class ActivityNameMethod(string activityName, bool adjustStartTime) : IProxyMethod
{
    public string ActivityName { get; } = activityName;

    public bool AdjustStartTime { get; } = adjustStartTime;
}

internal sealed class ActivityMethod(
    string activityName,
    ActivityKind kind,
    bool suppressInstrumentation)
    : IProxyMethod
{
    public string ActivityName { get; } = activityName;

    public ActivityKind Kind { get; } = kind;

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
