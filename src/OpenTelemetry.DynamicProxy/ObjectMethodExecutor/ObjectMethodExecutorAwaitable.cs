using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Microsoft.Extensions.Internal;

/// <summary>
/// Provides a common awaitable structure that <see cref="ObjectMethodExecutor.ExecuteAsync"/> can
/// return, regardless of whether the underlying value is a System.Task, an FSharpAsync, or an
/// application-defined custom awaitable.
/// </summary>
internal readonly struct ObjectMethodExecutorAwaitable(object customAwaitable,
    Func<object, object> getAwaiterMethod,
    Func<object, bool> isCompletedMethod,
    Func<object, object> getResultMethod,
    Action<object, Action> onCompletedMethod,
    Action<object, Action> unsafeOnCompletedMethod)
{
    public Awaiter GetAwaiter() => new(getAwaiterMethod(customAwaitable), isCompletedMethod, getResultMethod,
        onCompletedMethod, unsafeOnCompletedMethod);

    public readonly struct Awaiter(object customAwaiter,
        Func<object, bool> isCompletedMethod,
        Func<object, object> getResultMethod,
        Action<object, Action> onCompletedMethod,
        Action<object, Action>? unsafeOnCompletedMethod) : ICriticalNotifyCompletion
    {
        [DebuggerHidden]
        public bool IsCompleted => isCompletedMethod(customAwaiter);

        [DebuggerHidden]
        public object GetResult() => getResultMethod(customAwaiter);

        [DebuggerHidden]
        public void OnCompleted(Action continuation) => onCompletedMethod(customAwaiter, continuation);

        [DebuggerHidden]
        public void UnsafeOnCompleted(Action continuation) =>

            // If the underlying awaitable implements ICriticalNotifyCompletion, use its UnsafeOnCompleted.
            // If not, fall back on using its OnCompleted.
            //
            // Why this is safe:
            // - Implementing ICriticalNotifyCompletion is a way of saying the caller can choose whether it
            //   needs the execution context to be preserved (which it signals by calling OnCompleted), or
            //   that it doesn't (which it signals by calling UnsafeOnCompleted). Obviously it's faster *not*
            //   to preserve and restore the context, so we prefer that where possible.
            // - If a caller doesn't need the execution context to be preserved and hence calls UnsafeOnCompleted,
            //   there's no harm in preserving it anyway - it's just a bit of wasted cost. That's what will happen
            //   if a caller sees that the proxy implements ICriticalNotifyCompletion but the proxy chooses to
            //   pass the call on to the underlying awaitable's OnCompleted method.
            (unsafeOnCompletedMethod ?? onCompletedMethod)(customAwaiter, continuation);
    }
}
