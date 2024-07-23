using AssemblyToProcess;
using Fody;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using OpenTelemetry.StaticProxy.Fody;
using System.Reflection;
using ActivityName =
    System.Tuple<string?, System.Collections.Generic.IReadOnlyCollection<
        System.Collections.Generic.KeyValuePair<string, object?>>?, long>;

namespace OpenTelemetry.StaticProxy.Tests;

public class ModuleWeaverTest
{
    [Fact]
    public void NoProxyHasGenerated()
    {
        var method = AssemblyEmit(typeof(NormalClass))?.GetMethod(nameof(NormalClass.NormalMethod));

        Assert.NotNull(method?.DeclaringType);

        Assert.Null(method?.DeclaringType?.GetCustomAttribute<ProxyHasGeneratedAttribute>());
    }

    [Fact]
    public async Task SuppressInstrumentationScope()
    {
        await SuppressInstrumentationScopeTest(
            nameof(ModuleWeaverTestClass.SuppressInstrumentationScope), static instance => new((bool)instance)).ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
            nameof(ModuleWeaverTestClass.SuppressInstrumentationScope2), static instance => new((bool)instance)).ConfigureAwait(false);
    }

    [Fact]
    public async Task SuppressInstrumentationScopeAsync()
    {
        await SuppressInstrumentationScopeTest(
            nameof(ModuleWeaverTestClass.SuppressInstrumentationScopeAsync),
            static instance => new((Task<bool>)instance)).ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
            nameof(ModuleWeaverTestClass.SuppressInstrumentationScope2Async),
            static instance => (ValueTask<bool>)instance).ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
            nameof(ModuleWeaverTestClass.SuppressInstrumentationScope3Async),
            static instance => new((Task<bool>)instance)).ConfigureAwait(false);

        await SuppressInstrumentationScopeTest(
                nameof(ModuleWeaverTestClass.SuppressInstrumentationScopeAwaitable), Awaitable2ValueTask<bool>)
            .ConfigureAwait(false);
    }

    private static async Task SuppressInstrumentationScopeTest(string methodName, Func<object, ValueTask<bool>> func)
    {
        var method = AssemblyEmit()?.GetMethod(methodName);

        Assert.NotNull(method);

        Assert.True(await func(method.Invoke(null, [])!).ConfigureAwait(false));
    }

    [Fact]
    public Task GetActivityName() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityName),
        static instance => new((ActivityName)instance), new() { { "delay", 200 } });

    [Fact]
    public Task GetActivityNameAsync() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityNameAsync),
        static instance => (ValueTask<ActivityName>)instance, new() { { "delay", 200 } });

    [Fact]
    public Task GetActivityNameAwaitable() => ActivityNameTest(nameof(ModuleWeaverTestClass.GetActivityNameAwaitable),
        Awaitable2ValueTask<ActivityName>, new() { { "Now", new DateTime(2024, 1, 1) } });

    private static async Task ActivityNameTest(string methodName,
        Func<object, ValueTask<ActivityName>> func, Dictionary<string, object?> tags)
    {
        var method = AssemblyEmit()?.GetMethod(methodName);

        Assert.NotNull(method);

        var (activityName, tags2, availableTimes) = await func(method.Invoke(null, [200])!).ConfigureAwait(false);

        Assert.Equal($"{nameof(ModuleWeaverTestClass)}.{methodName}", activityName);
        Assert.Equal(tags, tags2);
        Assert.Equal(1, availableTimes);
    }

    [Fact]
    public Task GetCurrentActivity() => ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivity),
        static instance => new((Activity?)instance), new() { { "delay", 100 } });

    [Fact]
    public async Task GetCurrentActivityAsync()
    {
        await ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivityAsync),
            static instance => new((Task<Activity?>)instance), new() { { "delay", 100 } }).ConfigureAwait(false);

        await ActivityTest(nameof(ModuleWeaverTestClass.GetCurrentActivity2Async),
            static instance => new((Task<Activity?>)instance), new() { { "delay", 100 } }).ConfigureAwait(false);

        await ActivityTest(nameof(ModuleWeaverTestClass.AwaitGetCurrentActivityAsync),
            static instance => new((Task<Activity?>)instance), new() { { "delay", 100 } }).ConfigureAwait(false);
    }

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

        await Assert.ThrowsAsync<Exception>(() => ((Task)method.Invoke(null, [])!)).ConfigureAwait(false);

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

        var result = await ((Task<int>)method.Invoke(null, [ value ])!).ConfigureAwait(false);

        Assert.Equal(value + 1, result);

        var activity = Assert.Single(list);

        Assert.Equal(result, activity.GetTagItem(ActivityTagAttribute.ReturnValueTagName));
    }

    [Fact]
    public void UsingTest()
    {
        var method = AssemblyEmit()?.GetMethod(nameof(ModuleWeaverTestClass.Using));

        Assert.NotNull(method);

        var result = ((ModuleWeaverTestClass.TestDelegate)method.CreateDelegate(typeof(ModuleWeaverTestClass.TestDelegate)))(out _);

        Assert.True(result);
    }

    [Fact]
    public void GenericMethodTest()
    {
        var method = AssemblyEmit()?.GetMethod(nameof(ModuleWeaverTestClass.GenericMethod));

        Assert.NotNull(method);

        method.MakeGenericMethod(typeof(int)).Invoke(null, []);
    }

    [Fact]
    public void VoidMethodTest()
    {
        var method = AssemblyEmit()?.GetMethod(nameof(ModuleWeaverTestClass.VoidMethod));

        Assert.NotNull(method);

        method.Invoke(null, []);
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

        var activity = await func(method.Invoke(null, [100])!).ConfigureAwait(false);

        Assert.Equal(Assert.Single(list), activity);

        foreach (var kv in tags)
            Assert.Equal(kv.Value, activity!.GetTagItem(kv.Key));
    }

    private static Type? AssemblyEmit(Type? type = null) => new ModuleWeaver().ExecuteTestRun(
            typeof(ModuleWeaverTestClass).Assembly.Location,
            assemblyName: "AssemblyToProcess", runPeVerify: false).Assembly
        .GetType((type ?? typeof(ModuleWeaverTestClass)).FullName!);

    private static async ValueTask<T> Awaitable2ValueTask<T>(object awaitable) => await (TestAwaitable<T>)awaitable;
}
