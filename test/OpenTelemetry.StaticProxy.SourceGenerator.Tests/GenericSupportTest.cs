using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenTelemetry.Proxy;
using Xunit;

namespace OpenTelemetry.StaticProxy.Tests;

/// <summary>
/// End-to-end tests for generic type and generic method support.
/// Validates: Requirements 10.1, 10.2
/// </summary>
public class GenericSupportTest
{
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

    private static (string Output, IEnumerable<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var comp = CSharpCompilation.Create("TestAssembly", [tree], Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var gen = new ProxySourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(gen);
        driver = driver.RunGeneratorsAndUpdateCompilation(comp, out var outputComp, out var diagnostics);
        var result = driver.GetRunResult();

        var allOutput = new System.Text.StringBuilder();
        foreach (var genResult in result.Results)
            foreach (var genSource in genResult.GeneratedSources)
                allOutput.AppendLine(genSource.SourceText.ToString());

        return (allOutput.ToString(), diagnostics);
    }

    #endregion

    /// <summary>
    /// Generic type with [ActivitySource] should use open generic type name for ActivitySource name
    /// and generate correct interceptor code.
    /// </summary>
    [Fact]
    public void Generic_Type_ActivitySource_Uses_Open_Generic_Name()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public class GenericService<T> {
        [Activity]
        public async Task DoWork(T item) { await Task.CompletedTask; }
    }

    public class Caller {
        public void Call() {
            var svc = new GenericService<int>();
            svc.DoWork(42);
        }
    }
}";

        var (output, diagnostics) = RunGenerator(source);

        // ActivitySource name should use open generic format: GenericService`1
        Assert.Contains("GenericService`1", output);

        // typeof expression should use open generic format: GenericService<>
        Assert.Contains("GenericService<>", output);

        // Should have StartActivity call
        Assert.Contains("StartActivity(", output);

        // Should have the interceptor method with type argument
        Assert.Contains("_Intercept_", output);

        // No generator diagnostics
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Generic method on a non-generic type should preserve method type parameters
    /// and constraints in the interceptor.
    /// </summary>
    [Fact]
    public void Generic_Method_Preserves_Type_Parameters()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public class MyService {
        [Activity]
        public async Task DoWork<TItem>(TItem item) where TItem : class {
            await Task.CompletedTask;
        }
    }

    public class Caller {
        public void Call() {
            var svc = new MyService();
            svc.DoWork<string>(""hello"");
        }
    }
}";

        var (output, diagnostics) = RunGenerator(source);

        // Should have the interceptor method
        Assert.Contains("_Intercept_", output);
        Assert.Contains("StartActivity(", output);

        // No generator diagnostics
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Generic type + generic method should combine type parameters from both.
    /// </summary>
    [Fact]
    public void Generic_Type_And_Method_Combines_Type_Parameters()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public class GenericService<T> {
        [Activity]
        public async Task Process<U>(T item, U extra) where U : class {
            await Task.CompletedTask;
        }
    }

    public class Caller {
        public void Call() {
            var svc = new GenericService<int>();
            svc.Process<string>(42, ""test"");
        }
    }
}";

        var (output, diagnostics) = RunGenerator(source);

        // Should have the interceptor method
        Assert.Contains("_Intercept_", output);
        Assert.Contains("StartActivity(", output);

        // ActivitySource name should use open generic format
        Assert.Contains("GenericService`1", output);

        // No generator diagnostics
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Generic method with constraints should have those constraints copied to the interceptor
    /// when the call site uses an open type parameter.
    /// </summary>
    [Fact]
    public void Generic_Method_Constraints_Are_Copied()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public class MyService {
        [Activity]
        public async Task DoWork<T>(T item) where T : class, new() {
            await Task.CompletedTask;
        }
    }

    public class Caller {
        public void Call<T>(T item) where T : class, new() {
            var svc = new MyService();
            svc.DoWork<T>(item);
        }
    }
}";

        var (output, diagnostics) = RunGenerator(source);

        // Should contain the where clause with constraints
        Assert.Contains("where T : class, new()", output);

        // No generator diagnostics
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Generic type with multiple type parameters should work correctly.
    /// </summary>
    [Fact]
    public void Generic_Type_Multiple_Parameters()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public class PairService<TKey, TValue> {
        [Activity]
        public async Task Store(TKey key, TValue value) { await Task.CompletedTask; }
    }

    public class Caller {
        public void Call() {
            var svc = new PairService<string, int>();
            svc.Store(""key"", 42);
        }
    }
}";

        var (output, diagnostics) = RunGenerator(source);

        // ActivitySource name should use open generic format with arity 2
        Assert.Contains("PairService`2", output);

        // typeof should use PairService<,>
        Assert.Contains("PairService<,>", output);

        // Should have the interceptor
        Assert.Contains("_Intercept_", output);

        // No generator diagnostics
        Assert.Empty(diagnostics);
    }
}
