namespace OpenTelemetry.StaticProxy;

/// <summary>
/// The instrumentation mode for a method.
/// </summary>
internal enum MethodMode
{
    /// <summary>Create an Activity span around the method call.</summary>
    Activity,

    /// <summary>Set the inner activity name via InnerActivityAccessor.</summary>
    ActivityName,

    /// <summary>Suppress downstream instrumentation during the method call.</summary>
    SuppressInstrumentation
}
