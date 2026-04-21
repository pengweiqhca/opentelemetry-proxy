using FsCheck;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OpenTelemetry.Proxy.Tests;

/// <summary>
/// Feature: static-proxy-source-generator
/// Property 5: ActivityName 模式拦截代码结构完整性
/// Property 6: SuppressInstrumentation 模式拦截代码结构
///
/// These tests use the REAL ProxySourceGenerator to verify that generated interceptor
/// code contains the expected structural elements for ActivityName and SuppressInstrumentation modes.
/// </summary>
public class ActivityNameAndSuppressPropertyTest
{
    #region Models & Generators

    public record ActivityNameConfig(string Name, bool AdjustStartTime);

    public record SuppressInstrumentationModeConfig(bool SuppressInstrumentation);

    public static class Generators
    {
        private static readonly char[] Letters = "ABCDEFGHIJKLMNOP".ToCharArray();
        private static readonly char[] LowerLetters = "abcdefghijklmnopqrst".ToCharArray();

        public static Gen<string> SafeId() =>
            from f in Gen.Elements(Letters)
            from len in Gen.Choose(2, 5)
            from rest in Gen.ArrayOf(len, Gen.Elements(LowerLetters))
            select f + new string(rest);

        public static Gen<ActivityNameConfig> ActivityNameCfg() =>
            from name in SafeId()
            from adjust in Arb.Generate<bool>()
            select new ActivityNameConfig(name, adjust);

        public static Gen<SuppressInstrumentationModeConfig> SuppressModeConfig() =>
            from suppress in Arb.Generate<bool>()
            select new SuppressInstrumentationModeConfig(suppress);
    }

    public class Arbs
    {
        public static Arbitrary<ActivityNameConfig> ArbActivityName() =>
            Generators.ActivityNameCfg().ToArbitrary();

        public static Arbitrary<SuppressInstrumentationModeConfig> ArbSuppressMode() =>
            Generators.SuppressModeConfig().ToArbitrary();
    }

    #endregion

    #region Compilation Helpers

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
                or "System.Threading" or "System.Runtime.CompilerServices.Unsafe")
                refs.Add(MetadataReference.CreateFromFile(asm));
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(ActivitySourceAttribute).Assembly.Location));
        return refs.ToArray();
    }

    /// <summary>
    /// Runs the REAL ProxySourceGenerator and returns all generated source texts concatenated.
    /// </summary>
    private static string RunRealGenerator(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var comp = CSharpCompilation.Create("TestAssembly", [tree], Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var gen = new ProxySourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(gen);
        driver = driver.RunGeneratorsAndUpdateCompilation(comp, out _, out _);
        var result = driver.GetRunResult();

        var allOutput = new System.Text.StringBuilder();
        foreach (var genResult in result.Results)
            foreach (var genSource in genResult.GeneratedSources)
                allOutput.AppendLine(genSource.SourceText.ToString());

        return allOutput.ToString();
    }

    #endregion

    #region Property 5: ActivityName 模式拦截代码结构完整性

    /// <summary>
    /// Property 5: ActivityName mode interceptor code structural completeness.
    ///
    /// For any method annotated with [ActivityName], the generated interceptor should contain:
    /// (a) InnerActivityAccessor.SetActivityContext() call
    /// (b) Correct Name value matching the configured ActivityName
    /// (c) Correct AdjustStartTime value matching the configuration
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property ActivityName_Mode_Code_Structure_Is_Complete(ActivityNameConfig cfg)
    {
        var adjustArg = cfg.AdjustStartTime ? ", AdjustStartTime = true" : "";
        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyService {{
        [ActivityName(""{cfg.Name}""{adjustArg})]
        public async Task DoWork(int param1) {{
            await System.Threading.Tasks.Task.CompletedTask;
        }}
    }}

    public class Caller {{
        public void Call() {{
            var svc = new MyService();
            svc.DoWork(0);
        }}
    }}
}}";

        var output = RunRealGenerator(source);

        var hasSetActivityContext = output.Contains("SetActivityContext(");
        var hasName = output.Contains($"Name = \"{cfg.Name}\"");
        var hasAdjustStartTime = output.Contains($"AdjustStartTime = {(cfg.AdjustStartTime ? "true" : "false")}");
        var hasUsing = output.Contains("using (");

        var allPresent = hasSetActivityContext && hasName && hasAdjustStartTime && hasUsing;

        return allPresent.Label(
            $"SetActivityContext={hasSetActivityContext}, Name={hasName}, " +
            $"AdjustStartTime={hasAdjustStartTime}, using={hasUsing}\n" +
            $"Config: Name={cfg.Name}, AdjustStartTime={cfg.AdjustStartTime}\n" +
            $"Output:\n{output}");
    }

    #endregion

    #region Property 6: SuppressInstrumentation 模式拦截代码结构

    /// <summary>
    /// Property 6: SuppressInstrumentation mode interceptor code structure.
    ///
    /// When [NonActivity(true)] is applied, the generated interceptor should contain
    /// using(SuppressInstrumentationScope.Begin()) wrapping the original method call.
    ///
    /// Uses a bool toggle to generate variety in test inputs (different param names),
    /// but always tests [NonActivity(true)] since that is the mode that generates code.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property SuppressInstrumentation_Mode_Code_Structure(SuppressInstrumentationModeConfig cfg)
    {
        // Use the bool to vary the parameter name for input diversity
        var paramName = cfg.SuppressInstrumentation ? "value" : "param1";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyService {{
        [NonActivity(true)]
        public async Task DoWork(int {paramName}) {{
            await System.Threading.Tasks.Task.CompletedTask;
        }}
    }}

    public class Caller {{
        public void Call() {{
            var svc = new MyService();
            svc.DoWork(0);
        }}
    }}
}}";

        var output = RunRealGenerator(source);

        var hasSuppressBegin = output.Contains("SuppressInstrumentationScope.Begin()");
        var hasUsing = output.Contains("using (");
        var hasOriginalCall = output.Contains($"@this.DoWork({paramName})");

        // [NonActivity(true)] should always generate SuppressInstrumentationScope wrapper
        var correct = hasSuppressBegin && hasUsing && hasOriginalCall;

        return correct.Label(
            $"hasSuppressBegin={hasSuppressBegin}, hasUsing={hasUsing}, " +
            $"hasOriginalCall={hasOriginalCall}, paramName={paramName}\n" +
            $"Output:\n{output}");
    }

    #endregion
}
