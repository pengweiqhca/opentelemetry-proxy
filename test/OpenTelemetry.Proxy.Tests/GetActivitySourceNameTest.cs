using OpenTelemetry.Proxy.StandardFiles;

namespace OpenTelemetry.Proxy.Tests;

public class GetActivitySourceNameTest
{
    [Fact]
    public void Missing_ActivitySourceAttribute() =>
        Assert.Equal(typeof(ActivityTestClass3).FullName, ActivitySourceAttribute.GetActivitySourceName(typeof(ActivityTestClass3)));

    [Fact]
    public void Empty_ActivitySourceAttribute_Name() =>
        Assert.Equal(typeof(ActivityTestClass2<>).FullName, ActivitySourceAttribute.GetActivitySourceName(typeof(ActivityTestClass2<>)));

    [Fact]
    public void ActivitySourceAttribute_Name() =>
        Assert.Equal("ActivityTestClass", ActivitySourceAttribute.GetActivitySourceName(typeof(ActivityTestClass1)));
}
