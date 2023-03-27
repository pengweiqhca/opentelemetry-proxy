using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy.Tests.Common;

public class TestAwaitableWithICriticalNotifyCompletion
{
    public TestAwaiterWithICriticalNotifyCompletion GetAwaiter() => new();
}

public class TestAwaitableWithoutICriticalNotifyCompletion
{
    public TestAwaiterWithoutICriticalNotifyCompletion GetAwaiter() => new();
}

public class TestAwaiterWithICriticalNotifyCompletion
    : CompletionTrackingAwaiterBase, ICriticalNotifyCompletion { }

public class TestAwaiterWithoutICriticalNotifyCompletion
    : CompletionTrackingAwaiterBase, INotifyCompletion { }

public class CompletionTrackingAwaiterBase
{
    private string? _result;

    public bool IsCompleted { get; private set; }

    public string? GetResult() => _result;

    public void OnCompleted(Action continuation)
    {
        _result = "Used OnCompleted";
        IsCompleted = true;
        continuation();
    }

    public void UnsafeOnCompleted(Action continuation)
    {
        _result = "Used UnsafeOnCompleted";
        IsCompleted = true;
        continuation();
    }
}
