using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using System.Reflection;
using Xunit.Abstractions;

namespace OpenTelemetry.StaticProxy.Fody.Tests;

public class StaticProxyEmitterTest
{
    private readonly ITestOutputHelper _output;

    public StaticProxyEmitterTest(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(typeof(StaticProxyEmitterTest))]
    [InlineData(typeof(StaticProxyEmitterTestClass))]
    public void AddActivitySourceTest(Type type)
    {
        var emitter = CreateEmitter();

        var name = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.AddActivitySource(emitter.Context.TargetModule.GetType(type.FullName), name, version);

        var assembly = SaveAndLoad(emitter, _output);

        var field = assembly.GetType(type.FullName!)!.GetField("ActivitySource@",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.NotNull(field);

        var activitySource = Assert.IsType<ActivitySource>(field.GetValue(null));

        Assert.Equal(name, activitySource.Name);
        Assert.Equal(version, activitySource.Version);
    }

    [Fact]
    public void EmitSuppressInstrumentationScopeTest()
    {
        Assert.False(StaticProxyEmitterTestClass.SuppressInstrumentationScope());

        var emitter = CreateEmitter();

        emitter.EmitSuppressInstrumentationScope(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName))
            .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.SuppressInstrumentationScope)));

        var assembly = SaveAndLoad(emitter, _output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.SuppressInstrumentationScope));

        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method.Invoke(null, Array.Empty<object?>())));

        Assert.False(StaticProxyEmitterTestClass.SuppressInstrumentationScope());
    }

    [Fact]
    public void EmitActivityNameTest()
    {
        var (activityName, availableTimes) = StaticProxyEmitterTestClass.GetActivityName();

        Assert.Null(activityName);
        Assert.Equal(0, availableTimes);

        var emitter = CreateEmitter();

        activityName = Guid.NewGuid().ToString("N");
        availableTimes = DateTime.Now.Millisecond + 1;

        emitter.EmitActivityName(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                    .GetType(typeof(StaticProxyEmitterTestClass).FullName))
                .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.ActivityName)),
            activityName, availableTimes);

        var assembly = SaveAndLoad(emitter, _output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.ActivityName));

        Assert.NotNull(method);

        var tuple = Assert.IsType<Tuple<string?, int>>(method.Invoke(null, Array.Empty<object?>()));

        Assert.Equal(activityName, tuple.Item1);
        Assert.Equal(availableTimes, tuple.Item2);

        (activityName, availableTimes) = StaticProxyEmitterTestClass.GetActivityName();

        Assert.Null(activityName);
        Assert.Equal(0, availableTimes);
    }

    [Fact]
    public void EmitActivityTest()
    {
        Assert.Null(StaticProxyEmitterTestClass.GetCurrentActivity());

        var activity = new Activity("test").Start();

        var emitter = CreateEmitter();

        var name = Guid.NewGuid().ToString("N");
        var activityName = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.EmitActivity(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                    .GetType(typeof(StaticProxyEmitterTestClass).FullName))
                .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.GetCurrentActivity)),
            emitter.AddActivitySource(
                emitter.Context.TargetModule.GetType(typeof(StaticProxyEmitterTestClass).FullName), name,
                version),
            activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, _output);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == name && activitySource.Version == version,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.GetCurrentActivity));

        Assert.NotNull(method);

        var result = method.Invoke(null, Array.Empty<object?>());

        Assert.Equal(Assert.Single(list), Assert.IsType<Activity>(result));

        Assert.Equal(activity, StaticProxyEmitterTestClass.GetCurrentActivity());
    }

    [Fact]
    public void TryCatchTest()
    {
        var emitter = CreateEmitter();

        var name = Guid.NewGuid().ToString("N");
        var activityName = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.EmitActivity(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                    .GetType(typeof(StaticProxyEmitterTestClass).FullName))
                .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.TryCatch)),
            emitter.AddActivitySource(
                emitter.Context.TargetModule.GetType(typeof(StaticProxyEmitterTestClass).FullName), name,
                version),
            activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, _output);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == name && activitySource.Version == version,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.TryCatch));

        Assert.NotNull(method);

        var result = method.Invoke(null, Array.Empty<object?>());

        Assert.Equal(Assert.Single(list), Assert.IsType<Activity>(result));
    }

    [Fact]
    public void VoidTest()
    {
        var emitter = CreateEmitter();

        emitter.EmitSuppressInstrumentationScope(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName))
            .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.Void)));

        var assembly = SaveAndLoad(emitter, _output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.Void));

        Assert.NotNull(method);

        method.Invoke(null, Array.Empty<object?>());
    }

    [Fact]
    public void TryCatchVoidTest()
    {
        var emitter = CreateEmitter();

        emitter.EmitSuppressInstrumentationScope(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName))
            .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.TryCatchVoid)));

        var assembly = SaveAndLoad(emitter, _output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.TryCatchVoid));

        Assert.NotNull(method);

        method.Invoke(null, Array.Empty<object?>());
    }

    [Theory]
    [MemberData(nameof(AsyncMethods))]
    public async Task SuppressInstrumentationScopeAsync(string methodName, Func<object, Task> func)
    {
        var emitter = CreateEmitter();

        emitter.EmitSuppressInstrumentationScope(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName))
            .Single(m => m.Name == methodName));

        var assembly = SaveAndLoad(emitter, _output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(methodName);

        Assert.NotNull(method);

        await func(method.Invoke(null, Array.Empty<object?>())!).ConfigureAwait(false);
    }

    [Theory]
    [MemberData(nameof(AsyncMethods))]
    public async Task ActivityNameAsync(string methodName, Func<object, Task> func)
    {
        var emitter = CreateEmitter();

        var activityName = Guid.NewGuid().ToString("N");
        var availableTimes = DateTime.Now.Millisecond + 1;

        emitter.EmitActivityName(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                    .GetType(typeof(StaticProxyEmitterTestClass).FullName))
                .Single(m => m.Name == methodName),
            activityName, availableTimes);

        var assembly = SaveAndLoad(emitter, _output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(methodName);

        Assert.NotNull(method);

        await func(method.Invoke(null, Array.Empty<object?>())!).ConfigureAwait(false);
    }

    [Theory]
    [MemberData(nameof(AsyncMethods))]
    public async Task ActivityAsync(string methodName, Func<object, Task> func)
    {
        var activity = new Activity("test").Start();

        var emitter = CreateEmitter();

        var name = Guid.NewGuid().ToString("N");
        var activityName = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.EmitActivity(TypeDefinitionRocks.GetMethods(emitter.Context.TargetModule
                    .GetType(typeof(StaticProxyEmitterTestClass).FullName))
                .Single(m => m.Name == methodName),
            emitter.AddActivitySource(
                emitter.Context.TargetModule.GetType(typeof(StaticProxyEmitterTestClass).FullName), name,
                version),
            activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, _output);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == name && activitySource.Version == version,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(methodName);

        Assert.NotNull(method);

        await func(method.Invoke(null, Array.Empty<object?>())!).ConfigureAwait(false);
    }

    public static IEnumerable<object[]> AsyncMethods()
    {
        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.TaskMethod), (Func<object, Task>)(instance => (Task)instance)
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.GetCurrentActivity), (Func<object, Task>)(_ => Task.CompletedTask)
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.TaskTMethod), (Func<object, Task>)(instance => (Task)instance)
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.ValueTask),
            (Func<object, Task>)(instance => ((ValueTask)instance).AsTask())
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.ValueTTask),
            (Func<object, Task>)(instance => ((ValueTask<int>)instance).AsTask())
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.FSharp),
            (Func<object, Task>)(instance => FSharpAsync.StartAsTask((FSharpAsync<int>)instance,
                FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.None))
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.Awaitable),
            (Func<object, Task>)(async instance => await (TestAwaitable<int>)instance)
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.Awaitable2),
            (Func<object, Task>)(async instance => await (TestAwaitable)instance)
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.CriticalNotifyCompletion),
            (Func<object, Task>)(async instance => await (TestAwaitableWithICriticalNotifyCompletion)instance)
        };

        yield return new object[]
        {
            nameof(StaticProxyEmitterTestClass.NotifyCompletion),
            (Func<object, Task>)(async instance => await (TestAwaitableWithoutICriticalNotifyCompletion)instance)
        };
    }

    private static StaticProxyEmitter CreateEmitter() => new(new(
        AssemblyDefinition.ReadAssembly(typeof(StaticProxyEmitterTest).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(Activity).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(SuppressInstrumentationScope).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(BaseProvider).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(ActivityAttribute).Assembly.Location).MainModule));

    private static Assembly SaveAndLoad(StaticProxyEmitter emitter, ITestOutputHelper output)
    {
        var path = Path.Combine(AppContext.BaseDirectory, Path.GetTempFileName());

        emitter.Context.TargetModule.Assembly.Write(path);

        output.WriteLine(path);

        return Assembly.LoadFile(path);
    }
}

public static class StaticProxyEmitterTestClass
{
    private static readonly ActivitySource ActivitySource = new("Test", "1.0.0");

    public static bool SuppressInstrumentationScope() => Sdk.SuppressInstrumentation;

    public static Tuple<string?, int> GetActivityName()
    {
        var field = typeof(ActivityAttribute).Assembly.GetType("OpenTelemetry.Proxy.ActivityName")
            ?.GetField("Name", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var name = field.GetValue(null);

        Assert.NotNull(name);

        var nameHolder = name.GetType().GetProperty("Value")?.GetValue(name);

        return nameHolder == null
            ? new(null, 0)
            : new((string?)nameHolder.GetType().GetField("Name")?.GetValue(nameHolder),
                Assert.IsType<int>(nameHolder.GetType().GetField("AvailableTimes")?.GetValue(nameHolder)));
    }

    public static Tuple<string?, int> ActivityName() => GetActivityName();

    public static Activity? GetCurrentActivity() => Activity.Current;

    public static Activity? TryCatch()
    {
        Console.WriteLine(DateTime.Now);
        try
        {
            return Activity.Current;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
        finally
        {
            Console.WriteLine(DateTime.Now);
        }
    }

    public static void Void() => Console.WriteLine("abc");

    public static void TryCatchVoid()
    {
        try
        {
            Console.WriteLine("abc");
        }
        finally
        {
            Console.WriteLine(DateTime.Now);
        }
    }

    public static async Task<int> TaskTMethod()
    {
        await Task.Delay(100).ConfigureAwait(false);

        return DateTime.Now.Millisecond;
    }

    public static Task TaskMethod() => TaskTMethod();

    public static async ValueTask<int> ValueTTask()
    {
        await Task.Delay(100).ConfigureAwait(false);

        return DateTime.Now.Millisecond;
    }

    public static ValueTask ValueTask() => new(Task.CompletedTask);

    public static FSharpAsync<int> FSharp() => FSharpAsync.AwaitTask(TaskTMethod());

    public static TestAwaitable<int> Awaitable() => new(() => 3);

    public static TestAwaitable Awaitable2() => new(() => true);

    public static TestAwaitableWithICriticalNotifyCompletion CriticalNotifyCompletion() => new();

    public static TestAwaitableWithoutICriticalNotifyCompletion NotifyCompletion() => new();
}
