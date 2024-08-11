using OpenTelemetry.Proxy;
using System;

namespace OpenTelemetry.Proxy.StandardFiles;

#pragma warning disable CS0414
[ActivityTags(nameof(_abc), nameof(Abc))]
public class ActivityTagTestClass3
{
    private static readonly int Abc = 1;

    private readonly int _abc = 1;

    public static DateTime Now => DateTime.Now;

    [Activity]
    [ActivityTags(nameof(abc))]
    public void InstanceMethod(int abc, [ActivityTag("")] int age) { }

    [Activity]
    [ActivityTags(nameof(abc))]
    public static void StaticMethod(int abc, [ActivityTag(" ")] int age) { }
}
