using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using OpenTelemetry.Proxy.TestClass;
using Xunit.Sdk;

namespace OpenTelemetry.Proxy.Tests;

public class FunctionTest
{
    [Fact]
    public void SuppressInstrumentationScope()
    {
        Assert.True(ProxyTestClass.SuppressInstrumentationScope());
        Assert.True(ProxyTestClass.SuppressInstrumentationScope2());
    }

    [Fact]
    public async Task SuppressInstrumentationScopeAsync()
    {
        Assert.True(await ProxyTestClass.SuppressInstrumentationScopeAsync().ConfigureAwait(false));
        Assert.True(await ProxyTestClass.SuppressInstrumentationScope2Async().ConfigureAwait(false));
        Assert.True(await ProxyTestClass.SuppressInstrumentationScope3Async().ConfigureAwait(false));
        Assert.True(await ProxyTestClass.SuppressInstrumentationScopeAwaitable());
    }

    [Fact]
    public void GetActivityName()
    {
        var context = ProxyTestClass.GetActivityName(200, "123");

        Assert.NotNull(context);
        Assert.Equal($"{nameof(ProxyTestClass)}.{nameof(ProxyTestClass.GetActivityName)}", context.Name);
        Assert.NotNull(context.Tags);
        Assert.Contains(context.Tags, kv => kv is { Key: "delay", Value: 200 });
        Assert.True(context.AdjustStartTime);
    }

    [Fact]
    public async Task GetActivityNameAsync()
    {
        var context = await ProxyTestClass.GetActivityNameAsync(200, "123").ConfigureAwait(false);

        Assert.NotNull(context);
        Assert.Equal($"{nameof(ProxyTestClass)}.{nameof(ProxyTestClass.GetActivityNameAsync)}", context.Name);
        Assert.NotNull(context.Tags);
        Assert.Contains(context.Tags, kv => kv is { Key: "delay", Value: 200 });
        Assert.True(context.AdjustStartTime);
    }

    [Fact]
    public Task GetActivityNameAwaitable() =>
        Assert.ThrowsAsync<IsAssignableFromException>(async () =>
            await ProxyTestClass.GetActivityNameAwaitable(1));

    [Fact]
    public void GetCurrentActivity()
    {
        using var activityListener = CreateActivityListener(out var list);

        var activity = ProxyTestClass.GetCurrentActivity(100);

        Assert.Equal(Assert.Single(list), activity);
        Assert.Equal(100, activity!.GetTagItem("delay"));
    }

    [Fact]
    public async Task GetCurrentActivityAsync()
    {
        using var activityListener = CreateActivityListener(out var list);

        var activity = await ProxyTestClass.GetCurrentActivityAsync(100).ConfigureAwait(false);
        Assert.Equal(Assert.Single(list), activity);
        Assert.Equal(100, activity!.GetTagItem("delay"));

        list.Clear();

        activity = await ProxyTestClass.GetCurrentActivity2Async(100).ConfigureAwait(false);
        Assert.Equal(Assert.Single(list), activity);
        Assert.Equal(100, activity!.GetTagItem("delay"));

        list.Clear();

        activity = await ProxyTestClass.AwaitGetCurrentActivityAsync(100).ConfigureAwait(false);
        Assert.Equal(Assert.Single(list), activity);
        Assert.Equal(100, activity!.GetTagItem("delay"));
    }

    [Fact]
    public async Task GetCurrentActivityFSharpAsync()
    {
        using var activityListener = CreateActivityListener(out var list);

        var fsharpAsync = ProxyTestClass.GetCurrentActivityFSharpAsync(100);
        var activity = await FSharpAsync.StartAsTask(fsharpAsync,
            FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.None).ConfigureAwait(false);

        Assert.Equal(Assert.Single(list), activity);
        Assert.Equal(new DateTime(2024, 1, 1), activity!.GetTagItem("Now"));
    }

    [Fact]
    public async Task GetCurrentActivityAwaitable()
    {
        using var activityListener = CreateActivityListener(out var list);

        var activity = await ProxyTestClass.GetCurrentActivityAwaitable(100);

        Assert.Equal(Assert.Single(list), activity);
        Assert.Equal(100, activity!.GetTagItem("delay"));
        Assert.Equal(new DateTime(2024, 1, 1), activity.GetTagItem("Now"));
    }

    [Fact]
    public async Task Exception()
    {
        using var activityListener = CreateActivityListener(out var list);

        await Assert.ThrowsAsync<Exception>(() => ProxyTestClass.Exception()).ConfigureAwait(false);

        var activity = Assert.Single(list);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public void OutTest()
    {
        using var activityListener = CreateActivityListener(out var list);

        var a = DateTime.Now.Second + 10;
        var c = new Random().Next(10, 100);
        var refC = c;

        var result = ProxyTestClass.OutMethod(a, out var b, ref refC, 1, 2, 3);

        Assert.Equal(a * a, b);
        Assert.Equal(a * c, refC);

        var activity = Assert.Single(list);
        Assert.Equal(3, activity.GetTagItem("f"));
        Assert.Equal(result, activity.GetTagItem("def"));
    }

    [Fact]
    public async Task ReturnValueTest()
    {
        using var activityListener = CreateActivityListener(out var list);

        var value = DateTime.Now.Millisecond;
        var result = await ProxyTestClass.ReturnValue(value).ConfigureAwait(false);

        Assert.Equal(value + 1, result);

        var activity = Assert.Single(list);
        Assert.Equal(result, activity.GetTagItem("$returnvalue"));
    }

    [Fact]
    public async Task ReturnValueAsyncTest()
    {
        using var activityListener = CreateActivityListener(out var list);

        var value = DateTime.Now.Millisecond;
        var result = await ProxyTestClass.ReturnValueAsync(value).ConfigureAwait(false);

        Assert.Equal(value + 1, result);

        var activity = Assert.Single(list);
        Assert.Equal(result, activity.GetTagItem("$returnvalue"));
    }

    [Fact]
    public void UsingTest() => Assert.True(ProxyTestClass.Using(out _));

    private static ActivityListener CreateActivityListener(out List<Activity> list)
    {
        var activities = new List<Activity>();
        list = activities;

        var listener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == typeof(ProxyTestClass).FullName &&
                activitySource.Version == typeof(ProxyTestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = activities.Add
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }
}
