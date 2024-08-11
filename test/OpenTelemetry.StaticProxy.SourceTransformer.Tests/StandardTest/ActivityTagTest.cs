namespace OpenTelemetry.StaticProxy.SourceTransformer.Tests.StandardTest;

public class ActivityTagTest
{
    [Fact]
    public async Task ActivityTagsTest()
    {
        var test = new ProxyRewriterTest("ActivityTagTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var context = Assert.IsAssignableFrom<MethodActivityNameContext>(
            Assert.Single(Assert.Single(results).MethodContexts.Values));

        Assert.Equal(["test"], context.UnknownTag);

        Assert.Equal(
            new() { ["abc"] = new("abc", ActivityTagFrom.Argument), ["def"] = new("age", ActivityTagFrom.Argument), },
            context.InTags);
    }

    [Fact]
    public async Task InoutTagsTest()
    {
        var test = new ProxyRewriterTest("ActivityTagTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal(2, typeMethods.MethodContexts.Count);

        var dic = new Dictionary<string, ActivityTagSource>
        {
            ["a2"] = new("a", ActivityTagFrom.Argument),
            ["b"] = new("b", ActivityTagFrom.Argument),
            ["d"] = new("d", ActivityTagFrom.Argument),
            ["e"] = new("e", ActivityTagFrom.Argument),
            ["Now"] = new("Now", ActivityTagFrom.InstanceFieldOrProperty),
            ["_now"] = new("_now", ActivityTagFrom.InstanceFieldOrProperty),
        };

        var activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.First());
        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Equal(new() { ["c"] = new("c", ActivityTagFrom.Argument), ["d"] = new("d", ActivityTagFrom.Argument), },
            activityTag.OutTags);

        Assert.Equal("ghi", activityTag.ReturnValueTag);

        dic.Remove("_now");
        dic.Remove("Now");

        activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.ElementAt(1));

        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Equal(new() { ["c"] = new("c", ActivityTagFrom.Argument), ["d"] = new("d", ActivityTagFrom.Argument), },
            activityTag.OutTags);

        Assert.Equal("$returnvalue", activityTag.ReturnValueTag);
    }

    [Fact]
    public async Task TypeHaveTagsTest()
    {
        var test = new ProxyRewriterTest("ActivityTagTestClass3");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal(new() { { "Abc", new(true, false) }, { "_abc", new(false, false) }, { "Now", new(true, true) } },
            typeMethods.Context.PropertyOrField);

        Assert.Equal(["Abc", "_abc"], typeMethods.Context.Tags);

        Assert.Equal(2, typeMethods.MethodContexts.Count);

        var dic = new Dictionary<string, ActivityTagSource>
        {
            ["abc"] = new("abc", ActivityTagFrom.Argument),
            ["age"] = new("age", ActivityTagFrom.Argument),
            ["Abc"] = new("Abc", ActivityTagFrom.StaticFieldOrProperty),
            ["_abc"] = new("_abc", ActivityTagFrom.InstanceFieldOrProperty),
        };

        var activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.First());
        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);
        Assert.Null(activityTag.ReturnValueTag);

        dic.Remove("_abc");

        activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.ElementAt(1));

        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);
        Assert.Null(activityTag.ReturnValueTag);
    }

    [Fact]
    public async Task TypeNoTagsTest()
    {
        var test = new ProxyRewriterTest("ActivityTagTestClass4");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal(new() { { "Abc", new(true, false) }, { "_abc", new(false, false) }, { "Now", new(true, true) } },
            typeMethods.Context.PropertyOrField);

        Assert.Equal(2, typeMethods.MethodContexts.Count);

        var dic = new Dictionary<string, ActivityTagSource>
        {
            ["abc"] = new("abc", ActivityTagFrom.Argument),
            ["def"] = new("age", ActivityTagFrom.Argument),
            ["Abc"] = new("Abc", ActivityTagFrom.StaticFieldOrProperty),
            ["_abc"] = new("_abc", ActivityTagFrom.InstanceFieldOrProperty),
        };

        var activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.First());
        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);

        dic.Remove("_abc");

        activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.ElementAt(1));
        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);
    }
}
