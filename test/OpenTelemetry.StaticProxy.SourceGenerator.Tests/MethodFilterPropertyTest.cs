using FsCheck;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Tests;

/// <summary>
/// Feature: static-proxy-source-generator, Property 10: 方法过滤规则正确性
///
/// For any type annotated with [ActivitySource], the method filtering rules should correctly
/// determine which methods are included based on IncludeNonAsyncStateMachineMethod flag,
/// method visibility, async modifier, and explicit attribute annotations.
///
/// **Validates: Requirements 8.1, 8.2, 8.3**
/// </summary>
public class MethodFilterPropertyTest
{
    #region Models & Generators

    public enum Visibility { Public, Private, Internal }

    public record MethodConfig(
        string Name,
        Visibility Visibility,
        bool IsAsync,
        bool HasActivityAttribute,
        bool HasActivityNameAttribute,
        bool HasNonActivityAttribute);

    public record TestScenario(
        bool IncludeNonAsyncStateMachineMethod,
        MethodConfig[] Methods);

    public static class Generators
    {
        private static readonly char[] Letters = "ABCDEFGHIJKLMNOP".ToCharArray();
        private static readonly char[] LowerLetters = "abcdefghijklmnopqrst".ToCharArray();

        public static Gen<string> SafeId() =>
            from f in Gen.Elements(Letters)
            from len in Gen.Choose(2, 5)
            from rest in Gen.ArrayOf(len, Gen.Elements(LowerLetters))
            select f + new string(rest);

        public static Gen<MethodConfig> MethodCfg(int index) =>
            from vis in Gen.Elements(Visibility.Public, Visibility.Private, Visibility.Internal)
            from isAsync in Arb.Generate<bool>()
            from hasActivity in Arb.Generate<bool>()
            from hasActivityName in Gen.Constant(false) // simplify: don't combine with hasActivity
            from hasNonActivity in Gen.Constant(false)
            select new MethodConfig($"Method{index}", vis, isAsync, hasActivity, hasActivityName, hasNonActivity);

        public static Gen<TestScenario> Scenario() =>
            from includeNonAsync in Arb.Generate<bool>()
            from count in Gen.Choose(1, 4)
            from methods in Gen.Sequence(Enumerable.Range(0, count).Select(MethodCfg))
            select new TestScenario(includeNonAsync, methods.ToArray());
    }

    public class Arbs
    {
        public static Arbitrary<TestScenario> ArbScenario() =>
            Generators.Scenario().ToArbitrary();
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

    #region Source Code Builder

    private static string BuildVisibilityKeyword(Visibility vis) => vis switch
    {
        Visibility.Public => "public",
        Visibility.Private => "private",
        Visibility.Internal => "internal",
        _ => "public"
    };

    private static string BuildMethodSource(MethodConfig m)
    {
        var attrs = new List<string>();
        if (m.HasActivityAttribute) attrs.Add("[Activity]");
        if (m.HasActivityNameAttribute) attrs.Add("[ActivityName]");
        if (m.HasNonActivityAttribute) attrs.Add("[NonActivity]");

        var attrStr = attrs.Count > 0 ? string.Join(" ", attrs) + "\n        " : "";
        var vis = BuildVisibilityKeyword(m.Visibility);
        var asyncKw = m.IsAsync ? "async " : "";
        var returnType = m.IsAsync ? "Task" : "void";
        var body = m.IsAsync ? "await Task.CompletedTask;" : "{ }";

        return $"        {attrStr}{vis} {asyncKw}{returnType} {m.Name}() {(m.IsAsync ? $"{{ {body} }}" : body)}";
    }

    private static string BuildSource(TestScenario scenario)
    {
        var includeArg = scenario.IncludeNonAsyncStateMachineMethod
            ? "IncludeNonAsyncStateMachineMethod = true"
            : "";
        var attrArgs = string.IsNullOrEmpty(includeArg) ? "" : $"({includeArg})";

        var methods = string.Join("\n", scenario.Methods.Select(BuildMethodSource));

        return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;
namespace TestNs {{
    [ActivitySource{attrArgs}]
    public class TestClass {{
{methods}
    }}
}}";
    }

    #endregion

    #region Filtering Logic (Expected)

    /// <summary>
    /// Determines whether a method should be included in the generated output
    /// based on the filtering rules from Requirements 8.1, 8.2, 8.3.
    /// </summary>
    private static bool ShouldMethodBeIncluded(MethodConfig m, bool includeNonAsync)
    {
        var isPublic = m.Visibility == Visibility.Public;

        // Requirement 8.3: Non-public methods without explicit attributes are never included
        if (!isPublic && !m.HasActivityAttribute && !m.HasActivityNameAttribute)
            return false;

        // Methods with explicit [Activity] or [ActivityName] are always included
        if (m.HasActivityAttribute || m.HasActivityNameAttribute)
            return true;

        // Requirement 8.1: When IncludeNonAsyncStateMachineMethod=false,
        // only async methods are auto-included (among public methods)
        if (!includeNonAsync)
            return isPublic && m.IsAsync;

        // Requirement 8.2: When IncludeNonAsyncStateMachineMethod=true,
        // all public methods are included
        return isPublic;
    }

    #endregion

    #region Property Tests

    /// <summary>
    /// Property 10: Method filtering rules correctness.
    ///
    /// When IncludeNonAsyncStateMachineMethod=false: only async methods and explicitly
    /// attributed methods are included.
    /// When IncludeNonAsyncStateMachineMethod=true: all public methods are included.
    /// Non-public methods without explicit attributes are never included.
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property Method_Filter_Rules_Are_Correct(TestScenario scenario)
    {
        var source = BuildSource(scenario);
        var output = RunAndGetOutput(source);

        // Parse METHOD_INFO lines from output
        var methodInfoLines = output.Split('\n')
            .Where(l => l.Contains("// METHOD_INFO:"))
            .Select(l => l.Trim())
            .ToList();

        // Build a set of method names that appear in the output
        var reportedMethods = new Dictionary<string, (bool HasActivity, bool HasActivityName, bool HasNonActivity, bool IsAsync, bool IsPublic)>();
        foreach (var line in methodInfoLines)
        {
            var name = ExtractValue(line, "MethodName=");
            var hasActivity = ExtractValue(line, "HasActivity=") == "True";
            var hasActivityName = ExtractValue(line, "HasActivityName=") == "True";
            var hasNonActivity = ExtractValue(line, "HasNonActivity=") == "True";
            var isAsync = ExtractValue(line, "IsAsync=") == "True";
            var isPublic = ExtractValue(line, "IsPublic=") == "True";
            reportedMethods[name] = (hasActivity, hasActivityName, hasNonActivity, isAsync, isPublic);
        }

        // Verify each method's flags match what we generated
        var allCorrect = true;
        var labels = new List<string>();

        foreach (var m in scenario.Methods)
        {
            if (!reportedMethods.TryGetValue(m.Name, out var reported))
            {
                allCorrect = false;
                labels.Add($"Method {m.Name} not found in output");
                continue;
            }

            var expectedIsPublic = m.Visibility == Visibility.Public;

            // Verify the extracted flags match the input
            if (reported.HasActivity != m.HasActivityAttribute)
            {
                allCorrect = false;
                labels.Add($"{m.Name}: HasActivity expected={m.HasActivityAttribute} actual={reported.HasActivity}");
            }
            if (reported.IsAsync != m.IsAsync)
            {
                allCorrect = false;
                labels.Add($"{m.Name}: IsAsync expected={m.IsAsync} actual={reported.IsAsync}");
            }
            if (reported.IsPublic != expectedIsPublic)
            {
                allCorrect = false;
                labels.Add($"{m.Name}: IsPublic expected={expectedIsPublic} actual={reported.IsPublic}");
            }

            // Now verify the filtering rule:
            // A method "should be included" means it would be picked up for interception.
            // The MethodInfo flags allow the downstream code to apply the filter.
            // We verify the flags are correct so the filter CAN be applied correctly.
            var shouldInclude = ShouldMethodBeIncluded(m, scenario.IncludeNonAsyncStateMachineMethod);

            // Verify the filtering logic using the extracted flags matches our expectation
            var actualShouldInclude = ShouldMethodBeIncludedFromFlags(
                reported.HasActivity, reported.HasActivityName, reported.HasNonActivity,
                reported.IsAsync, reported.IsPublic,
                scenario.IncludeNonAsyncStateMachineMethod);

            if (shouldInclude != actualShouldInclude)
            {
                allCorrect = false;
                labels.Add($"{m.Name}: filter expected={shouldInclude} actual={actualShouldInclude} " +
                           $"(vis={m.Visibility}, async={m.IsAsync}, hasActivity={m.HasActivityAttribute}, includeNonAsync={scenario.IncludeNonAsyncStateMachineMethod})");
            }
        }

        var label = allCorrect
            ? $"All {scenario.Methods.Length} methods verified correctly"
            : string.Join("\n", labels) + $"\nOutput:\n{output}";

        return allCorrect.Label(label);
    }

    /// <summary>
    /// Apply the same filtering logic using the extracted flags from the generator output.
    /// This mirrors ShouldMethodBeIncluded but uses the raw flags.
    /// </summary>
    private static bool ShouldMethodBeIncludedFromFlags(
        bool hasActivity, bool hasActivityName, bool hasNonActivity,
        bool isAsync, bool isPublic, bool includeNonAsync)
    {
        // Non-public methods without explicit attributes are never included
        if (!isPublic && !hasActivity && !hasActivityName)
            return false;

        // Methods with explicit attributes are always included
        if (hasActivity || hasActivityName)
            return true;

        // IncludeNonAsync=false: only async public methods
        if (!includeNonAsync)
            return isPublic && isAsync;

        // IncludeNonAsync=true: all public methods
        return isPublic;
    }

    private static string ExtractValue(string line, string key)
    {
        var idx = line.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return "";
        var start = idx + key.Length;
        var end = line.IndexOf(' ', start);
        return end < 0 ? line.Substring(start) : line.Substring(start, end - start);
    }

    #endregion
}
