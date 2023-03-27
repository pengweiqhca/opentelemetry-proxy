using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Diagnostics.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Internal;

internal readonly struct CoercedAwaitableInfo
{
    public AwaitableInfo AwaitableInfo { get; }
    public IEnumerable<Instruction>? CoercerExpression { get; }

    [MemberNotNullWhen(true, nameof(CoercerExpression))]
    public bool RequiresCoercion => CoercerExpression != null;

    public CoercedAwaitableInfo(AwaitableInfo awaitableInfo)
    {
        AwaitableInfo = awaitableInfo;
        CoercerExpression = null;
    }

    public CoercedAwaitableInfo(IEnumerable<Instruction> coercerExpression, AwaitableInfo coercedAwaitableInfo)
    {
        CoercerExpression = coercerExpression;
        AwaitableInfo = coercedAwaitableInfo;
    }

    public static bool IsTypeAwaitable(TypeReference type, out CoercedAwaitableInfo info)
    {
        if (type == type.Module.TypeSystem.Void)
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
