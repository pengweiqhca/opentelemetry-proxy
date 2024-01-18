using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy.Tests.Common;

public class TestAwaitable
{
    private bool _result;
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = [];

    public TestAwaitable(Func<bool> resultFunc) => ThreadPool.QueueUserWorkItem(static state =>
    {
        var (testAwaitable, resultFunc) = ((TestAwaitable, Func<bool>))state!;

        Thread.Sleep(100);

        testAwaitable._result = resultFunc();

        testAwaitable.SetCompleted();
    }, (this, resultFunc));

    private void SetCompleted()
    {
        _isCompleted = true;

        foreach (var callback in _onCompletedCallbacks) callback();
    }

    public TestAwaiter<bool> GetAwaiter() => new(this, () => this._result);

    public readonly struct TestAwaiter<TResult>(TestAwaitable owner, Func<TResult> result) : INotifyCompletion
    {
        public bool IsCompleted => owner._isCompleted;

        public void OnCompleted(Action continuation)
        {
            if (owner._isCompleted) continuation();
            else owner._onCompletedCallbacks.Add(continuation);
        }

        public TResult GetResult() => result();
    }
}

public class TestAwaitable<TResult>
{
    private TResult _result = default!;
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = [];

    public TestAwaitable(Func<TResult> resultFunc) => ThreadPool.QueueUserWorkItem(static state =>
    {
        var (testAwaitable, resultFunc) = ((TestAwaitable<TResult>, Func<TResult>))state!;

        Thread.Sleep(100);

        testAwaitable._result = resultFunc();

        testAwaitable.SetCompleted();
    }, (this, resultFunc));

    private void SetCompleted()
    {
        _isCompleted = true;

        foreach (var callback in _onCompletedCallbacks) callback();
    }

    public TestAwaiter GetAwaiter() => new(this);

    public readonly struct TestAwaiter(TestAwaitable<TResult> owner) : INotifyCompletion
    {
        public bool IsCompleted => owner._isCompleted;

        public void OnCompleted(Action continuation)
        {
            if (owner._isCompleted) continuation();
            else owner._onCompletedCallbacks.Add(continuation);
        }

        public TResult GetResult() => owner._result;
    }
}
