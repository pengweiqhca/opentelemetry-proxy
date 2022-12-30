using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy.Tests.Common;

public class TestAwaitable
{
    private readonly int _result;
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = new();

    public TestAwaitable(int result)
    {
        _result = result;

        // Simulate a brief delay before completion
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(100);
            SetCompleted();
        });
    }

    private void SetCompleted()
    {
        _isCompleted = true;

        foreach (var callback in _onCompletedCallbacks) callback();
    }

    public TestAwaiter<int> GetAwaiter() => new(this, _result);

    public readonly struct TestAwaiter<TResult> : INotifyCompletion
    {
        private readonly TestAwaitable _owner;
        private readonly TResult _result;

        public TestAwaiter(TestAwaitable owner, TResult result)
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

        public TResult GetResult() => _result;
    }
}

public class TestAwaitable<TResult>
{
    private readonly TResult _result;
    private bool _isCompleted;
    private readonly List<Action> _onCompletedCallbacks = new();

    public TestAwaitable(TResult result)
    {
        _result = result;

        // Simulate a brief delay before completion
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(100);
            SetCompleted();
        });
    }

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
