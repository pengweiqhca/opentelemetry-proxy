using Microsoft.FSharp.Control;
using Mono.Cecil;
using OpenTelemetry.Proxy.Tests.Common;
using OpenTelemetry.StaticProxy.Fody;
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
    [InlineData(typeof(TestAwaitable), true, typeof(TestAwaitable.TestAwaiter<bool>), false)]
    [InlineData(typeof(TestAwaitableWithICriticalNotifyCompletion), true,
        typeof(TestAwaiterWithICriticalNotifyCompletion), true)]
    [InlineData(typeof(TestAwaitableWithoutICriticalNotifyCompletion), true,
        typeof(TestAwaiterWithoutICriticalNotifyCompletion), false)]
    public void AwaitableType(Type type, bool isAwaitable, Type awaiterType, bool hasUnsafeOnCompleted)
    {
        var typeRef = AssemblyDefinition.ReadAssembly(type.Assembly.Location).MainModule.ImportReference(type);

        if (!isAwaitable)
        {
            Assert.False(CoercedAwaitableInfo.IsTypeAwaitable(typeRef, out _));

            return;
        }

        Assert.True(CoercedAwaitableInfo.IsTypeAwaitable(typeRef, out var awaitableInfo));

        AssertAwaitableInfo(awaitableInfo.AwaitableInfo, type.IsGenericType,
            typeRef.Module.ImportReference(awaiterType), hasUnsafeOnCompleted);

        Assert.Null(awaitableInfo.CoercerExpression);
    }

    [Theory]
    [InlineData(typeof(FSharpAsync<string>), typeof(TaskAwaiter<string>))]
    public void FSharpAsync(Type type, Type awaiterType)
    {
        var typeRef = ModuleDefinition.ReadModule(type.Assembly.Location).ImportReference(type);

        Assert.True(CoercedAwaitableInfo.IsTypeAwaitable(typeRef, out var awaitableInfo));

        AssertAwaitableInfo(awaitableInfo.AwaitableInfo, type.IsGenericType,
            typeRef.Module.ImportReference(awaiterType), true);

        Assert.NotNull(awaitableInfo.CoercerExpression);
    }

    private static void AssertAwaitableInfo(AwaitableInfo awaitableInfo, bool isGeneric,
        TypeReference awaiterType, bool hasUnsafeOnCompleted)
    {
        if (!isGeneric) Assert.NotNull(awaitableInfo.AwaitableType);
        else Assert.IsType<GenericInstanceType>(awaitableInfo.AwaitableType);

        Assert.True(awaiterType.HaveSameIdentity(isGeneric
            ? Assert.IsType<GenericInstanceType>(awaitableInfo.AwaiterType)
            : awaitableInfo.AwaiterType));

        Assert.NotNull(awaitableInfo.AwaiterIsCompletedPropertyGetMethod);
        Assert.NotNull(awaitableInfo.AwaiterGetResultMethod);
        Assert.NotNull(awaitableInfo.AwaiterOnCompletedMethod);
        Assert.Equal(hasUnsafeOnCompleted, awaitableInfo.AwaiterUnsafeOnCompletedMethod != null);
        Assert.NotNull(awaitableInfo.GetAwaiterMethod);
    }
}
