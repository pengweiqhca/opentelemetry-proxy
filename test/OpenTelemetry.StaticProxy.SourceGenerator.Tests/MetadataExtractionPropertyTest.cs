using FsCheck;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Tests;

/// <summary>
/// Feature: static-proxy-source-generator, Property 1: 特性元数据提取正确性
///
/// For any valid type or method declaration with [ActivitySource], [Activity], [ActivityName],
/// [ActivityTag], or [ActivityTags] attributes, the metadata extractor should extract configuration
/// values that exactly match the attribute parameters.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5**
/// </summary>
public class MetadataExtractionPropertyTest
{
    #region Models & Generators

    public record ActivitySourceConfig(
        string? ActivitySourceName, int Kind,
        bool IncludeNonAsyncStateMachineMethod, bool SuppressInstrumentation);

    public record ActivityConfig(string? ActivityName, int Kind, bool SuppressInstrumentation);

    public record ActivityNameMethodConfig(string? ActivityName, bool AdjustStartTime);

    public record ActivityTagOnParamConfig(string ParamName, string? TagName, string? Expression);

    public static class Generators
    {
        public static Gen<string> SafeId() =>
            from f in Gen.Elements('A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P')
            from len in Gen.Choose(1, 6)
            from rest in Gen.ArrayOf(len, Gen.Elements(
                'a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t'))
            select f + new string(rest);

        public static Gen<string?> OptName() =>
            Gen.OneOf(Gen.Constant<string?>(null), SafeId().Select<string, string?>(s => s));

        public static Gen<ActivitySourceConfig> ActivitySourceCfg() =>
            from name in OptName()
            from kind in Gen.Choose(0, 4)
            from inc in Arb.Generate<bool>()
            from sup in Arb.Generate<bool>()
            select new ActivitySourceConfig(name, kind, inc, sup);

        public static Gen<ActivityConfig> ActivityCfg() =>
            from name in OptName()
            from kind in Gen.Choose(0, 4)
            from sup in Arb.Generate<bool>()
            select new ActivityConfig(name, kind, sup);

        public static Gen<ActivityNameMethodConfig> ActivityNameCfg() =>
            from name in OptName()
            from adj in Arb.Generate<bool>()
            select new ActivityNameMethodConfig(name, adj);

        public static Gen<ActivityTagOnParamConfig> ActivityTagCfg() =>
            from paramName in SafeId()
            from tagName in OptName()
            from expr in Gen.OneOf(Gen.Constant<string?>(null), SafeId().Select<string, string?>(s => "$." + s))
            select new ActivityTagOnParamConfig(paramName, tagName, expr);
    }

    public class Arbs
    {
        public static Arbitrary<ActivitySourceConfig> ArbActivitySource() =>
            Generators.ActivitySourceCfg().ToArbitrary();
        public static Arbitrary<ActivityConfig> ArbActivity() =>
            Generators.ActivityCfg().ToArbitrary();
        public static Arbitrary<ActivityNameMethodConfig> ArbActivityName() =>
            Generators.ActivityNameCfg().ToArbitrary();
        public static Arbitrary<ActivityTagOnParamConfig> ArbActivityTag() =>
            Generators.ActivityTagCfg().ToArbitrary();
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
                or "System.Linq" or "System.Threading.Tasks")
                refs.Add(MetadataReference.CreateFromFile(asm));
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(ActivitySourceAttribute).Assembly.Location));
        return refs.ToArray();
    }

    private static string RunAndGetOutput(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var comp = CSharpCompilation.Create("Test", [tree], Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var gen = new TestMetadataExtractionGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(gen);
        driver = driver.RunGeneratorsAndUpdateCompilation(comp, out _, out _);
        var result = driver.GetRunResult();

        var genResult = result.Results.FirstOrDefault();
        if (genResult.GeneratedSources.Length == 0) return "";
        return genResult.GeneratedSources[0].SourceText.ToString();
    }

    #endregion

    #region Helpers

    private static string KindLiteral(int kind) => kind switch
    {
        0 => "System.Diagnostics.ActivityKind.Internal",
        1 => "System.Diagnostics.ActivityKind.Server",
        2 => "System.Diagnostics.ActivityKind.Client",
        3 => "System.Diagnostics.ActivityKind.Producer",
        4 => "System.Diagnostics.ActivityKind.Consumer",
        _ => "System.Diagnostics.ActivityKind.Internal"
    };

    private static string ExpectedKind(int kind) =>
        kind == 0 ? "default" : $"(System.Diagnostics.ActivityKind){kind}";

    private static string BuildNamedArgs(params (string key, string val, bool include)[] args)
    {
        var parts = args.Where(a => a.include).Select(a => $"{a.key} = {a.val}");
        return string.Join(", ", parts);
    }

    #endregion

    #region Property Tests

    /// <summary>
    /// [ActivitySource] metadata extraction: ActivitySourceName, Kind,
    /// IncludeNonAsyncStateMachineMethod, SuppressInstrumentation.
    /// **Validates: Requirements 2.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property ActivitySource_Metadata_Matches_Attribute(ActivitySourceConfig cfg)
    {
        var named = BuildNamedArgs(
            ("Kind", KindLiteral(cfg.Kind), cfg.Kind != 0),
            ("IncludeNonAsyncStateMachineMethod", "true", cfg.IncludeNonAsyncStateMachineMethod),
            ("SuppressInstrumentation", "true", cfg.SuppressInstrumentation));

        var ctorPart = cfg.ActivitySourceName != null ? $"\"{cfg.ActivitySourceName}\"" : "";
        var allArgs = string.Join(", ", new[] { ctorPart, named }.Where(s => !string.IsNullOrEmpty(s)));
        var attrStr = allArgs.Length > 0 ? $"[ActivitySource({allArgs})]" : "[ActivitySource]";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;
namespace TestNs {{
    {attrStr}
    public class TestClass {{
        public async Task DoAsync() => await Task.CompletedTask;
    }}
}}";
        var output = RunAndGetOutput(source);
        var expectedName = cfg.ActivitySourceName ?? "TestNs.TestClass";

        return (output.Contains($"ActivitySourceName={expectedName}")
             && output.Contains($"Kind={ExpectedKind(cfg.Kind)}")
             && output.Contains($"IncludeNonAsync={cfg.IncludeNonAsyncStateMachineMethod}")
             && output.Contains($"SuppressInstrumentation={cfg.SuppressInstrumentation}"))
            .Label($"Output:\n{output}");
    }

    /// <summary>
    /// [Activity] metadata extraction: ActivityName, Kind, SuppressInstrumentation.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property Activity_Metadata_Matches_Attribute(ActivityConfig cfg)
    {
        var named = BuildNamedArgs(
            ("Kind", KindLiteral(cfg.Kind), cfg.Kind != 0),
            ("SuppressInstrumentation", "true", cfg.SuppressInstrumentation));

        var ctorPart = cfg.ActivityName != null ? $"\"{cfg.ActivityName}\"" : "";
        var allArgs = string.Join(", ", new[] { ctorPart, named }.Where(s => !string.IsNullOrEmpty(s)));
        var attrStr = allArgs.Length > 0 ? $"[Activity({allArgs})]" : "[Activity]";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;
namespace TestNs {{
    public class TestClass {{
        {attrStr}
        public async Task DoAsync() => await Task.CompletedTask;
    }}
}}";
        var output = RunAndGetOutput(source);
        var expectedName = cfg.ActivityName ?? "TestClass.DoAsync";

        return (output.Contains($"ACTIVITY_METHOD: ActivityName={expectedName}")
             && output.Contains($"ACTIVITY_METHOD: Kind={ExpectedKind(cfg.Kind)}")
             && output.Contains($"ACTIVITY_METHOD: SuppressInstrumentation={cfg.SuppressInstrumentation}"))
            .Label($"Output:\n{output}");
    }

    /// <summary>
    /// [ActivityName] on method: ActivityName, AdjustStartTime.
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property ActivityName_Method_Metadata_Matches_Attribute(ActivityNameMethodConfig cfg)
    {
        var named = cfg.AdjustStartTime ? "AdjustStartTime = true" : "";
        var ctorPart = cfg.ActivityName != null ? $"\"{cfg.ActivityName}\"" : "";
        var allArgs = string.Join(", ", new[] { ctorPart, named }.Where(s => !string.IsNullOrEmpty(s)));
        var attrStr = allArgs.Length > 0 ? $"[ActivityName({allArgs})]" : "[ActivityName]";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;
namespace TestNs {{
    public class TestClass {{
        {attrStr}
        public async Task DoAsync() => await Task.CompletedTask;
    }}
}}";
        var output = RunAndGetOutput(source);
        var expectedName = cfg.ActivityName ?? "TestClass.DoAsync";

        return (output.Contains($"ACTIVITYNAME_METHOD: ActivityName={expectedName}")
             && output.Contains($"ACTIVITYNAME_METHOD: AdjustStartTime={cfg.AdjustStartTime}"))
            .Label($"Output:\n{output}");
    }

    /// <summary>
    /// [ActivityTag] on parameter: TagName and Expression extraction.
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property ActivityTag_OnParam_Metadata_Matches_Attribute(ActivityTagOnParamConfig cfg)
    {
        var tagArgs = new List<string>();
        if (cfg.TagName != null) tagArgs.Add($"\"{cfg.TagName}\"");
        if (cfg.Expression != null) tagArgs.Add($"Expression = \"{cfg.Expression}\"");
        var tagAttr = tagArgs.Count > 0
            ? $"[ActivityTag({string.Join(", ", tagArgs)})]"
            : "[ActivityTag]";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;
namespace TestNs {{
    public class TestClass {{
        [Activity]
        public async Task DoAsync({tagAttr} int {cfg.ParamName}) => await Task.CompletedTask;
    }}
}}";
        var output = RunAndGetOutput(source);
        var expectedTagName = cfg.TagName ?? cfg.ParamName;
        var expectedExpr = cfg.Expression ?? "";

        return (output.Contains($"ACTIVITY_METHOD_INTAG: TagName={expectedTagName} SourceName={cfg.ParamName}")
             && output.Contains($"Expression={expectedExpr}"))
            .Label($"Output:\n{output}");
    }

    /// <summary>
    /// [ActivityTags] on type: Tag names extracted correctly.
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property ActivityTags_OnType_Metadata_Extracted(bool useTwoTags)
    {
        // Use fixed tag names to avoid identifier collision issues
        var tag1 = "delay";
        var tag2 = "count";
        var tagsArg = useTwoTags ? $"\"{tag1}\", \"{tag2}\"" : $"\"{tag1}\"";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;
namespace TestNs {{
    [ActivitySource]
    [ActivityTags({tagsArg})]
    public class TestClass {{
        public async Task DoAsync() => await Task.CompletedTask;
    }}
}}";
        var output = RunAndGetOutput(source);

        var hasTag1 = output.Contains($"TYPE_TAG: TagName={tag1}");
        var hasTag2 = !useTwoTags || output.Contains($"TYPE_TAG: TagName={tag2}");

        return (hasTag1 && hasTag2).Label($"Output:\n{output}");
    }

    #endregion
}
