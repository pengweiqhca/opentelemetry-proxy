using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Moq;
using Moq.Protected;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace OpenTelemetry.StaticProxy.Fody.Tests;

public class ActivityAwaiterEmitterTest(ITestOutputHelper output)
{
    private static readonly string Name = Guid.NewGuid().ToString();

    [Theory]
    [MemberData(nameof(TestData))]
    public async Task Test(bool success, Delegate func)
    {
        var context = GetContext();

        var awaiterType = func.Method.ReturnType;

        Assert.True(CoercedAwaitableInfo.IsTypeAwaitable(context.TargetModule.ImportReference(awaiterType), out var coercedAwaitableInfo));

        var getAwaiter = awaiterType.GetMethod(nameof(Task.GetAwaiter));

        Assert.NotNull(getAwaiter);

        awaiterType = getAwaiter.ReturnType;

        var isVoid = coercedAwaitableInfo.AwaitableInfo.AwaiterGetResultMethod.ReturnType
            .HaveSameIdentity(context.TargetModule.TypeSystem.Void);

        context.ActivityAwaiterEmitter.GetActivityAwaiter(coercedAwaitableInfo.AwaitableInfo, isVoid
        );

        var awaitable = func.Method.Invoke(func.Target, null);

        Assert.NotNull(awaitable);

        var awaiter = getAwaiter.Invoke(awaitable, []);

        var type = SaveAndLoad(context, output).GetTypes()[0];

        if (awaiterType.IsGenericType) type = type.MakeGenericType(awaiterType.GenericTypeArguments);

        var mock = new Mock<Activity>(Name);

        var tcs = new TaskCompletionSource();

        //protected virtual void Dispose(bool disposing)
        mock.Protected().Setup(nameof(IDisposable.Dispose), ItExpr.IsAny<bool>())
            .Callback(tcs.SetResult);

        type.GetMethod(nameof(TaskAwaiter.OnCompleted), BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, isVoid ? [awaiter, mock.Object] : [awaiter, mock.Object, null]);

        if (await Task.WhenAny(tcs.Task, Task.Delay(5000)).ConfigureAwait(false) != tcs.Task)
            Assert.Fail("Timeout");

        if (success)
            Assert.Equal(ActivityStatusCode.Unset, mock.Object.Status);
        else
        {
            Assert.Equal(ActivityStatusCode.Error, mock.Object.Status);
            Assert.Equal(Name, mock.Object.StatusDescription);

            Assert.Contains(mock.Object.Events, x => x.Name == "exception");
        }

        mock.Protected().Verify("Dispose", Times.Once(), ItExpr.IsAny<bool>());
    }

    public static IEnumerable<object[]> TestData()
    {
        yield return
        [
            true, () => Task.CompletedTask
        ];

        yield return
        [
            false, () => Task.FromException(new(Name))
        ];

        yield return
        [
            true, () => Task.Delay(1000)
        ];

        yield return
        [
            true, () => Task.FromResult(1)
        ];

        yield return
        [
            false, () => Task.FromException<int>(new(Name))
        ];

        yield return
        [
            true, () => Task.Delay(1000).ContinueWith(_ => 1)
        ];

        yield return
        [
            true, () => new ValueTask()
        ];

        yield return
        [
            true, () => new ValueTask(Task.Delay(1000))
        ];

        yield return
        [
            true, () => new ValueTask<int>(1)
        ];

        yield return
        [
            true, () => new ValueTask(Task.Delay(1000).ContinueWith(_ => 1))
        ];

        yield return
        [
            true, () => new TestAwaitable(() => true)
        ];

        yield return
        [
            true, () => new TestAwaitable<int>(() => 1)
        ];

        yield return
        [
            true, () => new TestAwaitableWithICriticalNotifyCompletion()
        ];

        yield return
        [
            true, () => new TestAwaitableWithoutICriticalNotifyCompletion()
        ];
    }

    private static EmitContext GetContext() => new(
        AssemblyDefinition.CreateAssembly(new(Name, new(1, 0, 0)), Name, ModuleKind.Dll).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(Activity).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(SuppressInstrumentationScope).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(BaseProvider).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(ActivityAttribute).Assembly.Location).MainModule);

    private static Assembly SaveAndLoad(EmitContext context, ITestOutputHelper output)
    {
        var path = Path.Combine(AppContext.BaseDirectory, Path.GetTempFileName());

        context.TargetModule.Assembly.Write(path);

        output.WriteLine(path);

        return Assembly.LoadFile(path);
    }
}
