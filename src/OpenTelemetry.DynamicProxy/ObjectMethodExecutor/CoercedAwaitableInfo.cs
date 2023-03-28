using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection.Emit;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Internal;

internal readonly struct CoercedAwaitableInfo
{
    public AwaitableInfo AwaitableInfo { get; }

    public Action<ILGenerator>? CoercerExpression { get; }

    public CoercedAwaitableInfo(AwaitableInfo awaitableInfo) => AwaitableInfo = awaitableInfo;

    public CoercedAwaitableInfo(Action<ILGenerator> coercerExpression, AwaitableInfo coercedAwaitableInfo)
        : this(coercedAwaitableInfo) => CoercerExpression = coercerExpression;

    public static bool IsTypeAwaitable(Type type, out CoercedAwaitableInfo info)
    {
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
