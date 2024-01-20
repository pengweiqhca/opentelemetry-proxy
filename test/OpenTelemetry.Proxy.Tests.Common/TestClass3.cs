namespace OpenTelemetry.Proxy.Tests.Common;

public class TestClass3(DateTime now, DateTime now2)
{
    private readonly DateTime _now = now;

    [ActivityTag("abc")]
    private readonly DateTime _now2 = now2;

    public DateTimeOffset Now { get; } = DateTimeOffset.Now.AddYears(1);

    [ActivityTag("def")]
    public static DateTimeOffset Now2 { get; } = DateTimeOffset.Now;

    [Activity("Test.InstanceMethod", Tags = [nameof(_now), nameof(Now), "e"])]
    [return: ActivityTag("ghi")]
    public virtual int InstanceMethod([ActivityTag("a2")] int a, int _, [ActivityTag] in int b,
        [ActivityTag] out DateTimeOffset c, [ActivityTag] ref int d, int e)
    {
        c = _now2;

        return _;
    }

    [Activity("Test.InstanceMethod", Tags = [nameof(_now), nameof(Now), "e"])]
    [return: ActivityTag("ghi")]
    public static int StaticMethod([ActivityTag("a2")] int a, int _, [ActivityTag] in int b,
        [ActivityTag] out DateTimeOffset c, [ActivityTag] ref int d, int e)
    {
        c = Now2;

        return _;
    }
}
