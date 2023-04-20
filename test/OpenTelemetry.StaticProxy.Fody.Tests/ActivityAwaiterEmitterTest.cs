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

public class ActivityAwaiterEmitterTest
{
    private static readonly string name = Guid.NewGuid().ToString();
    private readonly ITestOutputHelper _output;

    public ActivityAwaiterEmitterTest(ITestOutputHelper output) => _output = output;

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

        context.ActivityAwaiterEmitter.GetActivityAwaiter(coercedAwaitableInfo.AwaitableInfo);

        var awaitable = func.Method.Invoke(func.Target, null);

        Assert.NotNull(awaitable);

        var awaiter = getAwaiter.Invoke(awaitable, Array.Empty<object>());

        var type = SaveAndLoad(context, _output).GetTypes()[0];

        if (awaiterType.IsGenericType) type = type.MakeGenericType(awaiterType.GenericTypeArguments);

        var mock = new Mock<Activity>(name);

        var tcs = new TaskCompletionSource();

        //protected virtual void Dispose(bool disposing)
        mock.Protected().Setup(nameof(IDisposable.Dispose), ItExpr.IsAny<bool>())
            .Callback(tcs.SetResult);

        type.GetMethod(nameof(TaskAwaiter.OnCompleted), BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new[] { awaiter, mock.Object, null });

        if (await Task.WhenAny(tcs.Task, Task.Delay(5000)).ConfigureAwait(false) != tcs.Task)
            Assert.Fail("Timeout");

        if (success)
            Assert.Equal(ActivityStatusCode.Unset, mock.Object.Status);
        else
        {
            Assert.Equal(ActivityStatusCode.Error, mock.Object.Status);
            Assert.Equal(name, mock.Object.StatusDescription);

            Assert.Contains(mock.Object.Events, x => x.Name == "exception");
        }

        mock.Protected().Verify("Dispose", Times.Once(), ItExpr.IsAny<bool>());
    }

    public static IEnumerable<object[]> TestData()
    {
        yield return new object[]
        {
            true, () => Task.CompletedTask
        };

        yield return new object[]
        {
            false, () => Task.FromException(new(name))
        };

        yield return new object[]
        {
            true, () => Task.Delay(1000)
        };

        yield return new object[]
        {
            true, () => Task.FromResult(1)
        };

        yield return new object[]
        {
            false, () => Task.FromException<int>(new(name))
        };

        yield return new object[]
        {
            true, () => Task.Delay(1000).ContinueWith(_ => 1)
        };

        yield return new object[]
        {
            true, () => new ValueTask()
        };

        yield return new object[]
        {
            true, () => new ValueTask(Task.Delay(1000))
        };

        yield return new object[]
        {
            true, () => new ValueTask<int>(1)
        };

        yield return new object[]
        {
            true, () => new ValueTask(Task.Delay(1000).ContinueWith(_ => 1))
        };

        yield return new object[]
        {
            true, () => new TestAwaitable(() => true)
        };

        yield return new object[]
        {
            true, () => new TestAwaitable<int>(() => 1)
        };

        yield return new object[]
        {
            true, () => new TestAwaitableWithICriticalNotifyCompletion()
        };

        yield return new object[]
        {
            true, () => new TestAwaitableWithoutICriticalNotifyCompletion()
        };
    }

    private static EmitContext GetContext() => new(
        AssemblyDefinition.CreateAssembly(new(name, new(1, 0, 0)), name, ModuleKind.Dll).MainModule,
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
