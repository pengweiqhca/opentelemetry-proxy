namespace OpenTelemetry.Proxy.Tests.Common;

public class ActivityExtensionsTest
{
    [Fact]
    public void GenericType()
    {
        var activity = new Activity(nameof(PrimitiveType));

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid();

        activity.SetTagEnumerable(key, value);

        Assert.Equal(value, activity.GetTagItem(key));
    }

    [Fact]
    public void PrimitiveType()
    {
        var activity = new Activity(nameof(PrimitiveType));

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        activity.SetTagEnumerable(key, value);

        Assert.Equal(value, activity.GetTagItem(key));
    }

    [Fact]
    public void Dictionary()
    {
        var activity = new Activity(nameof(Dictionary));

        var key = Guid.NewGuid().ToString();
        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();

        var value = new Dictionary<string, string>
        {
            { key1, Guid.NewGuid().ToString() },
            { key2, Guid.NewGuid().ToString() }
        };

        activity.SetTagEnumerable(key, value);

        Assert.Equal(value[key1], activity.GetTagItem($"{key}.{key1}"));
        Assert.Equal(value[key2], activity.GetTagItem($"{key}.{key2}"));
    }

    [Fact]
    public void Tuple()
    {
        var activity = new Activity(nameof(Tuple));

        var key = Guid.NewGuid().ToString();

        var value = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        activity.SetTagEnumerable(key, value);

        Assert.Equal(value.Item1, activity.GetTagItem($"{key}.Item1"));
        Assert.Equal(value.Item2, activity.GetTagItem($"{key}.Item2"));
    }

    [Fact]
    public void Depth()
    {
        var activity = new Activity(nameof(Depth));

        var key = Guid.NewGuid().ToString();
        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();

        var value = new Dictionary<string, string[]>
        {
            { key1, [Guid.NewGuid().ToString()] },
            { key2, [Guid.NewGuid().ToString()] }
        };

        activity.SetTagEnumerable(key, value);

        Assert.Equal(value[key1], activity.GetTagItem($"{key}.{key1}"));
        Assert.Equal(value[key2], activity.GetTagItem($"{key}.{key2}"));
    }
}
