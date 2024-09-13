using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using OpenTelemetry.StaticProxy.TestClass;
using System.Reflection;
using Xunit.Sdk;

namespace OpenTelemetry.StaticProxy.Tests;

public class FunctionTest
{
    [Fact]
    public void ProxyHasGeneratedAttributeTest()
    {
        Assert.Null(typeof(EmptyClass2).GetCustomAttribute<ProxyHasGeneratedAttribute>());

        Assert.NotNull(typeof(TestClass.TestClass).GetCustomAttribute<ProxyHasGeneratedAttribute>());

        Assert.NotNull(typeof(TestClass.TestClass).Assembly.GetCustomAttribute<ProxyHasGeneratedAttribute>());
    }

    [Fact]
    public async Task SuppressInstrumentationScope()
    {
        await SuppressInstrumentationScopeTest(
                nameof(TestClass.TestClass.SuppressInstrumentationScope), static instance => new((bool)instance))
            .ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
                nameof(TestClass.TestClass.SuppressInstrumentationScope2), static instance => new((bool)instance))
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task SuppressInstrumentationScopeAsync()
    {
        await SuppressInstrumentationScopeTest(
            nameof(TestClass.TestClass.SuppressInstrumentationScopeAsync),
            static instance => new((Task<bool>)instance)).ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
            nameof(TestClass.TestClass.SuppressInstrumentationScope2Async),
            static instance => (ValueTask<bool>)instance).ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
            nameof(TestClass.TestClass.SuppressInstrumentationScope3Async),
            static instance => new((Task<bool>)instance)).ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
                nameof(TestClass.TestClass.SuppressInstrumentationScopeAwaitable), Awaitable2ValueTask<bool>)
            .ConfigureAwait(false);
    }

    private static async Task SuppressInstrumentationScopeTest(string methodName, Func<object, ValueTask<bool>> func)
    {
        var method = typeof(TestClass.TestClass).GetMethod(methodName);

        Assert.NotNull(method);

        Assert.True(await func(method.Invoke(null, [])!).ConfigureAwait(false));
    }

    [Fact]
    public Task GetActivityName() => ActivityNameTest(nameof(TestClass.TestClass.GetActivityName),
        static instance => new((InnerActivityContext?)instance));

    [Fact]
    public Task GetActivityNameAsync() => ActivityNameTest(nameof(TestClass.TestClass.GetActivityNameAsync),
        static instance => (ValueTask<InnerActivityContext?>)instance);

    [Fact]
    public Task GetActivityNameAwaitable() =>
        Assert.ThrowsAsync<IsAssignableFromException>(async () =>
            await TestClass.TestClass.GetActivityNameAwaitable(1));

    private static async Task ActivityNameTest(string methodName,
        Func<object, ValueTask<InnerActivityContext?>> func)
    {
        var method = typeof(TestClass.TestClass).GetMethod(methodName);

        Assert.NotNull(method);

        var context = await func(method.Invoke(null, [200, "123"])!).ConfigureAwait(false);

        Assert.NotNull(context);

        Assert.Equal($"{nameof(TestClass.TestClass)}.{methodName}", context.Name);
        Assert.NotNull(context.Tags);
        Assert.Contains(context.Tags, kv => kv is { Key: "delay", Value: 200 });
        Assert.True(context.AdjustStartTime);
    }

    [Fact]
    public Task GetCurrentActivity() => ActivityTest(nameof(TestClass.TestClass.GetCurrentActivity),
        static instance => new((Activity?)instance), new() { { "delay", 100 } });

    [Fact]
    public async Task GetCurrentActivityAsync()
    {
        await ActivityTest(nameof(TestClass.TestClass.GetCurrentActivityAsync),
            static instance => new((Task<Activity?>)instance), new() { { "delay", 100 } }).ConfigureAwait(false);

        await ActivityTest(nameof(TestClass.TestClass.GetCurrentActivity2Async),
            static instance => new((Task<Activity?>)instance), new() { { "delay", 100 } }).ConfigureAwait(false);

        await ActivityTest(nameof(TestClass.TestClass.AwaitGetCurrentActivityAsync),
            static instance => new((Task<Activity?>)instance), new() { { "delay", 100 } }).ConfigureAwait(false);
    }

    [Fact]
    public Task GetCurrentActivityFSharpAsync() => ActivityTest(
        nameof(TestClass.TestClass.GetCurrentActivityFSharpAsync),
        static instance => new(FSharpAsync.StartAsTask((FSharpAsync<Activity?>)instance,
            FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.None)),
        new() { { "Now", new DateTime(2024, 1, 1) } });

    [Fact]
    public Task GetCurrentActivityAwaitable() => ActivityTest(nameof(TestClass.TestClass.GetCurrentActivityAwaitable),
        Awaitable2ValueTask<Activity?>, new() { { "delay", 100 }, { "Now", new DateTime(2024, 1, 1) } });

    [Fact]
    public async Task Exception()
    {
        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == typeof(TestClass.TestClass).FullName &&
                activitySource.Version == typeof(TestClass.TestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var ex = await Assert.ThrowsAsync<Exception>(TestClass.TestClass.Exception).ConfigureAwait(false);

        var stackFrame = new EnhancedStackTrace(ex).GetFrame(0);

        Assert.Equal(142, stackFrame.GetFileLineNumber());
        Assert.Equal(Path.GetFullPath(
            "../../../../OpenTelemetry.StaticProxy.TestClass/TestClass.cs"), stackFrame.GetFileName());

        Assert.Equal(9, stackFrame.GetFileColumnNumber());

        var activity = Assert.Single(list);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public void OutTest()
    {
        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == typeof(TestClass.TestClass).FullName &&
                activitySource.Version == typeof(TestClass.TestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var a = DateTime.Now.Second + 10;
        var c = new Random().Next(10, 100);
        var refC = c;

        var result = TestClass.TestClass.OutMethod(a, out var b, ref refC, 1, 2, 3);

        Assert.Equal(a * a, b);
        Assert.Equal(a * c, refC);

        var activity = Assert.Single(list);

        Assert.Equal(3, activity.GetTagItem("f"));
        Assert.Equal(result, activity.GetTagItem("def"));
    }

    [Fact]
    public async Task ReturnValueTest()
    {
        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == typeof(TestClass.TestClass).FullName &&
                activitySource.Version == typeof(TestClass.TestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var value = DateTime.Now.Millisecond;

        var result = await TestClass.TestClass.ReturnValue(value).ConfigureAwait(false);

        Assert.Equal(value + 1, result);

        var activity = Assert.Single(list);

        _ = Assert.IsAssignableFrom<Task>(activity.GetTagItem("$returnvalue"));
    }

    [Fact]
    public async Task ReturnValueAsyncTest()
    {
        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == typeof(TestClass.TestClass).FullName &&
                activitySource.Version == typeof(TestClass.TestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var value = DateTime.Now.Millisecond;

        var result = await TestClass.TestClass.ReturnValueAsync(value).ConfigureAwait(false);

        Assert.Equal(value + 1, result);

        var activity = Assert.Single(list);

        Assert.Equal(result, activity.GetTagItem("$returnvalue"));
    }

    [Fact]
    public void UsingTest() => Assert.True(TestClass.TestClass.Using(out _));

    private static async Task ActivityTest(string methodName, Func<object, ValueTask<Activity?>> func,
        Dictionary<string, object> tags)
    {
        var method = typeof(TestClass.TestClass).GetMethod(methodName);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == typeof(TestClass.TestClass).FullName &&
                activitySource.Version == typeof(TestClass.TestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = a => list.Add(a)
        };

        ActivitySource.AddActivityListener(activityListener);

        Assert.NotNull(method);

        var activity = await func(method.Invoke(null, [100])!).ConfigureAwait(false);

        Assert.Equal(Assert.Single(list), activity);

        foreach (var kv in tags)
            Assert.Equal(kv.Value, activity!.GetTagItem(kv.Key));
    }

    private static async ValueTask<T> Awaitable2ValueTask<T>(object awaitable) => await (TestAwaitable<T>)awaitable;
}
