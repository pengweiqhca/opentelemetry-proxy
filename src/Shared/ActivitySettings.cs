namespace OpenTelemetry.Proxy;

internal enum ActivitySettings
{
    None = 0,
    SuppressInstrumentation = 1,
    Activity = 2,
    ActivityName = 3,
    ActivityAndSuppressInstrumentation = 4,
}
