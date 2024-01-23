using FluentAssertions;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using System.Reflection;
using Xunit.Abstractions;

namespace OpenTelemetry.StaticProxy.Fody.Tests;

public class StaticProxyEmitterTest(ITestOutputHelper output)
{
    [Fact]
    public void GetActivityTags()
    {
        List<string> tags = ["_now", "Now", "e", Guid.NewGuid().ToString("N")];

        var emitter = CreateEmitter(typeof(TestClass3));

        var (startInstructions, endInstructions) = emitter.GetActivityTags(
            emitter.Context.TargetModule.GetType(typeof(TestClass3).FullName)
                .GetMethods(nameof(TestClass3.StaticMethod)).Single(), tags, out var returnValueTagName);

        Assert.Equal("ghi", returnValueTagName);
        Assert.Equal(5, startInstructions.Count);
        Assert.Equal(2, endInstructions.Count);

        AssertInstructions(startInstructions[0], """
                                                 IL_0000: ldstr "def"
                                                 IL_0000: call System.DateTimeOffset OpenTelemetry.Proxy.Tests.Common.TestClass3::get_Now2()
                                                 IL_0000: box System.DateTimeOffset
                                                 """);

        AssertInstructions(startInstructions[1], """
                                                 IL_0000: ldstr "a2"
                                                 IL_0000: ldarg.0
                                                 IL_0000: box System.Int32
                                                 """);

        AssertInstructions(startInstructions[2], """
                                                 IL_0000: ldstr "b"
                                                 IL_0000: ldarg.2
                                                 IL_0000: ldind.ref
                                                 IL_0000: box System.Int32
                                                 """);

        AssertInstructions(startInstructions[3], """
                                                 IL_0000: ldstr "d"
                                                 IL_0000: ldarg.s d
                                                 IL_0000: ldind.ref
                                                 IL_0000: box System.Int32
                                                 """);

        AssertInstructions(startInstructions[4], """
                                                 IL_0000: ldstr "e"
                                                 IL_0000: ldarg.s e
                                                 IL_0000: box System.Int32
                                                 """);

        AssertInstructions(endInstructions[0], """
                                               IL_0000: ldstr "c"
                                               IL_0000: ldarg.3
                                               IL_0000: ldind.ref
                                               IL_0000: box System.DateTimeOffset
                                               """);

        AssertInstructions(endInstructions[1], """
                                               IL_0000: ldstr "d$out"
                                               IL_0000: ldarg.s d
                                               IL_0000: ldind.ref
                                               IL_0000: box System.Int32
                                               """);

        static void AssertInstructions(IEnumerable<Instruction> instructions, string il) =>
            Assert.Equal(il, string.Join(Environment.NewLine, instructions));
    }

    [Fact]
    public void FullTags()
    {
        var activity = new Activity("test").Start();

        var emitter = CreateEmitter(typeof(TestClass3));

        var name = Guid.NewGuid().ToString("N");
        var activityName = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.EmitActivity(emitter.Context.TargetModule.GetType(typeof(TestClass3).FullName)
                .GetMethods(nameof(TestClass3.StaticMethod)).Single(), false,
            emitter.AddActivitySource(name, version), activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, output);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == name && activitySource.Version == version,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        var type = assembly.GetType(typeof(TestClass3).FullName!);

        Assert.NotNull(type);

        var method = type.GetMethod(nameof(TestClass3.StaticMethod))?.CreateDelegate<TestClass3.TestDelegate>();

        Assert.NotNull(method);

        var random = Random.Shared;

        var now = DateTime.Now.AddDays(-1);
        var now2 = DateTime.Now.AddDays(1);

        var a = random.Next();
        var a2 = random.Next();
        var b = random.Next();
        var d = random.Next();
        var d2 = d;
        var e = random.Next();

        var result = method(a, a2, b, out var c, ref d, e);

        Assert.Equal(a2, result);

        activity = Assert.Single(list);

        AssertTag(activity, "def", type.GetProperty(nameof(TestClass3.Now2))!.GetValue(null));
        AssertTag(activity, "a2", a);
        AssertTag(activity, "b", b);
        AssertTag(activity, "c", c);
        AssertTag(activity, "d", d2);
        AssertTag(activity, "d$out", d);
        AssertTag(activity, "e", e);
        AssertTag(activity, "ghi", a2);

        static void AssertTag(Activity activity, string key, object? value) => activity.GetTagItem(key).Should()
            .Be(value, "Activity tag `{0}` should be equal", key);
    }

    [Fact]
    public void AddActivitySourceTest()
    {
        var emitter = CreateEmitter();

        var name = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.AddActivitySource(name, version);
        emitter.AddActivitySource(name, version);

        var assembly = SaveAndLoad(emitter, output);

        var type = assembly.GetTypes().Single(t => t.Name == "@ActivitySource@");

        var field = type.GetField($"{name}@{version}", BindingFlags.Static | BindingFlags.Public)!;

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

        emitter.EmitSuppressInstrumentationScope(emitter.Context.TargetModule
            .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
            .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.SuppressInstrumentationScope)), false);

        var assembly = SaveAndLoad(emitter, output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.SuppressInstrumentationScope));

        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method.Invoke(null, [])));

        Assert.False(StaticProxyEmitterTestClass.SuppressInstrumentationScope());
    }

    [Fact]
    public void EmitActivityNameTest()
    {
        var (activityName, tags, availableTimes) = StaticProxyEmitterTestClass.GetActivityName();

        Assert.Null(activityName);
        Assert.Null(tags);
        Assert.Equal(0, availableTimes);

        var emitter = CreateEmitter();

        activityName = Guid.NewGuid().ToString("N");
        availableTimes = DateTime.Now.Millisecond + 1;

        emitter.EmitActivityName(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
                .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.ActivityName)),
            false, activityName, availableTimes);

        var assembly = SaveAndLoad(emitter, output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.ActivityName));

        Assert.NotNull(method);

        var tuple = Assert.IsType<Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int>>(
            method.Invoke(null, [123]));

        Assert.Equal(activityName, tuple.Item1);
        Assert.NotNull(tuple.Item2);
        Assert.Equal(123, tuple.Item2.Single(x => x.Key == "delay").Value);
        Assert.Equal(availableTimes, tuple.Item3);

        (activityName, tags, availableTimes) = StaticProxyEmitterTestClass.GetActivityName();

        Assert.Null(activityName);
        Assert.Null(tags);
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

        emitter.EmitActivity(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
                .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.GetCurrentActivity)),
            false, emitter.AddActivitySource(name, version), activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, output);

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

        var result = method.Invoke(null, []);

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

        emitter.EmitActivity(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
                .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.TryCatch)),
            false, emitter.AddActivitySource(name, version), activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, output);

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

        var result = method.Invoke(null, []);

        Assert.Equal(Assert.Single(list), Assert.IsType<Activity>(result));
    }

    [Fact]
    public void VoidTest()
    {
        var emitter = CreateEmitter();

        emitter.EmitSuppressInstrumentationScope(emitter.Context.TargetModule
            .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
            .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.Void)), true);

        var assembly = SaveAndLoad(emitter, output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.Void));

        Assert.NotNull(method);

        method.Invoke(null, []);
    }

    [Fact]
    public void TryCatchVoidTest()
    {
        var emitter = CreateEmitter();

        emitter.EmitSuppressInstrumentationScope(emitter.Context.TargetModule
            .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
            .Single(static m => m.Name == nameof(StaticProxyEmitterTestClass.TryCatchVoid)), true);

        var assembly = SaveAndLoad(emitter, output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(
            nameof(StaticProxyEmitterTestClass.TryCatchVoid));

        Assert.NotNull(method);

        method.Invoke(null, []);
    }

    [Theory]
    [MemberData(nameof(AsyncMethods))]
    public async Task SuppressInstrumentationScopeAsync(string methodName, Func<object, Task> func)
    {
        var emitter = CreateEmitter();

        emitter.EmitSuppressInstrumentationScope(emitter.Context.TargetModule
            .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
            .Single(m => m.Name == methodName), false);

        var assembly = SaveAndLoad(emitter, output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(methodName);

        Assert.NotNull(method);

        await func(method.Invoke(null, [])!).ConfigureAwait(false);
    }

    [Theory]
    [MemberData(nameof(AsyncMethods))]
    public async Task ActivityNameAsync(string methodName, Func<object, Task> func)
    {
        var emitter = CreateEmitter();

        var activityName = Guid.NewGuid().ToString("N");
        var availableTimes = DateTime.Now.Millisecond + 1;

        emitter.EmitActivityName(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
                .Single(m => m.Name == methodName),
            false, activityName, availableTimes);

        var assembly = SaveAndLoad(emitter, output);

        var method = assembly.GetType(typeof(StaticProxyEmitterTestClass).FullName!)!.GetMethod(methodName);

        Assert.NotNull(method);

        await func(method.Invoke(null, [])!).ConfigureAwait(false);
    }

    [Theory]
    [MemberData(nameof(AsyncMethods))]
    public async Task ActivityAsync(string methodName, Func<object, Task> func)
    {
        var _ = new Activity("test").Start();

        var emitter = CreateEmitter();

        var name = Guid.NewGuid().ToString("N");
        var activityName = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.EmitActivity(emitter.Context.TargetModule
                .GetType(typeof(StaticProxyEmitterTestClass).FullName).GetMethods()
                .Single(m => m.Name == methodName),
            false, emitter.AddActivitySource(name, version), activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, output);

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

        await func(method.Invoke(null, [])!).ConfigureAwait(false);

        Assert.Single(list);
    }

    public static IEnumerable<object[]> AsyncMethods()
    {
        yield return
        [
            nameof(StaticProxyEmitterTestClass.TaskMethod), (Func<object, Task>)(instance => (Task)instance)
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.GetCurrentActivity), (Func<object, Task>)(_ => Task.CompletedTask)
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.TaskTMethod), (Func<object, Task>)(instance => (Task)instance)
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.ValueTask),
            (Func<object, Task>)(instance => ((ValueTask)instance).AsTask())
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.ValueTTask),
            (Func<object, Task>)(instance => ((ValueTask<int>)instance).AsTask())
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.FSharp),
            (Func<object, Task>)(instance => FSharpAsync.StartAsTask((FSharpAsync<int>)instance,
                FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.None))
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.Awaitable),
            (Func<object, Task>)(async instance => await (TestAwaitable<int>)instance)
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.Awaitable2),
            (Func<object, Task>)(async instance => await (TestAwaitable)instance)
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.CriticalNotifyCompletion),
            (Func<object, Task>)(async instance => await (TestAwaitableWithICriticalNotifyCompletion)instance)
        ];

        yield return
        [
            nameof(StaticProxyEmitterTestClass.NotifyCompletion),
            (Func<object, Task>)(async instance => await (TestAwaitableWithoutICriticalNotifyCompletion)instance)
        ];
    }

    [Theory]
    [MemberData(nameof(GenericTestSource))]
    public async Task GenericTest(Delegate rawMethod, object?[] args, Func<object, Task> func)
    {
        var emitter = CreateEmitter();

        var type = rawMethod.Method.DeclaringType;

        Assert.NotNull(type);

        var typeName = (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName;

        Assert.NotNull(typeName);

        var name = Guid.NewGuid().ToString("N");
        var activityName = Guid.NewGuid().ToString("N");
        var version = DateTime.Now.ToString("HH.mm.ss.fff");

        emitter.EmitActivity(emitter.Context.TargetModule.GetType(typeName).GetMethods()
                .Single(m => m.Name == rawMethod.Method.Name),
            false, emitter.AddActivitySource(name, version),
            activityName, (int)ActivityKind.Client);

        var assembly = SaveAndLoad(emitter, output);

        var type2 = assembly.GetType(typeName);

        Assert.NotNull(type2);

        if (type.IsGenericType) type2 = type2.MakeGenericType(type.GetGenericArguments());

        var method = type2.GetMethod(rawMethod.Method.Name);

        Assert.NotNull(method);

        var list = new List<Activity>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == name && activitySource.Version == version,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = list.Add
        };

        ActivitySource.AddActivityListener(activityListener);

        await func(method.MakeGenericMethod(rawMethod.Method.GetGenericArguments()).Invoke(null, args)!).ConfigureAwait(false);

        Assert.Single(list);
    }

    public static IEnumerable<object[]> GenericTestSource()
    {
        yield return
        [
            new Func<int, Task<int>>(StaticProxyEmitterTestClass.GenericMethod),
            new object?[] { 1 },
            (Func<object, Task>)(instance => (Task)instance)
        ];

        yield return
        [
            new Func<bool, int, string, ValueTask<(bool, int, string)>>(StaticProxyEmitterTestClass<bool>.GenericMethod),
            new object?[] { true, 1, Guid.NewGuid().ToString() },
            (Func<object, Task>)(instance => ((ValueTask<(bool, int, string)>)instance).AsTask())
        ];

        yield return
        [
            new Func<bool, int, string, ValueTask<(bool, int, string)>>(StaticProxyEmitterTestClass<bool, int>.GenericMethod),
            new object?[] { true, 1, Guid.NewGuid().ToString() },
            (Func<object, Task>)(instance => ((ValueTask<(bool, int, string)>)instance).AsTask())
        ];
    }

    private static StaticProxyEmitter CreateEmitter(Type? type = null) => new(new(
        AssemblyDefinition.ReadAssembly((type ?? typeof(StaticProxyEmitterTest)).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(Activity).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(SuppressInstrumentationScope).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(BaseProvider).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(ActivityAttribute).Assembly.Location).MainModule));

    private static Assembly SaveAndLoad(StaticProxyEmitter emitter, ITestOutputHelper output)
    {
        var path = Path.Combine(AppContext.BaseDirectory, Path.GetTempFileName() + ".dll");

        emitter.Context.TargetModule.Assembly.Write(path);

        output.WriteLine(path);

        return Assembly.LoadFile(path);
    }
}

public static class StaticProxyEmitterTestClass
{
    public static bool SuppressInstrumentationScope() => Sdk.SuppressInstrumentation;

    public static Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int> GetActivityName(
        [ActivityTag] int delay = 300)
    {
        var field = typeof(ActivityAttribute).Assembly.GetType("OpenTelemetry.Proxy.ActivityName")
            ?.GetField("Holder", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var name = field.GetValue(null);

        Assert.NotNull(name);

        var nameHolder = name.GetType().GetProperty("Value")?.GetValue(name);

        return nameHolder == null
            ? new(null, default, 0)
            : new(nameHolder.GetType().GetField("Name")?.GetValue(nameHolder) as string,
                nameHolder.GetType().GetField("Tags")?.GetValue(nameHolder) as
                    IReadOnlyCollection<KeyValuePair<string, object?>>,
                Assert.IsType<int>(nameHolder.GetType().GetField("AvailableTimes")?.GetValue(nameHolder)));
    }

    public static Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int> ActivityName(
        [ActivityTag] int delay = 300) => GetActivityName(delay);

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

    public static void Void()
    {
        if (DateTime.Now.Second < 10) return;

        Console.WriteLine("abc");
    }

    public static void TryCatchVoid()
    {
        try
        {
            if (DateTime.Now.Second < 10) return;

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

    public static Task<T> GenericMethod<T>(T arg) => Task.FromResult(arg);

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

public static class StaticProxyEmitterTestClass<T>
{

    public static ValueTask<(T, T1, T2)> GenericMethod<T1, T2>(T t0, T1 t1, T2 t2) => new((t0, t1, t2));
}

public static class StaticProxyEmitterTestClass<T1, T2>
{

    public static ValueTask<(T1, T2, T)> GenericMethod<T>(T1 t1, T2 t2, T t3) => new((t1, t2, t3));
}
