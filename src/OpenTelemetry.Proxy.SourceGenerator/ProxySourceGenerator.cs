using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using OpenTelemetry.Proxy.Models;
using System.Collections.Immutable;

namespace OpenTelemetry.Proxy;

[Generator(LanguageNames.CSharp)]
public class ProxySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Collect types annotated with [ActivitySource]
        var typeProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "OpenTelemetry.Proxy.ActivitySourceAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => MetadataExtractor.ExtractTypeMetadata(ctx, ct));

        // 2. Collect methods annotated with [Activity]
        var activityMethodProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "OpenTelemetry.Proxy.ActivityAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, ct) => MetadataExtractor.ExtractActivityMethodMetadata(ctx, ct));

        // 3. Collect types/methods annotated with [ActivityName]
        var activityNameProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "OpenTelemetry.Proxy.ActivityNameAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax or TypeDeclarationSyntax,
                transform: static (ctx, ct) => MetadataExtractor.ExtractActivityNameMetadata(ctx, ct));

        // 4. Collect methods annotated with [NonActivity]
        var nonActivityProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "OpenTelemetry.Proxy.NonActivityAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, ct) => MetadataExtractor.ExtractNonActivityMetadata(ctx, ct));

        // 5. Collect and combine all metadata
        var combined = typeProvider.Collect()
            .Combine(activityMethodProvider.Collect())
            .Combine(activityNameProvider.Collect())
            .Combine(nonActivityProvider.Collect());

        // 6. Read project properties (e.g. DisableProxyGenerator)
        var disabledProvider = context.AnalyzerConfigOptionsProvider.Select(
            static (options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.DisableProxyGenerator", out var value);

                return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            });

        // 7. Combine with CompilationProvider and disabled flag, register source output
        context.RegisterSourceOutput(
            combined.Combine(context.CompilationProvider).Combine(disabledProvider),
            static (spc, source) => Execute(spc, source));
    }

    /// <summary>
    /// Entry point for the source output callback. Destructures the combined pipeline tuple
    /// and delegates to the main Execute logic.
    /// </summary>
    private static void Execute(
        SourceProductionContext spc,
        ((((( ImmutableArray<TypeExtractionResult> Types,
              ImmutableArray<MethodExtractionResult> ActivityMethods),
             ImmutableArray<ActivityNameExtractionResult> ActivityNameItems),
            ImmutableArray<MethodExtractionResult> NonActivityMethods) Metadata,
          Compilation Compilation) Left,
         bool Disabled) source)
    {
        if (source.Disabled) return;

        var types = source.Left.Metadata.Item1.Item1.Types;
        var activityMethods = source.Left.Metadata.Item1.Item1.ActivityMethods;
        var activityNameItems = source.Left.Metadata.Item1.ActivityNameItems;
        var nonActivityMethods = source.Left.Metadata.NonActivityMethods;

        Execute(spc, source.Left.Compilation, types, activityMethods, activityNameItems, nonActivityMethods);
    }

    /// <summary>
    /// Main execution logic: reports diagnostics, filters out errored items,
    /// merges metadata, applies filtering, and invokes call site scanning and code generation.
    /// </summary>
    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<TypeExtractionResult> typeResults,
        ImmutableArray<MethodExtractionResult> activityMethodResults,
        ImmutableArray<ActivityNameExtractionResult> activityNameResults,
        ImmutableArray<MethodExtractionResult> nonActivityMethodResults)
    {
        var ct = context.CancellationToken;
        ct.ThrowIfCancellationRequested();

        // Report all diagnostics and filter out items with errors
        var types = ReportAndFilterTypes(context, typeResults);
        var activityMethods = ReportAndFilterMethods(context, activityMethodResults);
        var nonActivityMethods = ReportAndFilterMethods(context, nonActivityMethodResults);

        // Separate ActivityName items into TypeMetadata and MethodMetadata
        var activityNameTypes = ImmutableArray.CreateBuilder<TypeMetadata>();
        var activityNameMethods = ImmutableArray.CreateBuilder<MethodMetadata>();

        foreach (var result in activityNameResults)
        {
            // Report diagnostics for ActivityName items
            ReportDiagnostics(context, result.Diagnostics);

            // Skip items with errors
            if (result.HasErrors || result.Item == null) continue;

            switch (result.Item)
            {
                case TypeMetadata tm:
                    activityNameTypes.Add(tm);
                    break;
                case MethodMetadata mm:
                    activityNameMethods.Add(mm);
                    break;
            }
        }

        // Merge all method-level metadata
        var allMethods = ImmutableArray.CreateBuilder<MethodMetadata>(
            activityMethods.Length + activityNameMethods.Count + nonActivityMethods.Length);
        allMethods.AddRange((IEnumerable<MethodMetadata>)activityMethods);
        allMethods.AddRange(activityNameMethods);
        allMethods.AddRange((IEnumerable<MethodMetadata>)nonActivityMethods);

        // Merge all type-level metadata
        var allTypes = ImmutableArray.CreateBuilder<TypeMetadata>(
            types.Length + activityNameTypes.Count);
        allTypes.AddRange((IEnumerable<TypeMetadata>)types);
        allTypes.AddRange(activityNameTypes);

        // Apply method filtering rules based on type-level IncludeNonAsyncStateMachineMethod
        var filteredMethods = ApplyMethodFiltering(allTypes.ToImmutable(), allMethods.ToImmutable());

        ct.ThrowIfCancellationRequested();

        // Scan all call sites that match target methods
        var callSites = CallSiteScanner.ScanCallSites(compilation, filteredMethods, allTypes.ToImmutable(), ct);

        // Generate interceptor code for all matched call sites
        InterceptorEmitter.Emit(context, compilation, callSites, allTypes.ToImmutable());
    }

    #region Diagnostic Reporting

    /// <summary>
    /// Report diagnostics from type extraction results and return only the non-errored types.
    /// </summary>
    private static ImmutableArray<TypeMetadata> ReportAndFilterTypes(
        SourceProductionContext context,
        ImmutableArray<TypeExtractionResult> results)
    {
        var filtered = ImmutableArray.CreateBuilder<TypeMetadata>();

        foreach (var result in results)
        {
            ReportDiagnostics(context, result.Diagnostics);

            if (!result.HasErrors)
                filtered.Add(result.Metadata);
        }

        return filtered.ToImmutable();
    }

    /// <summary>
    /// Report diagnostics from method extraction results and return only the non-errored methods.
    /// </summary>
    private static ImmutableArray<MethodMetadata> ReportAndFilterMethods(
        SourceProductionContext context,
        ImmutableArray<MethodExtractionResult> results)
    {
        var filtered = ImmutableArray.CreateBuilder<MethodMetadata>();

        foreach (var result in results)
        {
            ReportDiagnostics(context, result.Diagnostics);

            if (!result.HasErrors)
                filtered.Add(result.Metadata);
        }

        return filtered.ToImmutable();
    }

    /// <summary>
    /// Report a collection of diagnostics to the SourceProductionContext.
    /// </summary>
    private static void ReportDiagnostics(
        SourceProductionContext context,
        EquatableArray<DiagnosticInfo> diagnostics)
    {
        foreach (var diag in diagnostics)
        {
            var location = Location.Create(
                diag.Location.FilePath,
                TextSpan.FromBounds(0, 0),
                new(
                    new(diag.Location.StartLine, diag.Location.StartCharacter),
                    new(diag.Location.EndLine, diag.Location.EndCharacter)));

            var args = new object[diag.MessageArgs.Length];
            for (var i = 0; i < diag.MessageArgs.Length; i++)
                args[i] = diag.MessageArgs[i];

            context.ReportDiagnostic(Diagnostic.Create(
                diag.Descriptor,
                location,
                args));
        }
    }

    #endregion

    /// <summary>
    /// Apply method filtering rules:
    /// - For each type with IncludeNonAsyncStateMachineMethod=false, only async methods
    ///   and explicitly attributed methods are included.
    /// - For each type with IncludeNonAsyncStateMachineMethod=true, all public methods are included.
    /// - Interface types include all methods regardless.
    /// Methods with explicit [Activity], [ActivityName], or [NonActivity] attributes are always included.
    /// </summary>
    internal static ImmutableArray<MethodMetadata> ApplyMethodFiltering(
        ImmutableArray<TypeMetadata> types,
        ImmutableArray<MethodMetadata> methods)
    {
        var result = ImmutableArray.CreateBuilder<MethodMetadata>();

        // All explicitly attributed methods are always included
        result.AddRange((IEnumerable<MethodMetadata>)methods);

        // For each type, check if there are implicit methods that should be included
        // based on IncludeNonAsyncStateMachineMethod and visibility rules.
        // Note: Actual MethodMetadata for implicit methods will be fully constructed
        // during call site scanning (Task 4.2) when we have full semantic info.
        // Here we just track which method names from each type should be eligible.
        foreach (var type in types)
        {
            var includeNonAsync = type.IncludeNonAsyncStateMachineMethod;

            foreach (var methodInfo in type.Methods)
            {
                // Skip methods that already have explicit attributes
                if (methodInfo.HasActivityAttribute ||
                    methodInfo.HasActivityNameAttribute ||
                    methodInfo.HasNonActivityAttribute)
                    continue;

                // Apply filtering rules
                if (!includeNonAsync && !methodInfo.IsAsync)
                    continue;

                if (!methodInfo.IsPublic)
                    continue;
            }
        }

        return result.ToImmutable();
    }
}
