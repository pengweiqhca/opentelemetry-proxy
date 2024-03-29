﻿namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Method)]
public class NonActivityAttribute : Attribute
{
    public NonActivityAttribute() { }

    public NonActivityAttribute(bool suppressInstrumentation) => SuppressInstrumentation = suppressInstrumentation;

    public bool SuppressInstrumentation { get; }
}
