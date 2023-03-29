using Mono.Cecil;
using OpenTelemetry.StaticProxy.Fody;
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Internal;

internal readonly struct AwaitableInfo
{
    public TypeReference AwaitableType { get; }

    public TypeReference AwaiterType { get; }

    public MethodDefinition AwaiterIsCompletedPropertyGetMethod { get; }

    public MethodDefinition AwaiterGetResultMethod { get; }

    public MethodDefinition AwaiterOnCompletedMethod { get; }

    public MethodDefinition? AwaiterUnsafeOnCompletedMethod { get; }

    public MethodDefinition GetAwaiterMethod { get; }

    private AwaitableInfo(TypeReference awaitableType,
        TypeReference awaiterType,
        MethodDefinition awaiterIsCompletedPropertyGetMethod,
        MethodDefinition awaiterGetResultMethod,
        MethodDefinition awaiterOnCompletedMethod,
        MethodDefinition? awaiterUnsafeOnCompletedMethod,
        MethodDefinition getAwaiterMethod)
    {
        AwaitableType = awaitableType;
        AwaiterType = awaiterType.MakeHostInstanceGeneric(awaitableType);
        AwaiterIsCompletedPropertyGetMethod = awaiterIsCompletedPropertyGetMethod;
        AwaiterGetResultMethod = awaiterGetResultMethod;
        AwaiterOnCompletedMethod = awaiterOnCompletedMethod;
        AwaiterUnsafeOnCompletedMethod = awaiterUnsafeOnCompletedMethod;
        GetAwaiterMethod = getAwaiterMethod;
    }

    public static bool IsTypeAwaitable(TypeReference type, out AwaitableInfo awaitableInfo)
    {
        // Based on Roslyn code: http://source.roslyn.io/#Microsoft.CodeAnalysis.Workspaces/Shared/Extensions/ISymbolExtensions.cs,db4d48ba694b9347

        // Awaitable must have method matching "object GetAwaiter()"
        var getAwaiterMethod = type.Resolve().GetParameterlessMethod(nameof(Task.GetAwaiter));
        if (getAwaiterMethod is null)
        {
            awaitableInfo = default;
            return false;
        }

        var awaiterType = getAwaiterMethod.ReturnType.Resolve();

        // Awaiter must have property matching "bool IsCompleted { get; }"
        var isCompletedProperty = awaiterType.GetProperty(nameof(TaskAwaiter.IsCompleted));
        if (isCompletedProperty?.GetMethod is null)
        {
            awaitableInfo = default;
            return false;
        }

        // Awaiter must implement INotifyCompletion
        var implementsINotifyCompletion = awaiterType.Interfaces.Any(ii =>
            ii.InterfaceType.HaveSameIdentity(awaiterType.Module.GetCoreType<INotifyCompletion>()));

        if (!implementsINotifyCompletion)
        {
            awaitableInfo = default;
            return false;
        }

        // INotifyCompletion supplies a method matching "void OnCompleted(Action action)"
        var onCompletedMethod = /*OnCompleted*/awaiterType.GetMethods(nameof(INotifyCompletion.OnCompleted))
            .FirstOrDefault(m =>
                m.Parameters.Count == 1 && m.Parameters[0].ParameterType
                    .HaveSameIdentity(awaiterType.Module.GetCoreType<Action>()));

        if (onCompletedMethod == null)
        {
            awaitableInfo = default;
            return false;
        }

        // Awaiter optionally implements ICriticalNotifyCompletion
        var implementsICriticalNotifyCompletion = awaiterType.Interfaces.Any(ii =>
            ii.InterfaceType.HaveSameIdentity(awaiterType.Module.GetCoreType<ICriticalNotifyCompletion>()));

        MethodDefinition? unsafeOnCompletedMethod = null;
        if (implementsICriticalNotifyCompletion)
            // ICriticalNotifyCompletion supplies a method matching "void UnsafeOnCompleted(Action action)"
            unsafeOnCompletedMethod = /*UnsafeOnCompleted*/awaiterType
                .GetMethods(nameof(ICriticalNotifyCompletion.UnsafeOnCompleted))
                .FirstOrDefault(m => m.Parameters.Count == 1 && m.Parameters[0].ParameterType
                    .HaveSameIdentity(awaiterType.Module.GetCoreType<Action>()));

        // Awaiter must have method matching "void GetResult" or "T GetResult()"
        var getResultMethod = awaiterType.GetParameterlessMethod(nameof(TaskAwaiter.GetResult));
        if (getResultMethod is null)
        {
            awaitableInfo = default;
            return false;
        }

        awaitableInfo = new(type,
            getAwaiterMethod.ReturnType,
            isCompletedProperty.GetMethod,
            getResultMethod,
            onCompletedMethod,
            unsafeOnCompletedMethod,
            getAwaiterMethod);

        return true;
    }
}
