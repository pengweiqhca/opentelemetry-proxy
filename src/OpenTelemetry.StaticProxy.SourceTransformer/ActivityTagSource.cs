namespace OpenTelemetry.StaticProxy;

internal record struct ActivityTagSource(string Name, ActivityTagFrom From);

internal enum ActivityTagFrom
{
    Argument,
    InstanceFieldOrProperty,
    StaticFieldOrProperty,
}
