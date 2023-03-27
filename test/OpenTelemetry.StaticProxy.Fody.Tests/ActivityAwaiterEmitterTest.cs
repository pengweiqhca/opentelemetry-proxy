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
    private readonly ITestOutputHelper _output;

    public ActivityAwaiterEmitterTest(ITestOutputHelper output) => _output = output;

    [Theory]
    [MemberData(nameof(TestData))]
    public async Task Test(bool success, Delegate func)
    {
        var context = GetContext();

        var awaiterType = func.Method.ReturnType;

        context.ActivityAwaiterEmitter.GetActivityAwaiter(context.TargetModule.ImportReference(awaiterType),
            context.TargetModule.ImportReference(awaiterType
                .GetMethod(nameof(TaskAwaiter.GetResult))!));

        var awaiter = func.Method.Invoke(func.Target, null);

        var type = SaveAndLoad(context, _output).GetTypes()[0];
        if (awaiterType.IsGenericType) type = type.MakeGenericType(awaiterType.GenericTypeArguments);

        //protected virtual void Dispose(bool disposing)
        var mock = new Mock<Activity>("Test");

        var tcs = new TaskCompletionSource();

        mock.Protected().Setup("Dispose", ItExpr.IsAny<bool>())
            .Callback(tcs.SetResult);

        if ((bool)awaiterType.GetProperty(nameof(TaskAwaiter.IsCompleted))!.GetValue(awaiter)!)
            type.GetMethod("OnCompleted", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new[]
            {
                mock.Object, awaiter
            });
        else
        {
            var action = type.GetMethod("OnCompleted", BindingFlags.Public | BindingFlags.Instance)!
                .CreateDelegate<Action>(
                    Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.Instance, null, new[]
                    {
                        mock.Object, awaiter
                    }, null));

            if (awaiter is ICriticalNotifyCompletion criticalNotifyCompletion)
                criticalNotifyCompletion.UnsafeOnCompleted(action);
            else if (awaiter is INotifyCompletion notifyCompletion)
                notifyCompletion.OnCompleted(action);
            else Assert.Fail("Unknown awaiter type");
        }

        if (await Task.WhenAny(tcs.Task, Task.Delay(5000)).ConfigureAwait(false) != tcs.Task)
            Assert.Fail("Timeout");

        if (success)
            Assert.Equal(ActivityStatusCode.Unset, mock.Object.Status);
        else
        {
            Assert.Equal(ActivityStatusCode.Error, mock.Object.Status);
            Assert.Equal("Test", mock.Object.StatusDescription);

            Assert.Contains(mock.Object.Events, x => x.Name == "exception");
        }

        mock.Protected().Verify("Dispose", Times.Once(), ItExpr.IsAny<bool>());
    }

    public static IEnumerable<object[]> TestData()
    {
        yield return new object[]
        {
            true, () => Task.CompletedTask.GetAwaiter()
        };

        yield return new object[]
        {
            false, () => Task.FromException(new("Test")).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => Task.Delay(1000).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => Task.FromResult(1).GetAwaiter()
        };

        yield return new object[]
        {
            false, () => Task.FromException<int>(new("Test")).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => Task.Delay(1000).ContinueWith(_ => 1).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new ValueTask().GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new ValueTask(Task.Delay(1000)).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new ValueTask<int>(1).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new ValueTask(Task.Delay(1000).ContinueWith(_ => 1)).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new TestAwaitable(() => true).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new TestAwaitable<int>(() => 1).GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new TestAwaitableWithICriticalNotifyCompletion().GetAwaiter()
        };

        yield return new object[]
        {
            true, () => new TestAwaitableWithoutICriticalNotifyCompletion().GetAwaiter()
        };
    }

    private static EmitContext GetContext() => new(
        AssemblyDefinition.CreateAssembly(new("Test", new(1, 0, 0)), "Test", ModuleKind.Dll).MainModule,
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
