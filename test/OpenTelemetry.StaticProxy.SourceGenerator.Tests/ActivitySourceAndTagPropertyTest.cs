using FsCheck;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Tests;

/// <summary>
/// Feature: static-proxy-source-generator
/// Property 7: ActivitySource 字段生成正确性
/// Property 8: ActivitySource 去重
/// Property 9: Tag 来源解析正确性
///
/// These tests use the REAL ProxySourceGenerator to verify ActivitySource field generation,
/// deduplication, and tag expression generation.
/// </summary>
public class ActivitySourceAndTagPropertyTest
{
    #region Models & Generators

    public record ActivitySourceConfig(string ActivitySourceName);

    public record DedupConfig(bool UseSameName);

    public enum TagSourceKind
    {
        Parameter,
        InstanceField,
        InstanceProperty,
        StaticField,
        StaticProperty
    }

    public record TagSourceConfig(TagSourceKind Kind, string MemberName, string? Expression);

    public static class Generators
    {
        private static readonly char[] Letters = "ABCDEFGHIJKLMNOP".ToCharArray();
        private static readonly char[] LowerLetters = "abcdefghijklmnopqrst".ToCharArray();

        public static Gen<string> SafeId() =>
            from f in Gen.Elements(Letters)
            from len in Gen.Choose(2, 5)
            from rest in Gen.ArrayOf(len, Gen.Elements(LowerLetters))
            select f + new string(rest);

        public static Gen<ActivitySourceConfig> ActivitySourceCfg() =>
            from name in SafeId()
            select new ActivitySourceConfig(name);

        public static Gen<DedupConfig> DedupCfg() =>
            from same in Arb.Generate<bool>()
            select new DedupConfig(same);

        public static Gen<TagSourceConfig> TagSourceCfg() =>
            Gen.OneOf(
                // Parameter with optional Expression (only [ActivityTag] supports Expression)
                from memberName in SafeId()
                from hasExpr in Arb.Generate<bool>()
                let expression = hasExpr ? "$.Length" : null
                select new TagSourceConfig(TagSourceKind.Parameter, memberName, expression),
                // Instance field (no Expression - [ActivityTags] encodes it in tag name)
                from memberName in SafeId()
                select new TagSourceConfig(TagSourceKind.InstanceField, memberName, null),
                // Instance property
                from memberName in SafeId()
                select new TagSourceConfig(TagSourceKind.InstanceProperty, memberName, null),
                // Static field
                from memberName in SafeId()
                select new TagSourceConfig(TagSourceKind.StaticField, memberName, null),
                // Static property
                from memberName in SafeId()
                select new TagSourceConfig(TagSourceKind.StaticProperty, memberName, null));
    }

    public class Arbs
    {
        public static Arbitrary<ActivitySourceConfig> ArbActivitySource() =>
            Generators.ActivitySourceCfg().ToArbitrary();
        public static Arbitrary<DedupConfig> ArbDedup() =>
            Generators.DedupCfg().ToArbitrary();
        public static Arbitrary<TagSourceConfig> ArbTagSource() =>
            Generators.TagSourceCfg().ToArbitrary();
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

    #region Property 7: ActivitySource 字段生成正确性

    /// <summary>
    /// Property 7: ActivitySource field generation correctness.
    ///
    /// For any type annotated with [ActivitySource], the generated code should contain
    /// a static readonly ActivitySource field with the correct constructor parameters:
    /// the ActivitySourceName and typeof(T).Assembly.GetName().Version?.ToString().
    ///
    /// **Validates: Requirements 6.2, 6.3, 6.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property ActivitySource_Field_Has_Correct_Constructor_Parameters(ActivitySourceConfig cfg)
    {
        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource(""{cfg.ActivitySourceName}"")]
    public class MyService {{
        [Activity]
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

        // Verify the ActivitySource field is created with the correct name
        var hasNewWithName = output.Contains($"new(\"{cfg.ActivitySourceName}\"");

        // Verify the typeof expression for version
        var hasTypeofVersion = output.Contains("typeof(global::TestNs.MyService).Assembly.GetName().Version?.ToString()");

        // Verify it's a static readonly field
        var hasStaticReadonly = output.Contains("static readonly global::System.Diagnostics.ActivitySource");

        var allCorrect = hasNewWithName && hasTypeofVersion && hasStaticReadonly;

        return allCorrect.Label(
            $"hasNewWithName={hasNewWithName}, hasTypeofVersion={hasTypeofVersion}, " +
            $"hasStaticReadonly={hasStaticReadonly}\n" +
            $"Config: ActivitySourceName={cfg.ActivitySourceName}\n" +
            $"Output:\n{output}");
    }

    #endregion

    #region Property 8: ActivitySource 去重

    /// <summary>
    /// Property 8: ActivitySource deduplication.
    ///
    /// When multiple types share the same ActivitySourceName, only one ActivitySource
    /// field should be generated per unique name.
    ///
    /// **Validates: Requirements 6.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property ActivitySource_Deduplication_By_Name(DedupConfig cfg)
    {
        // When UseSameName=true, both classes use "SharedName"; otherwise different names
        var name1 = "SharedName";
        var name2 = cfg.UseSameName ? "SharedName" : "OtherName";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource(""{name1}"")]
    public class ServiceA {{
        [Activity]
        public async Task DoWorkA(int param1) {{
            await System.Threading.Tasks.Task.CompletedTask;
        }}
    }}

    [ActivitySource(""{name2}"")]
    public class ServiceB {{
        [Activity]
        public async Task DoWorkB(int param1) {{
            await System.Threading.Tasks.Task.CompletedTask;
        }}
    }}

    public class Caller {{
        public void Call() {{
            var a = new ServiceA();
            a.DoWorkA(0);
            var b = new ServiceB();
            b.DoWorkB(0);
        }}
    }}
}}";

        var output = RunRealGenerator(source);

        // Count how many ActivitySource field declarations exist
        var fieldCount = CountOccurrences(output, "internal static readonly global::System.Diagnostics.ActivitySource");

        bool correct;
        if (cfg.UseSameName)
        {
            // Same name → only 1 ActivitySource field
            correct = fieldCount == 1;
        }
        else
        {
            // Different names → 2 ActivitySource fields
            correct = fieldCount == 2;
        }

        return correct.Label(
            $"UseSameName={cfg.UseSameName}, name1={name1}, name2={name2}, " +
            $"fieldCount={fieldCount}\n" +
            $"Output:\n{output}");
    }

    #endregion

    #region Property 9: Tag 来源解析正确性

    /// <summary>
    /// Property 9: Tag source resolution correctness.
    ///
    /// For any tag configuration, the tag value expression should be correctly generated:
    /// - Parameter → paramName (with [ActivityTag] Expression appended if present)
    /// - Instance field/property → @this.MemberName
    /// - Static field/property → TypeName.MemberName
    /// - Return value → captured to local variable (@return)
    /// - [ActivityTags] with expression → expression is part of tag name, value is base member
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property Tag_Source_Expression_Is_Correct(TagSourceConfig cfg)
    {
        var source = BuildTagSourceTestSource(cfg);
        var output = RunRealGenerator(source);

        var (expectedTagName, expectedValueExpr) = GetExpectedTagParts(cfg);
        var hasExpectedValue = output.Contains(expectedValueExpr);
        var hasExpectedTagName = output.Contains($"SetTag(\"{expectedTagName}\"");

        var correct = hasExpectedValue && hasExpectedTagName;

        return correct.Label(
            $"Kind={cfg.Kind}, MemberName={cfg.MemberName}, Expression={cfg.Expression}\n" +
            $"ExpectedTagName=\"{expectedTagName}\", foundTagName={hasExpectedTagName}\n" +
            $"ExpectedValueExpr=\"{expectedValueExpr}\", foundValue={hasExpectedValue}\n" +
            $"Output:\n{output}");
    }

    private static string BuildTagSourceTestSource(TagSourceConfig cfg)
    {
        switch (cfg.Kind)
        {
            case TagSourceKind.Parameter:
            {
                // Tag from parameter: use [ActivityTag] on parameter with optional Expression
                var exprAttr = cfg.Expression != null
                    ? $"(Expression = \"{cfg.Expression}\")"
                    : "";
                return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyService {{
        [Activity]
        public async Task DoWork([ActivityTag{exprAttr}] string {cfg.MemberName}) {{
            await System.Threading.Tasks.Task.CompletedTask;
        }}
    }}

    public class Caller {{
        public void Call() {{
            var svc = new MyService();
            svc.DoWork(""test"");
        }}
    }}
}}";
            }

            case TagSourceKind.InstanceField:
            {
                // Tag from instance field: use [ActivityTags] mapping to field
                return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    [ActivityTags(""{cfg.MemberName}"")]
    public class MyService {{
        public string {cfg.MemberName} = ""value"";

        [Activity]
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
            }

            case TagSourceKind.InstanceProperty:
            {
                return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    [ActivityTags(""{cfg.MemberName}"")]
    public class MyService {{
        public string {cfg.MemberName} {{ get; set; }} = ""value"";

        [Activity]
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
            }

            case TagSourceKind.StaticField:
            {
                return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    [ActivityTags(""{cfg.MemberName}"")]
    public class MyService {{
        public static string {cfg.MemberName} = ""value"";

        [Activity]
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
            }

            case TagSourceKind.StaticProperty:
            {
                return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    [ActivityTags(""{cfg.MemberName}"")]
    public class MyService {{
        public static string {cfg.MemberName} {{ get; set; }} = ""value"";

        [Activity]
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
            }

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>
    /// Returns (expectedTagName, expectedValueExpression) for the given config.
    /// For [ActivityTag] with Expression on parameters, the expression is appended to the value.
    /// For [ActivityTags] on fields/properties, the tag name is the member name and value is the base member access.
    /// </summary>
    private static (string TagName, string ValueExpr) GetExpectedTagParts(TagSourceConfig cfg)
    {
        switch (cfg.Kind)
        {
            case TagSourceKind.Parameter:
            {
                var tagName = cfg.MemberName;
                var valueExpr = cfg.MemberName;
                // [ActivityTag(Expression = "$.Length")] appends to value
                if (cfg.Expression != null && cfg.Expression.StartsWith("$"))
                    valueExpr = cfg.MemberName + cfg.Expression[1..];
                return (tagName, valueExpr);
            }

            case TagSourceKind.InstanceField:
            case TagSourceKind.InstanceProperty:
                return (cfg.MemberName, $"@this.{cfg.MemberName}");

            case TagSourceKind.StaticField:
            case TagSourceKind.StaticProperty:
                return (cfg.MemberName, $"global::TestNs.MyService.{cfg.MemberName}");

            default:
                return (cfg.MemberName, cfg.MemberName);
        }
    }

    #endregion

    #region Helpers

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }

    #endregion
}
