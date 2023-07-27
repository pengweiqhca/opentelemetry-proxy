using Mono.Cecil;
using Mono.Cecil.Cil;
using OpenTelemetry.StaticProxy.Fody;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Internal;

internal readonly struct CoercedAwaitableInfo
{
    public AwaitableInfo AwaitableInfo { get; }

    public IEnumerable<Instruction>? CoercerExpression { get; }

    public CoercedAwaitableInfo(AwaitableInfo awaitableInfo) => AwaitableInfo = awaitableInfo;

    public CoercedAwaitableInfo(IEnumerable<Instruction> coercerExpression, AwaitableInfo coercedAwaitableInfo)
        : this(coercedAwaitableInfo) => CoercerExpression = coercerExpression;

    public static bool IsTypeAwaitable(TypeReference type, out CoercedAwaitableInfo info)
    {
        if (type.HaveSameIdentity(type.Module.TypeSystem.Void))
        {
            info = default;
            return false;
        }

        if (AwaitableInfo.IsTypeAwaitable(type, out var directlyAwaitableInfo))
        {
            info = new(directlyAwaitableInfo);
            return true;
        }

        // It's not directly awaitable, but maybe we can coerce it.
        // Currently we support coercing FSharpAsync<T>.
        if (ObjectMethodExecutorFSharpSupport.TryBuildCoercerFromFSharpAsyncToAwaitable(type,
                out var coercerExpression, out var coercerResultType) &&
            AwaitableInfo.IsTypeAwaitable(coercerResultType, out var coercedAwaitableInfo))
        {
            info = new(coercerExpression, coercedAwaitableInfo);
            return true;
        }

        info = default;
        return false;
    }
}
