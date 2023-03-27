using OpenTelemetry.Proxy;
using System.Runtime.CompilerServices;

namespace OpenTelemetry.DynamicProxy.Tests;

[ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
public interface ITestInterface
{
    void Method0();

    int Method1();

    Task Method2();

    Task<int> Method3(int delay);

    ValueTask Method4(int delay);

    ValueTask<int> Method5(int delay);

    TestExceptionAwaitable<int> Method6(int delay);
}

public class TestInterface1 : ITestInterface
{
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

 #pragma warning disable CA2255
    [ModuleInitializer]
 #pragma warning restore CA2255
    public static void Initialize()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
    }
}

public class TestExceptionAwaitable<T>
{
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = new();

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

    public readonly struct TestAwaiter : INotifyCompletion
    {
        private readonly TestExceptionAwaitable<T> _owner;

        public TestAwaiter(TestExceptionAwaitable<T> owner) : this() => _owner = owner;

        public bool IsCompleted => _owner._isCompleted;

        public void OnCompleted(Action continuation)
        {
            if (_owner._isCompleted) continuation();
            else _owner._onCompletedCallbacks.Add(continuation);
        }

        public T GetResult() => throw new NotSupportedException();
    }
}
