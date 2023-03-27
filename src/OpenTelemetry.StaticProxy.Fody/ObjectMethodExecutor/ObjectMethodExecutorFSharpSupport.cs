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
    private static ModuleDefinition? _fsharpCoreAssembly;
    private static MethodReference? _fsharpAsyncStartAsTaskGenericMethod;
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
            GenericArguments =
            {
                awaiterResultType
            }
        };

        /*coerceToAwaitableExpression = (object fsharpAsync) => (object)FSharpAsync.StartAsTask<TResult>(
            (Microsoft.FSharp.Control.FSharpAsync<TResult>)fsharpAsync,
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);*/
        var startAsTaskClosedMethod = new GenericInstanceMethod(_fsharpAsyncStartAsTaskGenericMethod);

        startAsTaskClosedMethod.GenericArguments.Add(awaiterResultType);

        coerceToAwaitableExpression = new[]
        {
            Instruction.Create(OpCodes.Call, possibleFSharpAsyncType.Module.ImportReference(_fsharpOptionOfTaskCreationOptionsNonePropertyGetMethod)),
            Instruction.Create(OpCodes.Call, possibleFSharpAsyncType.Module.ImportReference(_fsharpOptionOfCancellationTokenNonePropertyGetMethod)),
            Instruction.Create(OpCodes.Call, possibleFSharpAsyncType.Module.ImportReference(startAsTaskClosedMethod))
        };

        return true;
    }

    [MemberNotNullWhen(true, nameof(_fsharpAsyncStartAsTaskGenericMethod), nameof(_fsharpCoreAssembly),
        nameof(_fsharpOptionOfTaskCreationOptionsNonePropertyGetMethod), nameof(_fsharpOptionOfCancellationTokenNonePropertyGetMethod))]
    private static bool IsFSharpAsyncOpenGenericType(TypeReference? possibleFSharpAsyncGenericType)
    {
        if (possibleFSharpAsyncGenericType == null || !string.Equals(possibleFSharpAsyncGenericType.FullName,
                "Microsoft.FSharp.Control.FSharpAsync`1", StringComparison.Ordinal)) return false;

        lock (FsharpValuesCacheLock)
            return _fsharpCoreAssembly != null
                // Since we've already found the real FSharpAsync.Core assembly, we just have to check that the supplied FSharpAsync`1 type is the one from that assembly.
                ? possibleFSharpAsyncGenericType.Module == _fsharpCoreAssembly
                // We'll keep trying to find the FSharp types/values each time any type called FSharpAsync`1 is supplied.
                : TryPopulateFSharpValueCaches(possibleFSharpAsyncGenericType);
    }

    private static bool TryPopulateFSharpValueCaches(TypeReference possibleFSharpAsyncGenericType)
    {
        var assembly = possibleFSharpAsyncGenericType.Module;
        var fsharpOptionType = assembly.GetType("Microsoft.FSharp.Core.FSharpOption`1");
        var fsharpAsyncType = assembly.GetType("Microsoft.FSharp.Control.FSharpAsync");

        if (fsharpOptionType == null || fsharpAsyncType == null) return false;

        // Get a reference to FSharpOption<TaskCreationOptions>.None
        var fsharpOptionOfTaskCreationOptionsType = fsharpOptionType.MakeGenericInstanceType(possibleFSharpAsyncGenericType.Module.GetCoreType<TaskCreationOptions>());
        _fsharpOptionOfTaskCreationOptionsNonePropertyGetMethod = new("get_None",
            possibleFSharpAsyncGenericType.Module.GetCoreType<TaskCreationOptions>(),
            fsharpOptionOfTaskCreationOptionsType);

        // Get a reference to FSharpOption<CancellationToken>.None
        var fsharpOptionOfCancellationTokenType = fsharpOptionType.MakeGenericInstanceType(possibleFSharpAsyncGenericType.Module.GetCoreType<CancellationToken>());
        _fsharpOptionOfCancellationTokenNonePropertyGetMethod = new("get_None",
            possibleFSharpAsyncGenericType.Module.GetCoreType<CancellationToken>(),
            fsharpOptionOfCancellationTokenType);

        // Get a reference to FSharpAsync.StartAsTask<>
        foreach (var candidateMethodInfo in fsharpAsyncType.GetMethods("StartAsTask"))
        {
            var parameters = candidateMethodInfo.Parameters;
            if (parameters.Count == 3 &&
                parameters[0].ParameterType.GetElementType().HaveSameIdentity(possibleFSharpAsyncGenericType) &&
                parameters[1].ParameterType.HaveSameIdentity(fsharpOptionOfTaskCreationOptionsType) &&
                parameters[2].ParameterType.HaveSameIdentity(fsharpOptionOfCancellationTokenType))
            {
                // This really does look like the correct method (and hence assembly).
                _fsharpAsyncStartAsTaskGenericMethod = candidateMethodInfo;
                _fsharpCoreAssembly = assembly;
                break;
            }
        }

        return _fsharpCoreAssembly != null;
    }
}
