namespace OpenTelemetry.DynamicProxy.Tests;

[ActivitySource]
public interface ITestInterface1
{
    void Method0();

    int Method1();

    Task Method2();

    Task<int> Method3(int delay);

    ValueTask Method4();

    ValueTask<int> Method5();

    IAsyncEnumerable<int> Method6(int delay);
}

public class TestInterface1 : ITestInterface1
{
    public void Method0() { }

    public int Method1() => 1;

    public Task Method2() => Task.CompletedTask;

    public async Task<int> Method3(int delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        throw new NotSupportedException();
    }

    public ValueTask Method4() => throw new NotSupportedException();

    public ValueTask<int> Method5() => new(1);

    public async IAsyncEnumerable<int> Method6(int delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        yield return 1;

        await Task.Delay(delay).ConfigureAwait(false);

        yield return 2;
    }
}
