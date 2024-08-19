namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal record struct ActivityTagSource(string SourceName, ActivityTagFrom From)
{
    public static ActivityTagSource ReturnValue { get; } = new("@return", ActivityTagFrom.ArgumentOrLocalVariable);
}

internal enum ActivityTagFrom
{
    ArgumentOrLocalVariable,
    InstanceFieldOrProperty,
    StaticFieldOrProperty,
}
