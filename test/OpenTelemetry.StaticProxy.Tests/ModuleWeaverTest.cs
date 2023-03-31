using AssemblyToProcess;
using Fody;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using OpenTelemetry.StaticProxy.Fody;

namespace OpenTelemetry.StaticProxy.Tests;

public class ModuleWeaverTest
{
    [Fact]
    public Task SuppressInstrumentationScope() => SuppressInstrumentationScopeTest(
        nameof(ModuleWeaverTestClass.SuppressInstrumentationScope), static instance => new((bool)instance));

    [Fact]
    public Task SuppressInstrumentationScopeAsync() => SuppressInstrumentationScopeTest(
        nameof(ModuleWeaverTestClass.SuppressInstrumentationScopeAsync), static instance => new((Task<bool>)instance));

    [Fact]
    public Task SuppressInstrumentationScope2Async() => SuppressInstrumentationScopeTest(
        nameof(ModuleWeaverTestClass.SuppressInstrumentationScope2Async), static instance => (ValueTask<bool>)instance);

    [Fact]
    public Task SuppressInstrumentationScopeAwaitable() => SuppressInstrumentationScopeTest(
        nameof(ModuleWeaverTestClass.SuppressInstrumentationScopeAwaitable), Awaitable2ValueTask<bool>);

    private static async Task SuppressInstrumentationScopeTest(string methodName, Func<object, ValueTask<bool>> func)
    {
        var method = AssemblyEmit()?.GetMethod(methodName);

        Assert.NotNull(method);

        Assert.True(await func(method.Invoke(null, Array.Empty<object?>())!).ConfigureAwait(false));
    }

    [Fact]
    public Task GetActivityName() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityName),
        static instance => new((Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int>)instance),
        new() { { "delay", 200 } });

    [Fact]
    public Task GetActivityNameAsync() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityNameAsync),
        static instance =>
            (ValueTask<Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int>>)instance,
        new() { { "delay", 200 } });

    [Fact]
    public Task GetActivityNameAwaitable() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityNameAwaitable),
        Awaitable2ValueTask<Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int>>,
        new() { { "Now", new DateTime(2024, 1, 1) } });

    private static async Task ActivityNameTest(string methodName,
        Func<object, ValueTask<Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int>>> func,
        Dictionary<string, object?> tags)
    {
        var method = AssemblyEmit()?.GetMethod(methodName);

        Assert.NotNull(method);

        var (activityName, tags2, availableTimes) =
            await func(method.Invoke(null, new object[] { 200 })!).ConfigureAwait(false);

        Assert.Equal($"{typeof(ModuleWeaverTestClass).FullName}.{methodName}", activityName);
        Assert.Equal(tags, tags2);
        Assert.Equal(1, availableTimes);
    }

    [Fact]
    public Task GetCurrentActivity() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivity),
        static instance => new((Activity?)instance), new() { { "delay", 100 } });

    [Fact]
    public Task GetCurrentActivityAsync() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivityAsync),
        static instance => new((Task<Activity?>)instance), new() { { "delay", 100 } });

    [Fact]
    public Task GetCurrentActivityFSharpAsync() => ActivityTest(
        nameof(ModuleWeaverTestClass.GetCurrentActivityFSharpAsync),
        static instance => new(FSharpAsync.StartAsTask((FSharpAsync<Activity?>)instance,
            FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.None)),
        new() { { "Now", new DateTime(2024, 1, 1) } });

    [Fact]
    public Task GetCurrentActivityAwaitable() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivityAwaitable),
        Awaitable2ValueTask<Activity?>, new() { { "delay", 100 }, { "Now", new DateTime(2024, 1, 1) } });

    [Fact]
    public async Task Exception()
    {
        var method = AssemblyEmit()?.GetMethod(nameof(ModuleWeaverTestClass.Exception));

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == nameof(ModuleWeaverTestClass) &&
                activitySource.Version == typeof(ModuleWeaverTestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        Assert.NotNull(method);

        try
        {
            await ((Task)method.Invoke(null, Array.Empty<object?>())!).ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        var activity = Assert.Single(list);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public void OutTest()
    {
        var method = AssemblyEmit()?.GetMethod(nameof(ModuleWeaverTestClass.OutMethod));

        Assert.NotNull(method);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == nameof(ModuleWeaverTestClass) &&
                activitySource.Version == typeof(ModuleWeaverTestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var testDelegate = (TestDelegate)method.CreateDelegate(typeof(TestDelegate));

        var a = DateTime.Now.Second + 10;
        var c = new Random().Next(10, 100);
        var refC = c;

        var result = testDelegate(a, out var b, ref refC, 1, 2, 3);

        Assert.Equal(a * a, b);
        Assert.Equal(a * c, refC);

        var activity = Assert.Single(list);

        Assert.Equal(3, activity.GetTagItem("f"));
        Assert.Equal(result, activity.GetTagItem("def"));
    }

    private delegate int TestDelegate(in int a, out int b, ref int c, int d, int e, int f);

    [Fact]
    public async Task ReturnValueTest()
    {
        var method = AssemblyEmit()?.GetMethod(nameof(ModuleWeaverTestClass.ReturnValue));

        Assert.NotNull(method);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == nameof(ModuleWeaverTestClass) &&
                activitySource.Version == typeof(ModuleWeaverTestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var value = DateTime.Now.Millisecond;

        var result = await ((Task<int>)method.Invoke(null, new object[] { value })!).ConfigureAwait(false);

        Assert.Equal(value + 1, result);

        var activity = Assert.Single(list);

        Assert.Equal(result, activity.GetTagItem(ActivityTagAttribute.ReturnValueTagName));
    }

    private static async Task ActivityTest(string methodName, Func<object, ValueTask<Activity?>> func,
        Dictionary<string, object> tags)
    {
        var method = AssemblyEmit()?.GetMethod(methodName);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static activitySource => activitySource.Name == nameof(ModuleWeaverTestClass) &&
                activitySource.Version == typeof(ModuleWeaverTestClass).Assembly.GetName().Version?.ToString(),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => list.Add(a)
        };

        ActivitySource.AddActivityListener(activityListener);

        Assert.NotNull(method);

        var activity = await func(method.Invoke(null, new object?[] { 100 })!).ConfigureAwait(false);

        Assert.Equal(Assert.Single(list), activity);

        foreach (var kv in tags)
            Assert.Equal(kv.Value, activity!.GetTagItem(kv.Key));
    }

    private static Type? AssemblyEmit() => new ModuleWeaver().ExecuteTestRun(
            typeof(ModuleWeaverTestClass).Assembly.Location,
            assemblyName: "AssemblyToProcess", runPeVerify: false).Assembly
        .GetType(typeof(ModuleWeaverTestClass).FullName!);

    private static async ValueTask<T> Awaitable2ValueTask<T>(object awaitable) => await (TestAwaitable<T>)awaitable;
}
