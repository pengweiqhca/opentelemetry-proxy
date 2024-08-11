using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

public class ActivityTagTestClass2(DateTime now, DateTime now2)
{
    [Activity("Test.InstanceMethod")]
    [ActivityTags(nameof(_now), nameof(Now), nameof(e))]
    [return: ActivityTag("ghi")]
    public virtual int InstanceMethod([ActivityTag("a2")] int a, int _, [ActivityTag] in int b,
        [ActivityTag] out DateTimeOffset c, [ActivityTag] ref int d, int e)
    {
        c = now2;

        return _;
    }

    [Activity("Test.StaticMethod")]
    [ActivityTags(nameof(_now), nameof(Now), nameof(e), "$returnvalue")]
    public static int StaticMethod([ActivityTag("a2")] int a, int _, [ActivityTag] in int b,
        [ActivityTag] out DateTimeOffset c, [ActivityTag] ref int d, int e)
    {
        c = Now2;

        return _;
    }

    private readonly DateTime _now = now;

    public DateTimeOffset Now { get; } = DateTimeOffset.Now.AddYears(1);

    public static DateTimeOffset Now2 { get; } = DateTimeOffset.Now;
}
