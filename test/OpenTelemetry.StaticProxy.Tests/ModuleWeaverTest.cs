using AssemblyToProcess;
using Fody;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
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
        static instance => new((Tuple<string?, int>)instance));

    [Fact]
    public Task GetActivityNameAsync() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityNameAsync),
        static instance => (ValueTask<Tuple<string?, int>>)instance);

    [Fact]
    public Task GetActivityNameAwaitable() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityNameAwaitable),
        Awaitable2ValueTask<Tuple<string?, int>>);

    private static async Task ActivityNameTest(string methodName, Func<object, ValueTask<Tuple<string?, int>>> func)
    {
        var method = AssemblyEmit()?.GetMethod(methodName);

        Assert.NotNull(method);

        var (activityName, availableTimes) =
            await func(method.Invoke(null, Array.Empty<object?>())!).ConfigureAwait(false);

        Assert.Equal($"{typeof(ModuleWeaverTestClass).FullName}.{methodName}", activityName);
        Assert.Equal(1, availableTimes);
    }

    [Fact]
    public Task GetCurrentActivity() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivity),
        static instance => new((Activity?)instance));

    [Fact]
    public Task GetCurrentActivityAsync() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivityAsync),
        static instance => new((Task<Activity?>)instance));

    [Fact]
    public Task GetCurrentActivityFSharpAsync() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivityFSharpAsync),
        static instance => new(FSharpAsync.StartAsTask((FSharpAsync<Activity?>)instance,
            FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.None)));

    [Fact]
    public Task GetCurrentActivityAwaitable() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivityAwaitable),
        Awaitable2ValueTask<Activity?>);

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

        testDelegate(a, out var b, ref refC);

        Assert.Equal(a * a, b);
        Assert.Equal(a * c, refC);

        Assert.Single(list);
    }

    private delegate void TestDelegate(in int a, out int b, ref int c);

    private static async Task ActivityTest(string methodName, Func<object, ValueTask<Activity?>> func)
    {
        var method = AssemblyEmit()?.GetMethod(methodName);

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

        var activity = await func(method.Invoke(null, Array.Empty<object?>())!).ConfigureAwait(false);

        Assert.Equal(Assert.Single(list), activity);
    }

    private static Type? AssemblyEmit() => new ModuleWeaver().ExecuteTestRun(
        typeof(ModuleWeaverTestClass).Assembly.Location,
        assemblyName: "AssemblyToProcess", runPeVerify: false).Assembly.GetType(typeof(ModuleWeaverTestClass).FullName!);

    private static async ValueTask<T> Awaitable2ValueTask<T>(object awaitable) => await (TestAwaitable<T>)awaitable;
}
