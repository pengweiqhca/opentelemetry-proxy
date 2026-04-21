using FsCheck;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Tests;

/// <summary>
/// Feature: static-proxy-source-generator
/// Property 11: 泛型类型与方法支持
///
/// These tests use the REAL ProxySourceGenerator to verify that generated interceptor
/// code correctly handles generic types with varying arity counts.
/// </summary>
public class GenericPropertyTest
{
    #region Models & Generators

    public record GenericArityConfig(int Arity);

    public static class Generators
    {
        public static Gen<GenericArityConfig> GenericArityCfg() =>
            from arity in Gen.Choose(1, 3)
            select new GenericArityConfig(arity);
    }

    public class Arbs
    {
        public static Arbitrary<GenericArityConfig> ArbGenericArity() =>
            Generators.GenericArityCfg().ToArbitrary();
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

    #region Source Builders

    /// <summary>
    /// Builds type parameter list like "T1, T2, T3" for the given arity.
    /// </summary>
    private static string BuildTypeParams(int arity) =>
        string.Join(", ", Enumerable.Range(1, arity).Select(i => $"T{i}"));

    /// <summary>
    /// Builds method parameters using each type param, like "T1 arg1, T2 arg2, T3 arg3".
    /// </summary>
    private static string BuildMethodParams(int arity) =>
        string.Join(", ", Enumerable.Range(1, arity).Select(i => $"T{i} arg{i}"));

    /// <summary>
    /// Builds concrete type arguments for the caller, like "int, string, bool".
    /// </summary>
    private static string BuildConcreteTypeArgs(int arity)
    {
        var types = new[] { "int", "string", "bool" };
        return string.Join(", ", Enumerable.Range(0, arity).Select(i => types[i % types.Length]));
    }

    /// <summary>
    /// Builds concrete argument values for the caller, like "0, \"x\", true".
    /// </summary>
    private static string BuildConcreteArgs(int arity)
    {
        var values = new[] { "0", "\"x\"", "true" };
        return string.Join(", ", Enumerable.Range(0, arity).Select(i => values[i % values.Length]));
    }

    /// <summary>
    /// Builds the open generic typeof comma separator, like "" for 1, "," for 2, ",," for 3.
    /// </summary>
    private static string BuildOpenGenericCommas(int arity) =>
        new string(',', arity - 1);

    private static string BuildGenericTypeSource(int arity)
    {
        var typeParams = BuildTypeParams(arity);
        var methodParams = BuildMethodParams(arity);
        var concreteTypeArgs = BuildConcreteTypeArgs(arity);
        var concreteArgs = BuildConcreteArgs(arity);

        return $@"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {{
    [ActivitySource]
    public class MyGenClass<{typeParams}> {{
        [Activity]
        public async Task DoWork({methodParams}) {{ await Task.CompletedTask; }}
    }}

    public class Caller {{
        public void Call() {{
            var svc = new MyGenClass<{concreteTypeArgs}>();
            svc.DoWork({concreteArgs});
        }}
    }}
}}";
    }

    #endregion

    #region Property 11: 泛型类型与方法支持

    /// <summary>
    /// Property 11: Generic type and method support.
    ///
    /// For any generic type with arity 1-3, the generated code should:
    /// (a) Use the open generic type name format for ActivitySource name (e.g., MyGenClass`2)
    /// (b) Use the correct open generic typeof expression (e.g., MyGenClass&lt;,&gt;)
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(Arbs)])]
    public Property Generic_Type_ActivitySource_Uses_Open_Generic_Name(GenericArityConfig cfg)
    {
        var source = BuildGenericTypeSource(cfg.Arity);
        var output = RunRealGenerator(source);

        // Verify ActivitySource name uses open generic format: MyGenClass`N
        var expectedBacktickName = $"MyGenClass`{cfg.Arity}";
        var hasBacktickName = output.Contains(expectedBacktickName);

        // Verify typeof uses open generic format: MyGenClass<,> for arity 2, MyGenClass<,,> for arity 3, etc.
        var openGenericCommas = BuildOpenGenericCommas(cfg.Arity);
        var expectedTypeOf = $"MyGenClass<{openGenericCommas}>";
        var hasTypeOf = output.Contains(expectedTypeOf);

        // Verify interceptor method is generated
        var hasInterceptor = output.Contains("_Intercept_");

        var allCorrect = hasBacktickName && hasTypeOf && hasInterceptor;

        return allCorrect.Label(
            $"Arity={cfg.Arity}, " +
            $"expectedBacktickName=\"{expectedBacktickName}\", hasBacktickName={hasBacktickName}, " +
            $"expectedTypeOf=\"{expectedTypeOf}\", hasTypeOf={hasTypeOf}, " +
            $"hasInterceptor={hasInterceptor}\n" +
            $"Output:\n{output}");
    }

    #endregion
}
