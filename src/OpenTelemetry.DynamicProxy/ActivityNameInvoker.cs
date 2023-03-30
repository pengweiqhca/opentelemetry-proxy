using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Reflection.Emit;

namespace OpenTelemetry.DynamicProxy;

public class ActivityNameInvoker : IActivityInvoker
{
    private readonly bool _suppressInstrumentation;
    private readonly string? _activityName;
    private readonly int _maxUsableTimes;
    private readonly Func<IInvocation, IReadOnlyCollection<KeyValuePair<string, object?>>?>? _getTags;

    public ActivityNameInvoker(bool suppressInstrumentation) => _suppressInstrumentation = suppressInstrumentation;

    public ActivityNameInvoker(string activityName, int maxUsableTimes,
        Func<IInvocation, IReadOnlyCollection<KeyValuePair<string, object?>>?> getTags)
    {
        _activityName = activityName;
        _maxUsableTimes = maxUsableTimes;
        _getTags = getTags;
    }

    public void Invoke(IInvocation invocation)
    {
        var disposable = _suppressInstrumentation
            ? SuppressInstrumentationScope.Begin()
            : ActivityName.SetName(_getTags?.Invoke(invocation), _activityName, _maxUsableTimes);

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

    protected virtual void InvokeAfter(IInvocation invocation, IDisposable disposable) => disposable.Dispose();

    internal static void BuildAwaitableActivityNameInvoker(TypeBuilder tb, Type returnType, CoercedAwaitableInfo info)
    {
        var invocationField = tb.DefineField("_invocation", typeof(IInvocation), FieldAttributes.Private);
        var disposableField = tb.DefineField("_disposable", typeof(IDisposable), FieldAttributes.Private);

        #region ctor

        var ctor = tb.DefineConstructor(MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(bool) });

        ctor.DefineParameter(0, ParameterAttributes.None, "suppressInstrumentation");

        var il = ctor.GetILGenerator();

        // base(suppressInstrumentation)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(ActivityNameInvoker).GetConstructors().Single(c => c.GetParameters().Length == 1));
        il.Emit(OpCodes.Ret);

        ctor = tb.DefineConstructor(MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard, new[]
            {
                typeof(string), typeof(int),
                typeof(Func<IInvocation, IReadOnlyCollection<KeyValuePair<string, object?>>?>)
            });

        ctor.DefineParameter(0, ParameterAttributes.None, "activityName");
        ctor.DefineParameter(1, ParameterAttributes.None, "maxUsableTimes");
        ctor.DefineParameter(2, ParameterAttributes.None, "getTags");

        il = ctor.GetILGenerator();

        // base(activityName, maxUsableTimes, getTags)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, typeof(ActivityNameInvoker).GetConstructors().Single(c => c.GetParameters().Length == 3));
        il.Emit(OpCodes.Ret);

        #endregion

        #region OnCompleted

        var baseInvokeAfter =
            typeof(ActivityNameInvoker).GetMethod(nameof(InvokeAfter), BindingFlags.NonPublic | BindingFlags.Instance)!;

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

        var invokeAfter = tb.DefineMethod(nameof(InvokeAfter),
            MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.Virtual, typeof(void),
            new[] { typeof(IInvocation), typeof(IDisposable) });

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
        il.Emit(info.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call,
            info.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod);

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
