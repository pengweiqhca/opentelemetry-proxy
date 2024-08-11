using OpenTelemetry.Proxy;
using System;

namespace OpenTelemetry.Proxy.StandardFiles;

#pragma warning disable CS0414
public class ActivityTagTestClass4
{
    [Activity]
    [ActivityTags(nameof(_abc), nameof(Abc), nameof(abc))]
    public void InstanceMethod(int abc, [ActivityTag("def")] int age) { }

    [Activity]
    [ActivityTags(nameof(_abc), nameof(Abc), nameof(abc))]
    public static void StaticMethod(int abc, [ActivityTag("def")] int age) { }

    private static readonly int Abc = 1;

    private readonly int _abc = 1;

    public static DateTime Now => DateTime.Now;
}
