using OpenTelemetry.Trace;
using System.Runtime.CompilerServices;

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

    private static readonly ActivitySource ActivitySource = new("Test");

    [Activity("Test.InstanceMethod", Tags = [nameof(_now), nameof(Now), "e"])]
    [return: ActivityTag("ghi")]
    public static int StaticMethod2([ActivityTag("a2")] int a, int _, [ActivityTag] in int b,
        [ActivityTag] out DateTimeOffset c, [ActivityTag] ref int d, int e)
    {
        c = default;

        int ret;
        var activity = ActivitySource.StartActivity("Test.InstanceMethod", ActivityKind.Client);

        activity?.SetTagEnumerable("def", Now2)
            .SetTagEnumerable("a2", a)
            .SetTagEnumerable("b", b)
            .SetTagEnumerable("d", d)
            .SetTagEnumerable("e", e);

        try
        {
            c = Now2;
            ret = _;
            activity?.SetTagEnumerable("ghi", ret);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);
            throw;
        }
        finally
        {
            activity?.SetTagEnumerable("c", c).SetTagEnumerable("d$out", d).Dispose();
        }

        return ret;
    }

    public delegate int TestDelegate(int a, int _, in int b, out DateTimeOffset c, ref int d, int e);
}
