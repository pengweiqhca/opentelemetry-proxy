using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;

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

    [Activity]
    [ActivityTags("Field", "$returnvalue")]
    ValueTask<int> Method5([ActivityTag] int delay);

    [Activity]
    [ActivityTags(nameof(Now))]
    TestAwaitable<int> Method6([ActivityTag] int delay);

    [Activity(SuppressInstrumentation = true)]
    bool Method7();
}

public class TestInterface1 : ITestInterface
{
    public DateTime Now => new(2024, 1, 1);

    public void Method0() { }

    public int Method1() => 1;

    [Activity]
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

    public TestAwaitable<int> Method6(int delay) => new(() => throw new NotSupportedException());

    public bool Method7() => Sdk.SuppressInstrumentation;
}

public interface ITestInterface2 : ITestInterface, IDisposable;

public class TestInterface2 : TestInterface1, ITestInterface2
{
    public void Dispose() { }
}
