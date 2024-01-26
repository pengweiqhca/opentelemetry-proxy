using Mono.Cecil;
using Mono.Cecil.Rocks;
using System.Runtime.CompilerServices;

namespace OpenTelemetry.StaticProxy.Fody;

internal class EmitContext
{
    public ModuleDefinition TargetModule { get; }

    public TypeReference Activity { get; }

    public MethodReference ActivitySetStatus { get; }

    public MethodReference ActivityDispose { get; }

    public MethodReference ActivityGetParent { get; }

    public MethodReference ActivitySetCurrent { get; }

    public MethodReference ActivitySetTagEnumerable { get; }

    public TypeReference ActivitySource { get; }

    public MethodReference ActivitySourceCtor { get; }

    public MethodReference ActivitySourceStartActivity { get; }

    public MethodReference Begin { get; }

    public MethodReference RecordException { get; }

    public MethodReference SetName { get; }

    public TypeReference ActivityAttribute { get; }

    public TypeReference ActivityNameAttribute { get; }

    public TypeReference ActivitySourceAttribute { get; }

    public TypeReference ActivityTagAttribute { get; }

    public TypeReference NonActivityAttribute { get; }

    public MethodReference ProxyHasGeneratedAttributeCtor { get; }

    public TypeReference Action { get; }

    public MethodReference ActionCtor { get; }

    public TypeReference Disposable { get; }

    public TypeReference Exception { get; }

    public MethodReference Dispose { get; }

    public MethodReference GetMessage { get; }

    public MethodReference ObjectCtor { get; }

    public ActivityAwaiterEmitter ActivityAwaiterEmitter { get; }

    public TypeReference AsyncStateMachineAttribute { get; }

    public TypeReference CompilerGeneratedAttribute { get; }

    public MethodReference CompilerGeneratedAttributeCtor { get; }

    public TypeReference KeyValuePair { get; }

    public MethodReference KeyValuePairCtor { get; }

    public EmitContext(ModuleDefinition targetModule,
        ModuleDefinition diagnosticSourceModule,
        ModuleDefinition openTelemetryModule,
        ModuleDefinition openTelemetryApiModule,
        ModuleDefinition openTelemetryProxyModule)
    {
        TargetModule = targetModule;

        var activity = diagnosticSourceModule.GetType("System.Diagnostics.Activity");
        var activitySource = diagnosticSourceModule.GetType("System.Diagnostics.ActivitySource");

        Activity = targetModule.ImportReference(activity);
        ActivitySetStatus = targetModule.ImportReference(activity.Methods.Single(static m => m.Name == "SetStatus"));

        ActivityDispose = targetModule.ImportReference(activity.GetParameterlessMethod("Dispose"));

        ActivityGetParent = targetModule.ImportReference(activity.Methods.Single(static m => m.Name == "get_Parent"));

        ActivitySetCurrent = targetModule.ImportReference(activity.Methods.Single(static m => m.Name == "set_Current"));

        ActivitySetTagEnumerable = targetModule.ImportReference(openTelemetryProxyModule
            .GetType("OpenTelemetry.Proxy.ActivityExtensions")
            .GetMethods().Single(static m => m.Name == "SetTagEnumerable"));

        ActivitySource = targetModule.ImportReference(activitySource);
        ActivitySourceCtor =
            targetModule.ImportReference(activitySource.GetConstructors().Single(static c => !c.IsStatic));

        ActivitySourceStartActivity = targetModule.ImportReference(activitySource.GetMethods("StartActivity")
            .Single(static m =>
                m.Parameters.Count == 2 && m.Parameters[0].ParameterType == m.Module.TypeSystem.String));

        Begin = targetModule.ImportReference(openTelemetryModule.GetType("OpenTelemetry.SuppressInstrumentationScope")
            .GetMethods()
            .Single(static m => m.Name == nameof(Begin)));

        RecordException = targetModule.ImportReference(openTelemetryApiModule
            .GetType("OpenTelemetry.Trace.ActivityExtensions").Methods
            .Single(static m => m.Name == nameof(RecordException) && m.Parameters.Count == 2));

        SetName = targetModule.ImportReference(openTelemetryProxyModule
            .GetType("OpenTelemetry.Proxy.ActivityName")
            .GetMethods().Single(static m => m.Name == nameof(SetName) && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.Name == "IReadOnlyCollection`1"));

        ActivityAttribute = openTelemetryProxyModule.GetType("OpenTelemetry.Proxy.ActivityAttribute");
        ActivityNameAttribute = openTelemetryProxyModule.GetType("OpenTelemetry.Proxy.ActivityNameAttribute");
        ActivitySourceAttribute = openTelemetryProxyModule.GetType("OpenTelemetry.Proxy.ActivitySourceAttribute");
        ActivityTagAttribute = openTelemetryProxyModule.GetType("OpenTelemetry.Proxy.ActivityTagAttribute");
        NonActivityAttribute = openTelemetryProxyModule.GetType("OpenTelemetry.Proxy.NonActivityAttribute");
        ProxyHasGeneratedAttributeCtor = openTelemetryProxyModule.GetType("OpenTelemetry.Proxy.ProxyHasGeneratedAttribute")
            .GetConstructors().Single(c => !c.IsStatic);

        Action = new(typeof(Exception).Namespace, nameof(Action), targetModule, targetModule.TypeSystem.CoreLibrary);
        ActionCtor = targetModule.ImportReference(Action.Resolve().GetConstructors().Single());

        Disposable = new(typeof(Exception).Namespace, nameof(IDisposable), targetModule,
            targetModule.TypeSystem.CoreLibrary);

        Dispose = targetModule.ImportReference(
            new MethodReference(nameof(Dispose), targetModule.TypeSystem.Void, Disposable)
            {
                HasThis = true
            });

        Exception = new(typeof(Exception).Namespace, nameof(Exception), targetModule,
            targetModule.TypeSystem.Void.Scope);

        GetMessage = targetModule.ImportReference(
            new MethodReference("get_Message", targetModule.TypeSystem.String, Exception)
            {
                HasThis = true
            });

        ObjectCtor = targetModule.ImportReference(new MethodReference(".ctor", targetModule.TypeSystem.Void,
            targetModule.TypeSystem.Object)
        {
            HasThis = true
        });

        AsyncStateMachineAttribute = targetModule.GetCoreType<AsyncStateMachineAttribute>();

        CompilerGeneratedAttribute = targetModule.GetCoreType<CompilerGeneratedAttribute>();

        CompilerGeneratedAttributeCtor = new(".ctor", targetModule.TypeSystem.Void,
            CompilerGeneratedAttribute) { HasThis = true };

        KeyValuePair = new GenericInstanceType(targetModule.GetCoreType(typeof(KeyValuePair<,>)))
            { GenericArguments = { targetModule.TypeSystem.String, targetModule.TypeSystem.Object } };

        KeyValuePairCtor = targetModule.ImportReference(KeyValuePair.Resolve().GetConstructors().Single().MakeHostInstanceGeneric(KeyValuePair));

        ActivityAwaiterEmitter = new(this);
    }
}
