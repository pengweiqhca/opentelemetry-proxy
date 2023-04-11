using Fody;
using Mono.Cecil;

namespace OpenTelemetry.StaticProxy.Fody;

// https://github.com/vescon/MethodBoundaryAspect.Fody
public class ModuleWeaver : BaseModuleWeaver
{
    public override void Execute()
    {
        string? version = null;

        var location = typeof(ModuleWeaver).Assembly.Location;
        if (File.Exists(location))
            version = FileVersionInfo.GetVersionInfo(location).ProductVersion?.Split('-')[0];

        if (string.IsNullOrWhiteSpace(version)) version = typeof(ModuleWeaver).Assembly.GetName().Version?.ToString();

        if (!string.IsNullOrWhiteSpace(version)) WriteInfo($"OpenTelemetry.StaticProxy version: {version}");

        new StaticProxyEmitter(new(ModuleDefinition,
            ResolveMainModule("System.Diagnostics.DiagnosticSource"),
            ResolveMainModule("OpenTelemetry"),
            ResolveMainModule("OpenTelemetry.Api"),
            ResolveMainModule("OpenTelemetry.Proxy"))).Emit();
    }

    private ModuleDefinition ResolveMainModule(string assemblyName)
    {
        var assembly = ResolveAssembly(assemblyName);
        if (assembly == null) throw new InvalidOperationException("Could not resolve assembly: " + assemblyName);

        return assembly.MainModule;
    }

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "OpenTelemetry";
        yield return "OpenTelemetry.Api";
        yield return "OpenTelemetry.Proxy";
        yield return "System.Diagnostics.DiagnosticSource";
    }

    public override bool ShouldCleanReference => true;
}
