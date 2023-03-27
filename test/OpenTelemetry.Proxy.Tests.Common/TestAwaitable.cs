using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy.Tests.Common;

public class TestAwaitable
{
    private bool _result;
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = new();

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

    public readonly struct TestAwaiter<TResult> : INotifyCompletion
    {
        private readonly TestAwaitable _owner;
        private readonly Func<TResult> _result;

        public TestAwaiter(TestAwaitable owner, Func<TResult> result)
        {
            _owner = owner;
            _result = result;
        }

        public bool IsCompleted => _owner._isCompleted;

        public void OnCompleted(Action continuation)
        {
            if (_owner._isCompleted) continuation();
            else _owner._onCompletedCallbacks.Add(continuation);
        }

        public TResult GetResult() => _result();
    }
}

public class TestAwaitable<TResult>
{
    private TResult _result = default!;
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = new();

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

    public readonly struct TestAwaiter : INotifyCompletion
    {
        private readonly TestAwaitable<TResult> _owner;

        public TestAwaiter(TestAwaitable<TResult> owner) => _owner = owner;

        public bool IsCompleted => _owner._isCompleted;

        public void OnCompleted(Action continuation)
        {
            if (_owner._isCompleted) continuation();
            else _owner._onCompletedCallbacks.Add(continuation);
        }

        public TResult GetResult() => _owner._result;
    }
}
