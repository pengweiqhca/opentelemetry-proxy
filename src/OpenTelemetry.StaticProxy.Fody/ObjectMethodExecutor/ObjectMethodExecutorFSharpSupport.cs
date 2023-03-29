using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OpenTelemetry.StaticProxy.Fody;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Internal;

/// <summary>
/// Helper for detecting whether a given type is FSharpAsync`1, and if so, supplying
/// an <see cref="Expression"/> for mapping instances of that type to a C# awaitable.
/// </summary>
/// <remarks>
/// The main design goal here is to avoid taking a compile-time dependency on
/// FSharp.Core.dll, because non-F# applications wouldn't use it. So all the references
/// to FSharp types have to be constructed dynamically at runtime.
/// </remarks>
internal static class ObjectMethodExecutorFSharpSupport
{
    private static readonly object FsharpValuesCacheLock = new();
    private static IMetadataScope? _fsharpCoreAssembly;
    private static MethodReference? _fsharpAsyncStartAsTask;
    private static MethodReference? _fsharpOptionOfTaskCreationOptionsNonePropertyGetMethod;
    private static MethodReference? _fsharpOptionOfCancellationTokenNonePropertyGetMethod;

    public static bool TryBuildCoercerFromFSharpAsyncToAwaitable(TypeReference possibleFSharpAsyncType,
        [NotNullWhen(true)] out IEnumerable<Instruction>? coerceToAwaitableExpression,
        [NotNullWhen(true)] out TypeReference? awaitableType)
    {
        if (possibleFSharpAsyncType is not GenericInstanceType genericInstanceType ||
            !IsFSharpAsyncOpenGenericType(genericInstanceType.ElementType))
        {
            coerceToAwaitableExpression = null;
            awaitableType = null;
            return false;
        }

        var awaiterResultType = genericInstanceType.GenericArguments.Single();
        awaitableType = new GenericInstanceType(possibleFSharpAsyncType.Module.GetCoreType(typeof(Task<>)))
        {
            GenericArguments = { awaiterResultType }
        };

        /*Task<TResult> task = FSharpAsync.StartAsTask<TResult>(
            (Microsoft.FSharp.Control.FSharpAsync<TResult>)fsharpAsync,
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);*/
        coerceToAwaitableExpression = new[]
        {
            Instruction.Create(OpCodes.Call, possibleFSharpAsyncType.Module
                .ImportReference(_fsharpOptionOfTaskCreationOptionsNonePropertyGetMethod)),
            Instruction.Create(OpCodes.Call, possibleFSharpAsyncType.Module
                .ImportReference(_fsharpOptionOfCancellationTokenNonePropertyGetMethod)),
            Instruction.Create(OpCodes.Call, possibleFSharpAsyncType.Module.ImportReference(
                new GenericInstanceMethod(_fsharpAsyncStartAsTask) { GenericArguments = { awaiterResultType } }))
        };

        return true;
    }

    [MemberNotNullWhen(true, nameof(_fsharpAsyncStartAsTask), nameof(_fsharpCoreAssembly),
        nameof(_fsharpOptionOfTaskCreationOptionsNonePropertyGetMethod),
        nameof(_fsharpOptionOfCancellationTokenNonePropertyGetMethod))]
    private static bool IsFSharpAsyncOpenGenericType(TypeReference? possibleFSharpAsyncGenericType)
    {
        if (possibleFSharpAsyncGenericType == null || !string.Equals(possibleFSharpAsyncGenericType.FullName,
                "Microsoft.FSharp.Control.FSharpAsync`1", StringComparison.Ordinal)) return false;

        lock (FsharpValuesCacheLock)
            return _fsharpCoreAssembly == null
                // We'll keep trying to find the FSharp types/values each time any type called FSharpAsync`1 is supplied.
                ? TryPopulateFSharpValueCaches(possibleFSharpAsyncGenericType)
                // Since we've already found the real FSharpAsync.Core assembly, we just have to check that the supplied FSharpAsync`1 type is the one from that assembly.
                : possibleFSharpAsyncGenericType.Scope.HaveSameIdentity(_fsharpCoreAssembly);
    }

    private static bool TryPopulateFSharpValueCaches(TypeReference possibleFSharpAsyncGenericType)
    {
        if (!possibleFSharpAsyncGenericType.Scope.IsFSharpCore()) return false;

        _fsharpAsyncStartAsTask = new TypeReference(
                "Microsoft.FSharp.Control", "FSharpAsync",
                possibleFSharpAsyncGenericType.Module, possibleFSharpAsyncGenericType.Scope).Resolve().GetMethods()
            .Single(static m => m.Name == "StartAsTask" && m.Parameters.Count == 3);

        var fsharpOptionType = new TypeReference("Microsoft.FSharp.Core", "FSharpOption`1",
            possibleFSharpAsyncGenericType.Module, possibleFSharpAsyncGenericType.Scope);

        // Get a reference to FSharpOption<TaskCreationOptions>.None
        _fsharpOptionOfTaskCreationOptionsNonePropertyGetMethod = GetNone(fsharpOptionType,
            possibleFSharpAsyncGenericType.Module.GetCoreType<TaskCreationOptions>());

        // Get a reference to FSharpOption<CancellationToken>.None
        _fsharpOptionOfCancellationTokenNonePropertyGetMethod = GetNone(fsharpOptionType,
            possibleFSharpAsyncGenericType.Module.GetCoreType<CancellationToken>());

        _fsharpCoreAssembly = possibleFSharpAsyncGenericType.Scope;

        return true;
    }

    private static MethodReference GetNone(TypeReference fSharpOptionType, TypeReference returnType) =>
        fSharpOptionType.Resolve().GetParameterlessMethod("get_None")!
            .MakeHostInstanceGeneric(new GenericInstanceType(fSharpOptionType) { GenericArguments = { returnType } });
}
