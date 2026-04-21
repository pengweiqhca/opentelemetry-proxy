using FsCheck;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OpenTelemetry.Proxy.Tests;

/// <summary>
/// Feature: static-proxy-source-generator
/// Property 2: Activity 模式拦截代码结构完整性
/// Property 3: Tag 设置代码生成正确性
/// Property 4: SuppressInstrumentation 条件生成
///
/// These tests use the REAL ProxySourceGenerator to verify that generated interceptor
/// code contains the expected structural elements for Activity mode interception.
/// </summary>
public class ActivityModePropertyTest
{
    #region Models & Generators

    public record MethodSignatureConfig(string ReturnType, bool IsAsync, string ParamType, string ParamName);

    public record TagConfig(string ParamName, string? TagName, string ParamType);

    public record SuppressConfig(bool SuppressInstrumentation);

    public static class Generators
    {
        private static readonly string[] SyncReturnTypes = ["int", "string", "bool", "double"];
        private static readonly string[] AsyncReturnTypes = ["Task<int>", "Task<string>", "Task<bool>"];
        private static readonly string[] VoidAsyncReturnTypes = ["Task"];
        private static readonly string[] ParamTypes = ["int", "string", "bool", "double", "long"];

        private static readonly char[] Letters = "ABCDEFGHIJKLMNOP".ToCharArray();
        private static readonly char[] LowerLetters = "abcdefghijklmnopqrst".ToCharArray();

        public static Gen<string> SafeId() =>
            from f in Gen.Elements(Letters)
            from len in Gen.Choose(2, 5)
            from rest in Gen.ArrayOf(len, Gen.Elements(LowerLetters))
            select f + new string(rest);

        public static Gen<MethodSignatureConfig> MethodSigCfg() =>
            Gen.OneOf(
                // async with typed return
                from ret in Gen.Elements(AsyncReturnTypes)
                from pt in Gen.Elements(ParamTypes)
                from pn in SafeId()
                select new MethodSignatureConfig(ret, true, pt, pn),
                // async void (Task)
                from pt in Gen.Elements(ParamTypes)
                from pn in SafeId()
                select new MethodSignatureConfig("Task", true, pt, pn),
                // sync with typed return
                from ret in Gen.Elements(SyncReturnTypes)
                from pt in Gen.Elements(ParamTypes)
                from pn in SafeId()
                select new MethodSignatureConfig(ret, false, pt, pn));

        public static Gen<TagConfig> TagCfg() =>
            from pn in SafeId()
            from tagName in Gen.OneOf(Gen.Constant<string?>(null), SafeId().Select<string, string?>(s => s))
            from pt in Gen.Elements(ParamTypes)
            select new TagConfig(pn, tagName, pt);

        public static Gen<SuppressConfig> SuppressCfg() =>
            from sup in Arb.Generate<bool>()
            select new SuppressConfig(sup);
    }

    public class Arbs
    {
        public static Arbitrary<MethodSignatureConfig> ArbMethodSig() =>
            Generators.MethodSigCfg().ToArbitrary();
        public static Arbitrary<TagConfig> ArbTag() =>
            Generators.TagCfg().ToArbitrary();
        public static Arbitrary<SuppressConfig> ArbSuppress() =>
            Generators.SuppressCfg().ToArbitrary();
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

    #region Source Builders

    private static string GetDefaultValue(string paramType) => paramType switch
    {
        "string" => "\"test\"",
        "bool" => "true",
        "double" => "1.0",
        "long" => "1L",
        _ => "0"
    };

    private static string GetReturnExpression(string returnType, bool isAsync) => returnType switch
    {
        "Task" => "await System.Threading.Tasks.Task.CompletedTask",
        "Task<int>" => "await System.Threading.Tasks.Task.FromResult(0)",
        "Task<string>" => "await System.Threading.Tasks.Task.FromResult(\"x\")",
        "Task<bool>" => "await System.Threading.Tasks.Task.FromResult(true)",
        "int" => "return 0",
        "string" => "return \"x\"",
        "bool" => "return true",
        "double" => "return 1.0",
        "long" => "return 1L",
        _ => "return default"
    };

    private static string BuildActivityMethodSource(MethodSignatureConfig cfg, string activityAttr = "[Activity]")
    {
        var asyncKw = cfg.IsAsync ? "async " : "";
        var body = cfg.ReturnType == "Task"
            ? "{ await System.Threading.Tasks.Task.CompletedTask; }"
            : $"{{ {GetReturnExpression(cfg.ReturnType, cfg.IsAsync)}; }}";

        return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyService {{
        {activityAttr}
        public {asyncKw}{cfg.ReturnType} DoWork({cfg.ParamType} {cfg.ParamName}) {body}
    }}

    public class Caller {{
        public void Call() {{
            var svc = new MyService();
            svc.DoWork({GetDefaultValue(cfg.ParamType)});
        }}
    }}
}}";
    }

    #endregion

    #region Property 2: Activity 模式拦截代码结构完整性

    /// <summary>
    /// Property 2: Activity mode interceptor code structural completeness.
    ///
    /// For any method annotated with [Activity], the generated interceptor should contain:
    /// (a) ActivitySource.StartActivity() call
    /// (b) try-catch-finally block
    /// (c) SetExceptionStatus() in catch
    /// (d) activity?.Dispose() in finally
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.4, 3.5, 3.6, 3.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property Activity_Mode_Code_Structure_Is_Complete(MethodSignatureConfig cfg)
    {
        var source = BuildActivityMethodSource(cfg);
        var output = RunRealGenerator(source);

        var hasStartActivity = output.Contains("StartActivity(");
        var hasTry = output.Contains("try");
        var hasCatch = output.Contains("catch");
        var hasFinally = output.Contains("finally");
        var hasSetExceptionStatus = output.Contains("SetExceptionStatus(");
        var hasDispose = output.Contains("activity?.Dispose()");
        var hasThrow = output.Contains("throw;");

        var allPresent = hasStartActivity && hasTry && hasCatch && hasFinally
                         && hasSetExceptionStatus && hasDispose && hasThrow;

        return allPresent.Label(
            $"StartActivity={hasStartActivity}, try={hasTry}, catch={hasCatch}, " +
            $"finally={hasFinally}, SetExceptionStatus={hasSetExceptionStatus}, " +
            $"Dispose={hasDispose}, throw={hasThrow}\n" +
            $"Config: ReturnType={cfg.ReturnType}, IsAsync={cfg.IsAsync}, " +
            $"ParamType={cfg.ParamType}, ParamName={cfg.ParamName}\n" +
            $"Output:\n{output}");
    }

    #endregion

    #region Property 3: Tag 设置代码生成正确性

    /// <summary>
    /// Property 3: Tag setting code generation correctness.
    ///
    /// For any Activity mode method with InTags (via [ActivityTag] on parameters),
    /// the generated code should contain the correct number of SetTag calls with
    /// correct tag names.
    ///
    /// **Validates: Requirements 3.3, 3.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property Tag_SetTag_Calls_Match_Configuration(TagConfig cfg)
    {
        var expectedTagName = cfg.TagName ?? cfg.ParamName;

        var tagArgs = cfg.TagName != null ? $"(\"{cfg.TagName}\")" : "";
        var tagAttr = $"[ActivityTag{tagArgs}]";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyService {{
        [Activity]
        public async Task DoWork({tagAttr} {cfg.ParamType} {cfg.ParamName}) {{
            await System.Threading.Tasks.Task.CompletedTask;
        }}
    }}

    public class Caller {{
        public void Call() {{
            var svc = new MyService();
            svc.DoWork({GetDefaultValue(cfg.ParamType)});
        }}
    }}
}}";

        var output = RunRealGenerator(source);

        // Verify SetTag call exists with the expected tag name
        var expectedSetTag = $"SetTag(\"{expectedTagName}\"";
        var hasSetTag = output.Contains(expectedSetTag);

        // Count SetTag calls in the output (should have at least 1 for the InTag)
        var setTagCount = CountOccurrences(output, "SetTag(");

        return (hasSetTag && setTagCount >= 1).Label(
            $"ExpectedTag=\"{expectedTagName}\", hasSetTag={hasSetTag}, setTagCount={setTagCount}\n" +
            $"Config: ParamName={cfg.ParamName}, TagName={cfg.TagName}, ParamType={cfg.ParamType}\n" +
            $"Output:\n{output}");
    }

    /// <summary>
    /// Property 3 (extended): OutTag via [return: ActivityTag] generates SetTag after method call.
    ///
    /// **Validates: Requirements 3.3, 3.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property OutTag_ReturnValue_Generates_SetTag(TagConfig cfg)
    {
        var expectedTagName = cfg.TagName ?? "$returnvalue";

        var tagArgs = cfg.TagName != null ? $"(\"{cfg.TagName}\")" : "";
        var returnTagAttr = $"[return: ActivityTag{tagArgs}]";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyService {{
        [Activity]
        {returnTagAttr}
        public int DoWork(int param1) {{
            return 42;
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

        var expectedSetTag = $"SetTag(\"{expectedTagName}\"";
        var hasOutTag = output.Contains(expectedSetTag);

        return hasOutTag.Label(
            $"ExpectedOutTag=\"{expectedTagName}\", hasOutTag={hasOutTag}\n" +
            $"Config: TagName={cfg.TagName}\n" +
            $"Output:\n{output}");
    }

    #endregion

    #region Property 4: SuppressInstrumentation 条件生成

    /// <summary>
    /// Property 4: SuppressInstrumentation conditional generation.
    ///
    /// When SuppressInstrumentation=true, the generated code should contain
    /// SuppressInstrumentationScope.Begin() and disposable?.Dispose().
    /// When SuppressInstrumentation=false, these should be absent.
    ///
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property SuppressInstrumentation_Conditional_Generation(SuppressConfig cfg)
    {
        var suppressArg = cfg.SuppressInstrumentation ? "SuppressInstrumentation = true" : "";
        var activityAttr = string.IsNullOrEmpty(suppressArg) ? "[Activity]" : $"[Activity({suppressArg})]";

        var source = $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyService {{
        {activityAttr}
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

        var hasSuppressBegin = output.Contains("SuppressInstrumentationScope.Begin()");
        var hasDisposableDispose = output.Contains("disposable?.Dispose()");

        bool correct;
        if (cfg.SuppressInstrumentation)
        {
            // When true: both should be present
            correct = hasSuppressBegin && hasDisposableDispose;
        }
        else
        {
            // When false: neither should be present
            correct = !hasSuppressBegin && !hasDisposableDispose;
        }

        return correct.Label(
            $"SuppressInstrumentation={cfg.SuppressInstrumentation}, " +
            $"hasSuppressBegin={hasSuppressBegin}, hasDisposableDispose={hasDisposableDispose}\n" +
            $"Output:\n{output}");
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
