using Microsoft.Extensions.Internal;
using OpenTelemetry.Trace;
using System.Reflection.Emit;

namespace OpenTelemetry.DynamicProxy;

public class ActivityInvoker : IActivityInvoker
{
    private readonly ActivitySource _activitySource;

    private readonly ActivityKind _kind;

    private readonly string? _activityName;

    public ActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
    {
        _activitySource = activitySource;

        _kind = kind;

        _activityName = activityName;
    }

    public void Invoke(IInvocation invocation)
    {
        if (_activitySource.StartActivity(string.IsNullOrWhiteSpace(_activityName)
                ? $"{_activitySource.Name}.{invocation.Method.Name}"
                : _activityName!, _kind) is not { } activity)
        {
            invocation.Proceed();

            return;
        }

        try
        {
            invocation.Proceed();

            Activity.Current = activity.Parent;
        }
        catch (Exception ex)
        {
            OnException(activity, ex);

            throw;
        }

        InvokeAfter(invocation, activity);
    }

    protected static void OnException(Activity activity, Exception ex)
    {
        activity.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

        activity.Dispose();
    }

    protected virtual void InvokeAfter(IInvocation invocation, Activity activity) => activity.Dispose();

    internal static void BuildAwaitableActivityInvoker(TypeBuilder tb, Type returnType, CoercedAwaitableInfo info)
    {
        var awaiterField = tb.DefineField("_awaiter", info.AwaitableInfo.AwaiterType, FieldAttributes.Private);
        var activityField = tb.DefineField("_activity", typeof(Activity), FieldAttributes.Private);

        #region ctor

        var ctor = tb.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            new[]
            {
                typeof(ActivitySource), typeof(string), typeof(ActivityKind)
            });

        ctor.DefineParameter(0, ParameterAttributes.None, "activitySource");
        ctor.DefineParameter(1, ParameterAttributes.None, "activityName");
        ctor.DefineParameter(2, ParameterAttributes.None, "kind");

        var il = ctor.GetILGenerator();

        // base(activitySource, activityName, kind)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, typeof(ActivityInvoker).GetConstructors()[0]);
        il.Emit(OpCodes.Ret);

        #endregion

        #region OnCompleted

        var onCompleted = tb.DefineMethod("OnCompleted", MethodAttributes.Private | MethodAttributes.HideBySig);
        il = onCompleted.GetILGenerator();
        il.DeclareLocal(typeof(Exception));

        /*try
        {
            _awaiter.GetResult();

            _activity.Dispose();
        }*/
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, awaiterField);
        il.Emit(info.AwaitableInfo.AwaiterGetResultMethod.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
            info.AwaitableInfo.AwaiterGetResultMethod);

        if (info.AwaitableInfo.AwaiterGetResultMethod.ReturnType != typeof(void)) il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, activityField);
        il.Emit(OpCodes.Callvirt, typeof(Activity).GetMethod(nameof(Activity.Dispose))!);

        var end = il.DefineLabel();
        //il.Emit(OpCodes.Leave_S, end);

        /*catch (Exception ex)
      {
        OnException(_activity, ex);
      }*/
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, activityField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Call,
            typeof(ActivityInvoker).GetMethod(nameof(OnException), BindingFlags.Static | BindingFlags.NonPublic)!);

        il.EndExceptionBlock();

        // Stop(_activity);
        il.MarkLabel(end);
        il.Emit(OpCodes.Ret);

        #endregion

        #region InvokeAfter

        var invokeAfter = tb.DefineMethod(nameof(InvokeAfter),
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, typeof(void), new[]
            {
                typeof(IInvocation), typeof(Activity)
            });

        tb.DefineMethodOverride(invokeAfter,
            typeof(ActivityInvoker).GetMethod(nameof(InvokeAfter), BindingFlags.Instance | BindingFlags.NonPublic)!);

        invokeAfter.DefineParameter(1, ParameterAttributes.None, "invocation");
        invokeAfter.DefineParameter(2, ParameterAttributes.None, "activity");

        il = invokeAfter.GetILGenerator();

        var awaiter = il.DeclareLocal(info.AwaitableInfo.AwaiterType);

        // var awaiter = ((AwaitableType)invocation.ReturnValue).GetAwaiter();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(IInvocation).GetProperty(nameof(IInvocation.ReturnValue))!.GetMethod!);
        il.Emit(returnType.IsClass ? OpCodes.Castclass : OpCodes.Unbox_Any, returnType);
        info.CoercerExpression?.Invoke(il);

        if (info.AwaitableInfo.AwaitableType.IsValueType)
        {
            var awaitable = il.DeclareLocal(info.AwaitableInfo.AwaitableType);

            il.Emit(OpCodes.Stloc_1);
            il.Emit(OpCodes.Ldloca_S, awaitable);
        }

        il.Emit(info.AwaitableInfo.GetAwaiterMethod.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
            info.AwaitableInfo.GetAwaiterMethod);

        il.Emit(OpCodes.Stloc_0);

        // _awaiter = awaiter;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Stfld, awaiterField);

        // _activity = activity;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, activityField);

        // if (awaiter.IsCompleted)
        il.Emit(info.AwaitableInfo.AwaiterType.IsValueType ? OpCodes.Ldloca_S : OpCodes.Ldloc, awaiter);
        il.Emit(info.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
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

        il.Emit(awaiterOnCompleted.IsFinal ? OpCodes.Call : OpCodes.Callvirt, awaiterOnCompleted);
        il.Emit(OpCodes.Ret);

        #endregion
    }
}
