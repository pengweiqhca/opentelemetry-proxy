using Microsoft.FSharp.Control;
using OpenTelemetry.Proxy.Tests.Common;
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Internal;

public class CoercedAwaitableInfoTest
{
    [Theory]
    [InlineData(typeof(List<string>), false, typeof(void), false)]
    [InlineData(typeof(object), false, typeof(void), false)]
    [InlineData(typeof(int), false, typeof(void), false)]
    [InlineData(typeof(Task), true, typeof(TaskAwaiter), true)]
    [InlineData(typeof(Task<string>), true, typeof(TaskAwaiter<string>), true)]
    [InlineData(typeof(ValueTask), true, typeof(ValueTaskAwaiter), true)]
    [InlineData(typeof(ValueTask<string>), true, typeof(ValueTaskAwaiter<string>), true)]
    [InlineData(typeof(TestAwaitable<string>), true, typeof(TestAwaitable<string>.TestAwaiter), false)]
    [InlineData(typeof(TestAwaitable), true, typeof(TestAwaitable.TestAwaiter<int>), false)]
    [InlineData(typeof(TestAwaitableWithICriticalNotifyCompletion), true,
        typeof(TestAwaiterWithICriticalNotifyCompletion), true)]
    [InlineData(typeof(TestAwaitableWithoutICriticalNotifyCompletion), true,
        typeof(TestAwaiterWithoutICriticalNotifyCompletion), false)]
    public void AwaitableType(Type type, bool isAwaitable, Type awaiterType, bool hasUnsafeOnCompleted)
    {
        if (!isAwaitable)
        {
            Assert.False(CoercedAwaitableInfo.IsTypeAwaitable(type, out _));

            return;
        }

        Assert.True(CoercedAwaitableInfo.IsTypeAwaitable(type, out var awaitableInfo));

        AssertAwaitableInfo(awaitableInfo.AwaitableInfo, awaiterType, hasUnsafeOnCompleted);

        Assert.Null(awaitableInfo.CoercerExpression);
    }

    [Theory]
    [InlineData(typeof(FSharpAsync<string>), typeof(TaskAwaiter<string>))]
    public void FSharpAsync(Type type, Type awaiterType)
    {
        Assert.True(CoercedAwaitableInfo.IsTypeAwaitable(type, out var awaitableInfo));

        AssertAwaitableInfo(awaitableInfo.AwaitableInfo, awaiterType, true);

        Assert.NotNull(awaitableInfo.CoercerExpression);
    }

    private static void AssertAwaitableInfo(AwaitableInfo awaitableInfo, Type awaiterType, bool hasUnsafeOnCompleted)
    {
        Assert.NotNull(awaitableInfo.AwaitableType);
        Assert.Equal(awaiterType, awaitableInfo.AwaiterType);
        Assert.NotNull(awaitableInfo.AwaiterIsCompletedPropertyGetMethod);
        Assert.NotNull(awaitableInfo.AwaiterGetResultMethod);
        Assert.NotNull(awaitableInfo.AwaiterOnCompletedMethod);
        Assert.Equal(hasUnsafeOnCompleted, awaitableInfo.AwaiterUnsafeOnCompletedMethod != null);
        Assert.NotNull(awaitableInfo.GetAwaiterMethod);
    }
}
