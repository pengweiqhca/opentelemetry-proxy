using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenTelemetry.Proxy;
using Xunit;

namespace OpenTelemetry.StaticProxy.Tests;

/// <summary>
/// Edge case tests for the Source Generator.
///
/// Validates: Requirements 14.1, 14.2, 14.3, 2.8
/// </summary>
public class EdgeCaseTest
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
                or "System.Threading" or "System.Runtime.CompilerServices.Unsafe"
                or "System.Console")
                refs.Add(MetadataReference.CreateFromFile(asm));
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(ActivitySourceAttribute).Assembly.Location));
        return refs.ToArray();
    }

    private static string RunGenerator(string source)
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

    #region Expression Body Method Interception

    /// <summary>
    /// Validates: Requirement 14.1 — expression body method (=> syntax) is correctly intercepted.
    /// </summary>
    [Fact]
    public void ExpressionBody_Method_Generates_Interceptor()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    public class MathService {
        [Activity]
        public int DoWork(int x) => x + 1;
    }

    public class Caller {
        public void Call() {
            var svc = new MathService();
            svc.DoWork(42);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("InterceptsLocation", generated);
        Assert.Contains("StartActivity(", generated);
        Assert.Contains("activity?.Dispose()", generated);
        Assert.Contains("SetExceptionStatus(", generated);
    }

    #endregion

    #region Throw Expression Handling

    /// <summary>
    /// Validates: Requirement 14.3 — expression body with throw expression is correctly handled.
    /// </summary>
    [Fact]
    public void ThrowExpression_Method_Generates_Interceptor()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System;

namespace TestNs {
    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    public class ThrowService {
        [Activity]
        public int DoWork() => throw new NotImplementedException();
    }

    public class Caller {
        public void Call() {
            var svc = new ThrowService();
            svc.DoWork();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("InterceptsLocation", generated);
        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SetExceptionStatus(", generated);
        Assert.Contains("activity?.Dispose()", generated);
    }

    #endregion

    #region Empty Tag List

    /// <summary>
    /// Validates: Requirement 14.1 — method with no tags at all still generates correct interceptor.
    /// </summary>
    [Fact]
    public void EmptyTagList_Method_Generates_Interceptor_Without_SetTag()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public class NoTagService {
        [Activity]
        public async Task DoWork() => await Task.CompletedTask;
    }

    public class Caller {
        public void Call() {
            var svc = new NoTagService();
            svc.DoWork();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("InterceptsLocation", generated);
        Assert.Contains("StartActivity(", generated);
        Assert.Contains("activity?.Dispose()", generated);
        // No SetTag calls should be present since there are no tags
        Assert.DoesNotContain("SetTag(", generated);
    }

    #endregion

    #region Multiple Partial Declarations Merging

    /// <summary>
    /// Validates: Requirement 2.8 — partial class with [ActivitySource] on one declaration
    /// and methods on another are correctly merged.
    /// </summary>
    [Fact]
    public void PartialClass_Merges_Declarations()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public partial class PartialService {
    }

    public partial class PartialService {
        [Activity]
        public async Task DoWork() => await Task.CompletedTask;
    }

    public class Caller {
        public void Call() {
            var svc = new PartialService();
            svc.DoWork();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("InterceptsLocation", generated);
        Assert.Contains("StartActivity(", generated);
        Assert.Contains("activity?.Dispose()", generated);
    }

    #endregion
}
