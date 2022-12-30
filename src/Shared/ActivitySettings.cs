namespace OpenTelemetry.Proxy;

internal enum ActivitySettings
{
    NonActivity = 0,
    NonActivityAndSuppressInstrumentation = 1,
    Activity = 2,
    ActivityNameOnly = 3
}
