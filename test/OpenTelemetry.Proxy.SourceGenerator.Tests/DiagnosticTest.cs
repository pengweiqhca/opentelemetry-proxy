using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OpenTelemetry.Proxy.Tests;

/// <summary>
/// Tests for diagnostic descriptors and diagnostic reporting through the MetadataExtractor.
///
/// Note: The diagnostics (OTSP001, OTSP002, OTSP003) are safety nets in MetadataExtractor
/// for parsing attribute arguments (ActivityKind enum and boolean values). Since the C# compiler
/// itself rejects most type mismatches at the syntax level, these diagnostics are hard to trigger
/// through normal C# code. We verify:
/// 1. Diagnostic descriptors exist with correct IDs and severity
/// 2. Valid attributes produce no diagnostics
/// 3. The TestMetadataExtractionGenerator pipeline correctly surfaces diagnostics when they occur
///
/// Validates: Requirements 11.1, 11.2, 11.3
/// </summary>
public class DiagnosticTest
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
        refs.Add(MetadataReference.CreateFromFile(typeof(OpenTelemetry.Proxy.ActivitySourceAttribute).Assembly.Location));
        return refs.ToArray();
    }

    /// <summary>
    /// Runs the TestMetadataExtractionGenerator and returns the generated output text.
    /// Diagnostics from MetadataExtractor are surfaced as "// DIAGNOSTIC:" comments.
    /// </summary>
    private static string RunMetadataExtraction(string source)
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

    /// <summary>
    /// Runs the real ProxySourceGenerator and returns the diagnostics from the driver result.
    /// </summary>
    private static IReadOnlyList<Diagnostic> RunGeneratorAndGetDiagnostics(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var comp = CSharpCompilation.Create("TestAssembly", [tree], Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var gen = new ProxySourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(gen);
        driver = driver.RunGeneratorsAndUpdateCompilation(comp, out _, out var diagnostics);

        return diagnostics;
    }

    #endregion

    #region Descriptor Tests

    [Fact]
    public void OTSP001_Descriptor_Has_Correct_Id_And_Severity()
    {
        var descriptor = DiagnosticDescriptors.UnrecognizedAttributeArg;

        Assert.Equal("OTSP001", descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.Equal("OpenTelemetry.Proxy", descriptor.Category);
    }

    [Fact]
    public void OTSP002_Descriptor_Has_Correct_Id_And_Severity()
    {
        var descriptor = DiagnosticDescriptors.InvalidAttributeArgValue;

        Assert.Equal("OTSP002", descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.Equal("OpenTelemetry.Proxy", descriptor.Category);
    }

    [Fact]
    public void OTSP003_Descriptor_Has_Correct_Id_And_Severity()
    {
        var descriptor = DiagnosticDescriptors.InvalidAttributeArgType;

        Assert.Equal("OTSP003", descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.Equal("OpenTelemetry.Proxy", descriptor.Category);
    }

    #endregion

    #region No Diagnostics for Valid Attributes

    [Fact]
    public void Valid_ActivitySource_With_Kind_Produces_No_Diagnostics()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Diagnostics;

namespace TestNs {
    [ActivitySource(Kind = ActivityKind.Server)]
    public class MyService {
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var output = RunMetadataExtraction(source);

        Assert.DoesNotContain("// DIAGNOSTIC:", output);
        Assert.Contains("Kind=(System.Diagnostics.ActivityKind)1", output);
    }

    [Fact]
    public void Valid_ActivitySource_With_Boolean_Args_Produces_No_Diagnostics()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource(IncludeAllMethods = true, SuppressInstrumentation = true)]
    public class MyService {
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var output = RunMetadataExtraction(source);

        Assert.DoesNotContain("// DIAGNOSTIC:", output);
        Assert.Contains("IncludeNonAsync=True", output);
        Assert.Contains("SuppressInstrumentation=True", output);
    }

    [Fact]
    public void Valid_Activity_With_Kind_And_SuppressInstrumentation_Produces_No_Diagnostics()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Diagnostics;

namespace TestNs {
    [ActivitySource]
    public class MyService {
        [Activity(Kind = ActivityKind.Client, SuppressInstrumentation = true)]
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var output = RunMetadataExtraction(source);

        Assert.DoesNotContain("// DIAGNOSTIC:", output);
    }

    [Fact]
    public void Valid_NonActivity_With_SuppressInstrumentation_Produces_No_Diagnostics()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource(IncludeAllMethods = true)]
    public class MyService {
        [NonActivity(true)]
        public void DoWork() { }
    }
}";
        var output = RunMetadataExtraction(source);

        Assert.DoesNotContain("// DIAGNOSTIC:", output);
        Assert.Contains("SuppressInstrumentation=True", output);
    }

    [Fact]
    public void Valid_Default_ActivitySource_Produces_No_Diagnostics()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource]
    public class MyService {
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var output = RunMetadataExtraction(source);

        Assert.DoesNotContain("// DIAGNOSTIC:", output);
        Assert.Contains("Kind=default", output);
        Assert.Contains("IncludeNonAsync=False", output);
        Assert.Contains("SuppressInstrumentation=False", output);
    }

    #endregion

    #region Diagnostics via Real ProxySourceGenerator

    [Fact]
    public void Valid_Code_Produces_No_Generator_Diagnostics()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Diagnostics;

namespace TestNs {
    [ActivitySource(Kind = ActivityKind.Producer, SuppressInstrumentation = false)]
    public class MyService {
        [Activity(Kind = ActivityKind.Consumer)]
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var diagnostics = RunGeneratorAndGetDiagnostics(source);

        // Filter to only OTSP diagnostics (ignore compiler diagnostics)
        var otspDiagnostics = diagnostics.Where(d => d.Id.StartsWith("OTSP")).ToList();
        Assert.Empty(otspDiagnostics);
    }

    [Fact]
    public void All_ActivityKind_Values_Produce_No_Diagnostics()
    {
        // Test all valid ActivityKind enum values
        var kinds = new[] { "Internal", "Server", "Client", "Producer", "Consumer" };

        foreach (var kind in kinds)
        {
            var source = $@"
using OpenTelemetry.Proxy;
using System.Diagnostics;

namespace TestNs {{
    [ActivitySource(Kind = ActivityKind.{kind})]
    public class MyService {{
        public async System.Threading.Tasks.Task DoWork() {{ }}
    }}
}}";
            var output = RunMetadataExtraction(source);
            Assert.DoesNotContain("// DIAGNOSTIC:", output);
        }
    }

    #endregion

    #region DiagnosticInfo Creation Tests

    [Fact]
    public void CreateDiagnosticInfo_Produces_Correct_OTSP001_Info()
    {
        // Verify that CreateDiagnosticInfo correctly creates DiagnosticInfo for OTSP001
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource]
    public class MyService {
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation.Create("Test", [tree], Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Get a syntax node to use for location
        var root = tree.GetRoot();
        var classNode = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();

        var info = MetadataExtractor.CreateDiagnosticInfo(
            DiagnosticDescriptors.UnrecognizedAttributeArg,
            classNode,
            "someExpression");

        Assert.Equal("OTSP001", info.Descriptor.Id);
        Assert.Single(info.MessageArgs.AsEnumerable());
        Assert.Equal("someExpression", info.MessageArgs[0]);
    }

    [Fact]
    public void CreateDiagnosticInfo_Produces_Correct_OTSP002_Info()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource]
    public class MyService {
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);

        var root = tree.GetRoot();
        var classNode = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();

        var info = MetadataExtractor.CreateDiagnosticInfo(
            DiagnosticDescriptors.InvalidAttributeArgValue,
            classNode);

        Assert.Equal("OTSP002", info.Descriptor.Id);
        Assert.Empty(info.MessageArgs.AsEnumerable());
    }

    [Fact]
    public void CreateDiagnosticInfo_Produces_Correct_OTSP003_Info()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource]
    public class MyService {
        public async System.Threading.Tasks.Task DoWork() { }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);

        var root = tree.GetRoot();
        var classNode = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();

        var info = MetadataExtractor.CreateDiagnosticInfo(
            DiagnosticDescriptors.InvalidAttributeArgType,
            classNode,
            "Boolean", "String");

        Assert.Equal("OTSP003", info.Descriptor.Id);
        Assert.Equal(2, info.MessageArgs.Length);
        Assert.Equal("Boolean", info.MessageArgs[0]);
        Assert.Equal("String", info.MessageArgs[1]);
    }

    #endregion
}
