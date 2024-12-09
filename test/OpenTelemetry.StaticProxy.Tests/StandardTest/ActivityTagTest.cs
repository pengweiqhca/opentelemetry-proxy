namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

public class ActivityTagTest
{
    [Fact]
    public async Task ActivityTagsTest()
    {
        var test = new ProxyVisitorTest("ActivityTagTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var context = Assert.IsAssignableFrom<MethodActivityNameContext>(
            Assert.Single(Assert.Single(results).MethodContexts.Values));

        Assert.Equal(["test"], context.UnknownTag.Select(x => x.Value));

        Assert.Equal(new()
        {
            [new("abc")] = new("abc", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("def")] = new("age", ActivityTagFrom.ArgumentOrLocalVariable),
        }, context.InTags);
    }

    [Fact]
    public async Task InoutTagsTest()
    {
        var test = new ProxyVisitorTest("ActivityTagTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal(2, typeMethods.MethodContexts.Count);

        var dic = new Dictionary<ActivityTag, ActivityTagSource>
        {
            [new("a2")] = new("a", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("b")] = new("b", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("d")] = new("d", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("e")] = new("e", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("Now")] = new("Now", ActivityTagFrom.InstanceFieldOrProperty),
            [new("_now")] = new("_now", ActivityTagFrom.InstanceFieldOrProperty),
        };

        var activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.First());

        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Equal(new()
        {
            [new("c")] = new("c", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("d")] = new("d", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("ghi")] = ActivityTagSource.ReturnValue,
        }, activityTag.OutTags);

        dic.Remove(new("_now"));
        dic.Remove(new("Now"));

        activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.ElementAt(1));

        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Equal(new()
        {
            [new("c")] = new("c", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("d")] = new("d", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("$returnvalue")] = ActivityTagSource.ReturnValue,
        }, activityTag.OutTags);
    }

    [Fact]
    public async Task TypeHaveTagsTest()
    {
        var test = new ProxyVisitorTest("ActivityTagTestClass3");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal(new()
        {
            { "Abc", new(true, false) },
            { "_abc", new(false, false) },
            { "Now", new(true, true) }
        }, typeMethods.Context.PropertyOrField);

        Assert.Equal(["_abc", "Abc"], typeMethods.Context.Tags.Select(x => x.Value));

        Assert.Equal(2, typeMethods.MethodContexts.Count);

        var dic = new Dictionary<ActivityTag, ActivityTagSource>
        {
            [new("abc")] = new("abc", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("age")] = new("age", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("Abc")] = new("Abc", ActivityTagFrom.StaticFieldOrProperty),
            [new("_abc")] = new("_abc", ActivityTagFrom.InstanceFieldOrProperty),
        };

        var activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.First());
        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);

        dic.Remove(new("_abc"));

        activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.ElementAt(1));

        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);
    }

    [Fact]
    public async Task TypeNoTagsTest()
    {
        var test = new ProxyVisitorTest("ActivityTagTestClass4");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal(new() { { "Abc", new(true, false) }, { "_abc", new(false, false) }, { "Now", new(true, true) } },
            typeMethods.Context.PropertyOrField);

        Assert.Equal(2, typeMethods.MethodContexts.Count);

        var dic = new Dictionary<ActivityTag, ActivityTagSource>
        {
            [new("abc")] = new("abc", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("def")] = new("age", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("Abc")] = new("Abc", ActivityTagFrom.StaticFieldOrProperty),
            [new("_abc")] = new("_abc", ActivityTagFrom.InstanceFieldOrProperty),
        };

        var activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.First());
        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);

        dic.Remove(new("_abc"));

        activityTag = Assert.IsAssignableFrom<ActivityContext>(typeMethods.MethodContexts.Values.ElementAt(1));
        Assert.Equal(dic, activityTag.InTags);
        Assert.Empty(activityTag.UnknownTag);
        Assert.Empty(activityTag.OutTags);
    }

    [Fact]
    public async Task ExpressionTest()
    {
        var test = new ProxyVisitorTest("ActivityTagTestClass5");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var context = Assert.IsAssignableFrom<ActivityContext>(
            Assert.Single(Assert.Single(results).MethodContexts.Values));

        Assert.Empty(context.UnknownTag);

        Assert.Equal(new()
        {
            [new("abc.TotalSeconds", "$.TotalSeconds")] = new("abc", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("str")] = new("str", ActivityTagFrom.ArgumentOrLocalVariable),
            [new("def", "$?.Length")] = new("str", ActivityTagFrom.ArgumentOrLocalVariable),
        }, context.InTags);

        Assert.Equal(new()
        {
            [new("$returnvalue.Second", "$.Second")] = ActivityTagSource.ReturnValue,
            [new("$returnvalue.Day", "$.Day")] = ActivityTagSource.ReturnValue,
            [new("ret1", "$.Hour")] = ActivityTagSource.ReturnValue,
            [new("ret2", "$.Minute")] = ActivityTagSource.ReturnValue,
        }, context.OutTags);
    }
}
