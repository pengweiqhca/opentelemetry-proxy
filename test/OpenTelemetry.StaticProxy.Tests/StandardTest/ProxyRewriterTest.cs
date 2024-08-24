using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Model;
using OpenTelemetry.Proxy;
using OpenTelemetry.StaticProxy;
using System.Collections.Immutable;
using System.Reflection;

namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

internal class ProxyRewriterTest : AnalyzerTest<DefaultVerifier>
{
    private static readonly LanguageVersion DefaultLanguageVersion =
        Enum.TryParse("Default", out LanguageVersion version) ? version : LanguageVersion.CSharp6;

    protected override string DefaultFileExt => "cs";

    public override string Language => LanguageNames.CSharp;

    public ProxyRewriterTest(string codeFileName)
    {
        TestCode = File.ReadAllText("StandardFiles\\" + codeFileName + ".cs");

        ReferenceAssemblies = ReferenceAssemblies.Default
            .AddPackages(new[] { new PackageIdentity("OpenTelemetry", "1.9.0") }.ToImmutableArray())
            .AddAssemblies(new[] { typeof(ActivityTagAttribute).Assembly.Location }.ToImmutableArray());
    }

    protected override CompilationOptions CreateCompilationOptions() =>
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);

    protected override ParseOptions CreateParseOptions() =>
        new CSharpParseOptions(DefaultLanguageVersion, DocumentationMode.Diagnose)
#if NET6_0_OR_GREATER
            .WithPreprocessorSymbols("NET6_0_OR_GREATER");
#else
            .WithPreprocessorSymbols("NET472_OR_GREATER");
#endif
    protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
        [];

    private async Task<(Project, SolutionState, Compilation)> GetCompilationAsync(CancellationToken cancellationToken)
    {
        var analyzers = GetDiagnosticAnalyzers().ToArray();
        var fixableDiagnostics = ImmutableArray<string>.Empty;

        var testState = TestState.WithInheritedValuesApplied(null, fixableDiagnostics)
            .WithProcessedMarkup(MarkupOptions, GetDefaultDiagnostic(analyzers),
                analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToImmutableArray(),
                fixableDiagnostics, DefaultFilePath);

        var project = await CreateProjectAsync(new(testState, ReferenceAssemblies),
            testState.AdditionalProjects.Values.Select(additionalProject =>
                new EvaluatedProjectState(additionalProject, ReferenceAssemblies)).ToImmutableArray(),
            cancellationToken).ConfigureAwait(false);

        return (project, testState, await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException());
    }

    public async Task<IReadOnlyList<TypeMethods>> VisitAsync()
    {
        var (project, solution, compilation) = await GetCompilationAsync(default).ConfigureAwait(false);

        var list = new List<Diagnostic>();

        var methods = ProxyVisitor.GetProxyMethods(compilation, list.Add);

        typeof(AnalyzerTest<DefaultVerifier>)
            .GetMethod("VerifyDiagnosticResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(this,
            [
                list.Select(d => (project, d)), GetDiagnosticAnalyzers().ToImmutableArray(),
                solution.ExpectedDiagnostics.ToArray(), Verify
            ]);

        return methods.ToArray();
    }
}
