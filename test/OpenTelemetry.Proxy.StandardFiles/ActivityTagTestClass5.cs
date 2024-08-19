using OpenTelemetry.Proxy;
using System;

namespace OpenTelemetry.Proxy.StandardFiles;

public class ActivityTagTestClass5
{
    [Activity]
    [ActivityTags("$returnvalue.Second", "$returnvalue.Day", nameof(str), nameof(abc) + ".TotalSeconds")]
    [return: ActivityTag("ret1", Expression = "$.Hour"), ActivityTag("ret2", Expression = "$.Minute")]
    public DateTime InstanceMethod(TimeSpan abc, [ActivityTag("def", Expression = "$?.Length")] string str) => Now;

    public static DateTime Now => DateTime.Now;
}
