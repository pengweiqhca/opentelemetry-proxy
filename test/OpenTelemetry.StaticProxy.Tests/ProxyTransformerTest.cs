using OpenTelemetry.StaticProxy.Tests.StandardTest;

namespace OpenTelemetry.StaticProxy.Tests;

public class ProxyTransformerTest
{
    [Theory]
    [MemberData(nameof(TestData))]
    public async Task Test(string codeFileName)
    {
        var node = Assert.Single(ProxyTransformer
            .Rewrite(await new ProxyVisitorTest(codeFileName).VisitAsync().ConfigureAwait(false)).Select(t => t.Item2));

        var transformed = "StandardFiles\\" + codeFileName + ".transformed.cs";
#if NETFRAMEWORK
        transformed = File.ReadAllText(transformed);
#else
        transformed = await File.ReadAllTextAsync(transformed).ConfigureAwait(false);
#endif
        Assert.Equal(transformed, node.ToString());
    }

    public static TheoryData<string> TestData =>
    [
        "KeepLineNumberTestClass"
    ];
}
