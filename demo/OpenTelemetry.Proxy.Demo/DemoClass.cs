using OpenTelemetry.Proxy;

[ActivitySource]
public class DemoClass
{
    private readonly AsyncLocal<string> _asyncLocal = new();

    [Activity]
    public virtual async Task<T> Demo<T>(T arg)
    {
        Console.WriteLine($"Demo begin: {_asyncLocal.Value}");

        _asyncLocal.Value = "Demo";

        await Demo2().ConfigureAwait(false);

        Console.WriteLine($"Demo middle: {_asyncLocal.Value}");

        await Demo5().ConfigureAwait(false);

        Console.WriteLine($"Demo end: {_asyncLocal.Value}");

        return arg;
    }

    [Activity]
    public virtual async ValueTask Demo2()
    {
        Console.WriteLine($"Demo2 begin: {_asyncLocal.Value}");

        _asyncLocal.Value = "Demo2";

        var task = Demo3();

        await Demo4().ConfigureAwait(false);

        Console.WriteLine($"Demo2 middle: {_asyncLocal.Value}");

        await task.ConfigureAwait(false);

        Console.WriteLine($"Demo2 end: {_asyncLocal.Value}");
    }

    [Activity(Tags = [ActivityTagAttribute.ReturnValueTagName])]
    public virtual async Task<int> Demo3()
    {
        Console.WriteLine($"Demo3 begin: {_asyncLocal.Value}");

        _asyncLocal.Value = "Demo3";

        await Task.Delay(Random.Shared.Next(20, 200)).ConfigureAwait(false);

        Console.WriteLine($"Demo3 end: {_asyncLocal.Value}");

        return DateTime.Now.Microsecond;
    }

    [Activity]
    [return: ActivityTag("__ReturnValue__")]
    public virtual async Task<DateTime> Demo4()
    {
        Console.WriteLine($"Demo4 begin: {_asyncLocal.Value}");

        _asyncLocal.Value = "Demo4";

        await Task.Delay(Random.Shared.Next(20, 200)).ConfigureAwait(false);

        Console.WriteLine($"Demo4 end: {_asyncLocal.Value}");

        return DateTime.Now;
    }

    [Activity]
    public virtual async ValueTask Demo5()
    {
        Console.WriteLine($"Demo5 begin: {_asyncLocal.Value}");

        _asyncLocal.Value = "Demo5";

        await Task.WhenAll(Demo3(), Demo4()).ConfigureAwait(false);

        Console.WriteLine($"Demo5 end: {_asyncLocal.Value}");
    }
}
