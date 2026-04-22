using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using Xunit;

namespace OpenTelemetry.Proxy.Tests;

public class DisableProxyGeneratorTest
{
    private static readonly Lazy<MetadataReference[]> Refs = new(CreateRefs);

    private static MetadataReference[] CreateRefs()
    {
        var refs = new List<MetadataReference>();
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? [];

        foreach (var asm in trusted)
        {
            var name = Path.GetFileNameWithoutExtension(asm);
            if (name is "System.Runtime" or "System.Collections" or "netstandard"
                or "mscorlib" or "System.Private.CoreLib"
                or "System.Diagnostics.DiagnosticSource"
                or "System.Linq" or "System.Threading.Tasks"
                or "System.Threading")
                refs.Add(MetadataReference.CreateFromFile(asm));
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(ActivitySourceAttribute).Assembly.Location));

        return refs.ToArray();
    }

    private const string TestSource = """
        using OpenTelemetry.Proxy;
        using System.Threading.Tasks;

        namespace TestNs {
            [ActivitySource]
            public class MyService {
                [Activity]
                public async Task DoWork(int param) => await Task.CompletedTask;
            }

            public class Caller {
                public void Call() {
                    var svc = new MyService();
                    svc.DoWork(42);
                }
            }
        }
        """;

    private static string RunGenerator(string source, bool disable)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var comp = CSharpCompilation.Create("TestAssembly", [tree], Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var gen = new ProxySourceGenerator();
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(disable);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [gen.AsSourceGenerator()],
            optionsProvider: optionsProvider);

        driver = driver.RunGeneratorsAndUpdateCompilation(comp, out _, out _);
        var result = driver.GetRunResult();

        var sb = new System.Text.StringBuilder();

        foreach (var genResult in result.Results)
            foreach (var genSource in genResult.GeneratedSources)
                sb.AppendLine(genSource.SourceText.ToString());

        return sb.ToString();
    }

    [Fact]
    public void When_Disabled_No_Interceptors_Generated()
    {
        var output = RunGenerator(TestSource, disable: true);

        Assert.DoesNotContain("InterceptsLocation", output);
        Assert.DoesNotContain("StartActivity", output);
        Assert.DoesNotContain("ActivitySourceHolder", output);
    }

    [Fact]
    public void When_Enabled_Interceptors_Generated()
    {
        var output = RunGenerator(TestSource, disable: false);

        Assert.Contains("InterceptsLocation", output);
        Assert.Contains("StartActivity", output);
        Assert.Contains("ActivitySourceHolder", output);
    }

    /// <summary>
    /// Provides build_property.DisableProxyGenerator to the Source Generator.
    /// </summary>
    private sealed class TestAnalyzerConfigOptionsProvider(bool disable) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestGlobalOptions(disable);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions.Instance;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions.Instance;

        private sealed class TestGlobalOptions(bool disable) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.DisableProxyGenerator")
                {
                    value = disable ? "true" : "false";

                    return true;
                }

                value = "";

                return false;
            }
        }

        private sealed class EmptyOptions : AnalyzerConfigOptions
        {
            public static EmptyOptions Instance { get; } = new();

            public override bool TryGetValue(string key, out string value)
            {
                value = "";

                return false;
            }
        }
    }
}
