namespace OpenTelemetry.StaticProxy;

/// <summary>
/// The source of a tag value.
/// </summary>
internal enum TagSource
{
    /// <summary>Tag value comes from a method parameter.</summary>
    Parameter,

    /// <summary>Tag value comes from the method return value.</summary>
    ReturnValue,

    /// <summary>Tag value comes from an instance field or property.</summary>
    InstanceFieldOrProperty,

    /// <summary>Tag value comes from a static field or property.</summary>
    StaticFieldOrProperty
}
