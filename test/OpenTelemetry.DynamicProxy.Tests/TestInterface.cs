using OpenTelemetry.Proxy;
using System.Runtime.CompilerServices;

namespace OpenTelemetry.DynamicProxy.Tests;

[ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
public interface ITestInterface
{
    public DateTime Now { get; }

    void Method0();

    [return: ActivityTag("abc")]
    int Method1();

    Task Method2();

    Task<int> Method3([ActivityTag] int delay);

    ValueTask Method4([ActivityTag] int delay);

    [Activity(Tags = ["Field", ActivityTagAttribute.ReturnValueTagName])]
    ValueTask<int> Method5([ActivityTag] int delay);

    [Activity(Tags = [nameof(Now)])]
    TestExceptionAwaitable<int> Method6([ActivityTag] int delay);

    [Activity(SuppressInstrumentation = true)]
    bool Method7();
}

public class TestInterface1 : ITestInterface
{
 #pragma warning disable CS0414
    private static readonly string Field = "Abc";
 #pragma warning restore CS0414

    public DateTime Now => new(2024, 1, 1);

    public void Method0() { }

    public int Method1() => 1;

    public Task Method2() => Task.CompletedTask;

    public async Task<int> Method3(int delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        throw new NotSupportedException();
    }

    public async ValueTask Method4(int delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        throw new NotSupportedException();
    }

    public async ValueTask<int> Method5(int delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        return delay;
    }

    public TestExceptionAwaitable<int> Method6(int delay) => new(delay);

    public bool Method7() => Sdk.SuppressInstrumentation;
}

public class TestExceptionAwaitable<T>
{
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = [];

    public TestExceptionAwaitable(int delay) => ThreadPool.QueueUserWorkItem(_ =>
    {
        Thread.Sleep(delay);

        SetCompleted();
    });

    private void SetCompleted()
    {
        _isCompleted = true;

        foreach (var callback in _onCompletedCallbacks) callback();
    }

    public TestAwaiter GetAwaiter() => new(this);

    public readonly struct TestAwaiter(TestExceptionAwaitable<T> owner) : INotifyCompletion
    {
        public bool IsCompleted => owner._isCompleted;

        public void OnCompleted(Action continuation)
        {
            if (owner._isCompleted) continuation();
            else owner._onCompletedCallbacks.Add(continuation);
        }

        public T GetResult() => throw new NotSupportedException();
    }
}

public interface ITestInterface2 : ITestInterface, IDisposable;

public class TestInterface2 : TestInterface1, ITestInterface2
{
    public void Dispose() { }
}
