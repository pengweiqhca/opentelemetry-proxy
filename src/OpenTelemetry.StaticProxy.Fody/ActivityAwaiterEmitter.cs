using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Text;

namespace OpenTelemetry.StaticProxy.Fody;

internal class ActivityAwaiterEmitter(EmitContext context)
{
    private static readonly string CoreLibKey = Guid.NewGuid().ToString();

    private readonly Dictionary<TypeReference, MethodDefinition> _awaiterTypes = new(new TypeReferenceComparer());

    public MethodReference GetActivityAwaiter(AwaitableInfo awaitableInfo, bool isVoid)
    {
        if (awaitableInfo.AwaiterType is GenericInstanceType git)
        {
            if (!_awaiterTypes.TryGetValue(git.ElementType, out var awaiter))
                _awaiterTypes[git.ElementType] = awaiter = CreateActivityAwaiter(git.ElementType, awaitableInfo, isVoid);

            return awaiter.MakeHostInstanceGeneric(
                awaiter.DeclaringType.MakeGenericInstanceType([.. git.GenericArguments]));
        }
        else
            return !_awaiterTypes.TryGetValue(awaitableInfo.AwaiterType, out var awaiter)
                ? _awaiterTypes[awaitableInfo.AwaiterType] =
                    CreateActivityAwaiter(awaitableInfo.AwaiterType, awaitableInfo, isVoid)
                : awaiter;
    }

    private MethodDefinition CreateActivityAwaiter(TypeReference awaiterType, AwaitableInfo awaitableInfo, bool isVoid)
    {
        awaiterType = context.TargetModule.ImportReference(awaiterType);

        var (type, ctor, onCompleted, completed) = CreateActivityAwaiter(awaiterType,
            context.TargetModule.ImportReference(awaitableInfo.AwaiterGetResultMethod), isVoid);

        var method = new MethodDefinition("OnCompleted",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static,
            context.TargetModule.TypeSystem.Void);

        foreach (var parameter in completed.Parameters) method.Parameters.Add(parameter);

        type.Methods.Add(method);

        if (awaiterType.HasGenericParameters)
            awaiterType = awaiterType.MakeGenericInstanceType([.. awaiterType.GenericParameters]);

        /*IL_0021: ldarga.s 1
        IL_0023: call instance bool valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::get_IsCompleted()*/
        method.Body.Instructions.Add(awaiterType.IsValueType
            ? Instruction.Create(OpCodes.Ldarga_S, method.Parameters[0])
            : Instruction.Create(OpCodes.Ldarg_0));

        method.Body.Instructions.Add(Instruction.Create(awaitableInfo.AwaiterType.IsValueType ||
            awaitableInfo.AwaiterIsCompletedPropertyGetMethod.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
            method.Module.ImportReference(awaitableInfo.AwaiterIsCompletedPropertyGetMethod)
                .MakeHostInstanceGeneric(completed.Parameters[0].ParameterType)));

        var brfalse = awaiterType.IsValueType ? Instruction.Create(OpCodes.Ldarga_S, method.Parameters[0]) : Instruction.Create(OpCodes.Ldarg_0);

        /*IL_0047: brfalse.s IL_0052

        IL_0049: ldloc.0
        IL_004a: ldloc.2
        IL_004b: call void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::OnCompleted(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0>)
        IL_0050: ret*/
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, brfalse));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        if (!isVoid) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Call,
            type.HasGenericParameters ? completed.MakeHostInstanceGeneric(awaiterType) : completed));

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        /*IL_0052: ldloca.s 2
IL_0054: ldloc.0
IL_0055: ldloc.2
IL_0056: newobj instance void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::.ctor(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0>)
IL_005b: ldftn instance void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::OnCompleted()
IL_0061: newobj instance void [mscorlib]System.Action::.ctor(object, native int)
IL_0066: call instance void valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::UnsafeOnCompleted(class [mscorlib]System.Action)

IL_006c: ret*/
        method.Body.Instructions.Add(brfalse);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        if (!isVoid) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_3));

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj,
            type.HasGenericParameters ? ctor.MakeHostInstanceGeneric(awaiterType) : ctor));

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldftn,
            type.HasGenericParameters ? onCompleted.MakeHostInstanceGeneric(awaiterType) : onCompleted));

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, context.ActionCtor));

        var awaiterOnCompleted = awaitableInfo.AwaiterUnsafeOnCompletedMethod ??
            awaitableInfo.AwaiterOnCompletedMethod;

        method.Body.Instructions.Add(Instruction.Create(
            awaitableInfo.AwaiterType.IsValueType || awaiterOnCompleted.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
            context.TargetModule.ImportReference(awaiterOnCompleted)
                .MakeHostInstanceGeneric(method.Parameters[0].ParameterType)));

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        method.Body.OptimizeMacros();

        return method;
    }

    private Tuple<TypeDefinition, MethodDefinition, MethodDefinition, MethodDefinition>
        CreateActivityAwaiter(TypeReference awaiterType, MethodReference getResult, bool isVoid)
    {
        var type = new TypeDefinition("", "@ActivityAwaiter@" + _awaiterTypes.Count,
            TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit)
        {
            IsBeforeFieldInit = true,
            BaseType = context.TargetModule.TypeSystem.Object
        };

        type.CustomAttributes.Add(new(context.CompilerGeneratedAttributeCtor));

        context.TargetModule.Types.Add(type);

        foreach (var genericParameter in awaiterType.GenericParameters) type.GenericParameters.Add(genericParameter);

        if (awaiterType.HasGenericParameters)
            awaiterType = awaiterType.MakeGenericInstanceType([.. awaiterType.GenericParameters]);

        #region field and method

        /*.field private initonly class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity _activity
    .field private initonly valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!T> _awaiter*/
        const FieldAttributes initOnly = FieldAttributes.Private | FieldAttributes.InitOnly;

        type.Fields.Add(new("_awaiter", initOnly, awaiterType));
        type.Fields.Add(new("_activity", initOnly, context.Activity));
        type.Fields.Add(new("_disposable", initOnly, context.Disposable));
        if (!isVoid) type.Fields.Add(new("_returnValueTagName", initOnly, context.TargetModule.TypeSystem.String));

        var type2 = context.TargetModule.ImportReference(type);
        if (type.HasGenericParameters)
            type2 = type.MakeGenericInstanceType([.. type.GenericParameters]);

        var activity = new FieldReference("_activity", context.Activity, type2);
        var disposable = new FieldReference("_disposable", context.Disposable, type2);
        var returnValueTagName = isVoid
            ? null
            : new FieldReference("_returnValueTagName", context.TargetModule.TypeSystem.String, type2);

        var parameters = new ParameterDefinition[isVoid ? 3 : 4];

        parameters[0] = new("awaiter", ParameterAttributes.None, awaiterType);
        parameters[1] = new("activity", ParameterAttributes.None, context.Activity);
        parameters[2] = new("disposable", ParameterAttributes.None, context.Disposable);
        if (!isVoid)
            parameters[3] = new("returnValueTagName", ParameterAttributes.None, context.TargetModule.TypeSystem.String);

        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName, context.TargetModule.TypeSystem.Void);

        Array.ForEach(parameters, ctor.Parameters.Add);

        type.Methods.Add(ctor);

        var onCompleted = new MethodDefinition("OnCompleted", MethodAttributes.Private | MethodAttributes.HideBySig,
            context.TargetModule.TypeSystem.Void);

        type.Methods.Add(onCompleted);

        var completed = new MethodDefinition("Completed",
            MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.Static,
            context.TargetModule.TypeSystem.Void);

        Array.ForEach(parameters, completed.Parameters.Add);

        type.Methods.Add(completed);

        #endregion

        #region ctor

        /*IL_0000: ldarg.0
    IL_0001: call instance void [mscorlib]System.Object::.ctor()
    IL_0006: ldarg.0
    IL_0007: ldarg.1
    IL_0008: stfld class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<!T>::_activity
    IL_000d: ldarg.0
    IL_000e: ldarg.2
    IL_000f: stfld valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0> class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<!T>::_awaiter

    IL_0027: ret*/
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, context.ObjectCtor));

        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, context.TargetModule.ImportReference(new FieldReference("_awaiter", awaiterType, type2), ctor)));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, activity));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, disposable));
        if (returnValueTagName != null)
        {
            ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_S, ctor.Parameters[3]));
            ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, returnValueTagName));
        }

        /*IL_001b: ldarg.2
          IL_001c: brfalse.s IL_0029

          IL_001e: ldarg.2
          IL_001f: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::get_Parent()
          IL_0024: call void [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::set_Current(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity)

          IL_0029: ret*/
        var ret = Instruction.Create(OpCodes.Ret);
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, ret));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, context.ActivityGetParent));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, context.ActivitySetCurrent));
        ctor.Body.Instructions.Add(ret);

        #endregion

        #region OnCompleted

        /*IL_0000: ldarg.0
        IL_0001: ldfld class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<!T>::_activity
        IL_0006: ldarg.0
        IL_0007: ldfld valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0> class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<!T>::_awaiter
        IL_000c: call void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<!T>::OnCompleted(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0>)
        IL_0011: ret*/
        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, context.TargetModule.ImportReference(new FieldReference("_awaiter", awaiterType, type2), onCompleted)));

        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, activity));
        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, disposable));

        if (returnValueTagName != null)
        {
            onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, returnValueTagName));
        }

        var completedMethod = new MethodReference("Completed", context.TargetModule.TypeSystem.Void, type2)
            { HasThis = false };

        Array.ForEach(parameters, completedMethod.Parameters.Add);

        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Call, completedMethod));

        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        #endregion

        #region static Completed

        completed.Body.InitLocals = true;
        completed.Body.Variables.Add(new(context.Exception));

        var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = awaiterType.IsValueType
                ? Instruction.Create(OpCodes.Ldarga, completed.Parameters[0])
                : Instruction.Create(OpCodes.Ldarg_0),
            HandlerStart = Instruction.Create(OpCodes.Ldarg_2),
            HandlerEnd = Instruction.Create(OpCodes.Ret)
        };

        finallyHandler.TryEnd = finallyHandler.HandlerStart;

        var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = finallyHandler.TryStart,
            CatchType = context.Exception,
            HandlerStart = Instruction.Create(OpCodes.Stloc_0),
            HandlerEnd = finallyHandler.HandlerStart
        };

        catchHandler.TryEnd = catchHandler.HandlerStart;

        completed.Body.ExceptionHandlers.Add(catchHandler);
        completed.Body.ExceptionHandlers.Add(finallyHandler);

        /*IL_0000: ldarga.s awaiter
          IL_0002: call instance object Microsoft.Extensions.Internal.ObjectMethodExecutorAwaitable/Awaiter::GetResult()
          IL_0007: stloc.0
          IL_0008: ldarg.3
          IL_0009: brfalse.s IL_0014

          IL_000b: ldarg.0
          IL_000c: ldarg.3
          IL_000d: ldloc.0
          IL_000e: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::SetTag(string, object)
          IL_0013: pop

          IL_0014: leave.s IL_0027*/
        completed.Body.Instructions.Add(finallyHandler.TryStart);

        completed.Body.Instructions.Add(Instruction.Create(
            awaiterType.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
            getResult.MakeHostInstanceGeneric(awaiterType)));

        var handlerEnd = Instruction.Create(OpCodes.Leave, finallyHandler.HandlerEnd);
        if (!isVoid)
        {
            completed.Body.Variables.Add(new(getResult.ReturnType));

            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Stloc_1));
            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, handlerEnd));

            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc_1));
            if (getResult.ReturnType.IsValueType || getResult.ReturnType.IsGenericParameter)
                completed.Body.Instructions.Add(Instruction.Create(OpCodes.Box, getResult.ReturnType));

            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Call, context.ActivitySetTagEnumerable));
            completed.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        }

        completed.Body.Instructions.Add(handlerEnd);

        /*IL_0009: stloc.0
          IL_000a: ldarg.1
          IL_000b: brfalse.s IL_0020

          IL_000d: ldarg.1
          IL_000e: ldc.i4.2
          IL_000f: ldloc.0
          IL_0010: callvirt instance string [mscorlib]System.Exception::get_Message()
          IL_0015: call instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::SetStatus(valuetype [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivityStatusCode, string)
          IL_001a: ldloc.0
          IL_001b: call void [OpenTelemetry.Api]OpenTelemetry.Trace.ActivityExtensions::RecordException(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, class [mscorlib]System.Exception)

          IL_0020: leave.s IL_0035*/
        var leave = Instruction.Create(OpCodes.Leave, finallyHandler.HandlerEnd);

        completed.Body.Instructions.Add(catchHandler.HandlerStart);
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, leave));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, context.GetMessage));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Call, context.ActivitySetStatus));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Call, context.RecordException));
        completed.Body.Instructions.Add(leave);

        /*IL_0022: ldarg.2
          IL_0023: brfalse.s IL_002b

          IL_0025: ldarg.2
          IL_0026: callvirt instance void [mscorlib]System.IDisposable::Dispose()

          IL_002b: ldarg.1
          IL_002c: brfalse.s IL_0034

          IL_002e: ldarg.1
          IL_002f: call instance void [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::Dispose()

          IL_0034: endfinally*/
        var ldActivity = Instruction.Create(OpCodes.Ldarg_1);
        var end = Instruction.Create(OpCodes.Endfinally);

        completed.Body.Instructions.Add(finallyHandler.HandlerStart);
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, ldActivity));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, context.Dispose));

        completed.Body.Instructions.Add(ldActivity);
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, end));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        completed.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, context.ActivityDispose));
        completed.Body.Instructions.Add(end);

        completed.Body.Instructions.Add(finallyHandler.HandlerEnd);

        #endregion

        ctor.Body.OptimizeMacros();
        onCompleted.Body.OptimizeMacros();
        completed.Body.OptimizeMacros();

        return Tuple.Create(type, ctor, onCompleted, completed);
    }

    private sealed class TypeReferenceComparer : IEqualityComparer<TypeReference>
    {
        public bool Equals(TypeReference? x, TypeReference? y) =>
            x is null ? y is null : y is not null && GetName(x) == GetName(y);

        private static string GetName(TypeReference type)
        {
            var sb = new StringBuilder();
            sb.Append(type.FullName).Append(',').Append(type.Scope.Name);
            if (type.Scope is ModuleReference m)
                sb.Append(',').Append(m.MetadataToken.ToUInt32());
            else
            {
                if (type.Scope is not AssemblyNameReference a)
                {
                    if (type.Scope is ModuleDefinition d) a = d.Assembly.Name;
                    else return sb.ToString();
                }

                sb.Append(',');

                if (a.IsCoreLib()) sb.Append(CoreLibKey);
                else
                {
                    sb.Append("PublicKeyToken=");

                    if (a.PublicKeyToken is { Length: > 0 })
                        foreach (var b in a.PublicKeyToken)
                            sb.Append(b.ToString("x2"));
                    else sb.Append("null");
                }
            }

            return sb.ToString();
        }

        public int GetHashCode(TypeReference obj) => GetName(obj).GetHashCode();
    }
}
