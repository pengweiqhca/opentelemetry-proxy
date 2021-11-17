namespace OpenTelemetry.DynamicProxy.Tests;

public class CreateActivityInvokerTest
{
    [Theory]
    [MemberData(nameof(CreateActivityInvokerData))]
    public void CreateActivityInvoker(Type returnType, Type activityInvokerType) =>
        Assert.Equal(activityInvokerType, ActivityInvokerHelper.GetActivityInvokerType(returnType));

    public static IEnumerable<object[]> CreateActivityInvokerData()
    {
        yield return new object[] { typeof(void), typeof(ActivityInvoker) };
        yield return new object[] { typeof(string), typeof(ActivityInvoker) };
        yield return new object[] { typeof(List<string>), typeof(ActivityInvoker) };
        yield return new object[] { typeof(Task), typeof(TaskActivityInvoker) };
        yield return new object[] { typeof(Task<string>), typeof(TaskActivityInvoker<string>) };
        yield return new object[] { typeof(ValueTask), typeof(ValueTaskActivityInvoker) };
        yield return new object[] { typeof(ValueTask<string>), typeof(ValueTaskActivityInvoker<string>) };
        yield return new object[] { typeof(IAsyncEnumerable<string>), typeof(AsyncStreamActivityInvoker<string>) };
    }
}

