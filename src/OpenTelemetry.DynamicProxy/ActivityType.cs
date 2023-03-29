namespace OpenTelemetry.DynamicProxy;

public enum ActivityType
{
    Explicit = 0,
 #pragma warning disable CA1069
    Default = 0,
 #pragma warning restore CA1069
    ImplicitActivity = 1,
    ImplicitActivityName = 2,
}
