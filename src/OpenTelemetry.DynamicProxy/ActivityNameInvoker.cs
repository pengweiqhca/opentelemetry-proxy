using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Reflection.Emit;

namespace OpenTelemetry.DynamicProxy;

public class ActivityNameInvoker : IActivityInvoker
{
    private readonly string? _activityName;
    private readonly int _maxUsableTimes;
    private readonly bool _suppressInstrumentation;

    public ActivityNameInvoker(string? activityName, int maxUsableTimes, bool suppressInstrumentation)
    {
        _activityName = activityName;
        _maxUsableTimes = maxUsableTimes;
        _suppressInstrumentation = suppressInstrumentation;
    }

    public void Invoke(IInvocation invocation)
    {
        if (_suppressInstrumentation)
        {
            var disposable = SuppressInstrumentationScope.Begin();

            try
            {
                invocation.Proceed();
            }
            catch
            {
                disposable.Dispose();

                throw;
            }

            InvokeAfter(invocation, disposable);
        }
        else
        {
            ActivityName.SetName(string.IsNullOrWhiteSpace(_activityName) ? $"{invocation.TargetType.FullName}.{invocation.Method.Name}" : _activityName, _maxUsableTimes);

            try
            {
                invocation.Proceed();
            }
            catch
            {
                ActivityName.Clear();

                throw;
            }

            InvokeAfter(invocation, null);
        }
    }

    protected virtual void InvokeAfter(IInvocation invocation, IDisposable? disposable)
    {
        if (disposable == null) ActivityName.Clear();
        else disposable.Dispose();
    }

    internal static void BuildAwaitableActivityNameInvoker(TypeBuilder tb, Type returnType, CoercedAwaitableInfo info)
    {
        var invocationField = tb.DefineField("_invocation", typeof(IInvocation), FieldAttributes.Private);
        var disposableField = tb.DefineField("_disposable", typeof(IDisposable), FieldAttributes.Private);

        #region ctor

        var ctor = tb.DefineConstructor(MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(string), typeof(int), typeof(bool) });

        ctor.DefineParameter(0, ParameterAttributes.None, "activityName");
        ctor.DefineParameter(1, ParameterAttributes.None, "maxUsableTimes");
        ctor.DefineParameter(2, ParameterAttributes.None, "suppressInstrumentation");

        var il = ctor.GetILGenerator();

        // base(activityName, maxUsableTimes, suppressInstrumentation)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, typeof(ActivityNameInvoker).GetConstructors()[0]);
        il.Emit(OpCodes.Ret);
        #endregion

        #region OnCompleted
        var baseInvokeAfter = typeof(ActivityNameInvoker).GetMethod(nameof(InvokeAfter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        var onCompleted = tb.DefineMethod("OnCompleted", MethodAttributes.Private | MethodAttributes.HideBySig);
        il = onCompleted.GetILGenerator();

        // base.InvokeAfter(_invocation, _disposable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, invocationField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, disposableField);
        il.Emit(OpCodes.Call, baseInvokeAfter);
        il.Emit(OpCodes.Ret);
        #endregion

        #region InvokeAfter
        var invokeAfter = tb.DefineMethod(nameof(InvokeAfter), MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.Virtual, typeof(void), new[] { typeof(IInvocation), typeof(IDisposable) });
        tb.DefineMethodOverride(invokeAfter, baseInvokeAfter);

        il = invokeAfter.GetILGenerator();
        var awaiter = il.DeclareLocal(info.AwaitableInfo.AwaiterType);

        // _invocation = invocation;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, invocationField);

        // _disposable = disposable;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, disposableField);

        // var awaiter = ((AwaitableType)invocation.ReturnValue).GetAwaiter();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(IInvocation).GetProperty(nameof(IInvocation.ReturnValue))!.GetMethod!);
        il.Emit(returnType.IsClass ? OpCodes.Castclass : OpCodes.Unbox_Any, returnType);
        info.CoercerExpression?.Invoke(il);
        il.Emit(info.AwaitableInfo.GetAwaiterMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call,
            info.AwaitableInfo.GetAwaiterMethod);
        il.Emit(OpCodes.Stloc_0);

        // if (awaiter.IsCompleted)
        il.Emit(info.AwaitableInfo.AwaiterType.IsValueType ? OpCodes.Ldloca_S : OpCodes.Ldloc, awaiter);
        il.Emit(info.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, info.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod);
        var falseLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse_S, falseLabel);

        // OnCompleted();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, onCompleted);
        il.Emit(OpCodes.Ret);

        // else awaiter.OnCompleted(OnCompleted);
        il.MarkLabel(falseLabel);
        il.Emit(info.AwaitableInfo.AwaiterType.IsValueType ? OpCodes.Ldloca_S : OpCodes.Ldloc, awaiter);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, onCompleted);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructors()[0]);

        var awaiterOnCompleted = info.AwaitableInfo.AwaiterUnsafeOnCompletedMethod ??
            info.AwaitableInfo.AwaiterOnCompletedMethod;

        il.Emit(awaiterOnCompleted.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, awaiterOnCompleted);
        il.Emit(OpCodes.Ret);
        #endregion
    }
}
