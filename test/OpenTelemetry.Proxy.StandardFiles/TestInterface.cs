namespace OpenTelemetry.Proxy.StandardFiles;

#if NET6_0_OR_GREATER
public interface ITestInterface
{
    [Activity]
    static void StaticMethod() { }

    [Activity]
    void InstanceMethod() { }
}
#endif
