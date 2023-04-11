using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using OpenTelemetry.Proxy;
using System.Diagnostics.CodeAnalysis;

namespace OpenTelemetry.StaticProxy.Fody;

internal class StaticProxyEmitter
{
    public EmitContext Context { get; }

    public StaticProxyEmitter(EmitContext context) => Context = context;

    public void Emit()
    {
        var version = Context.TargetModule.Assembly.Name.Version?.ToString() ?? string.Empty;
        foreach (var type in Context.TargetModule.Types.ToArray())
        {
            if (type.IsInterface || type.IsValueType) continue;

            type.CustomAttributes.Add(new(type.Module.ImportReference(Context.ProxyHasGeneratedAttributeCtor)));

            var proxyType = ActivityInvokerHelper.GetActivityName(type, Context);
            if (proxyType.Methods.Count < 1) continue;

            FieldReference? activitySource = null;
            var activitySourceName = string.IsNullOrWhiteSpace(proxyType.ActivitySourceName)
                ? type.FullName
                : proxyType.ActivitySourceName!;

            foreach (var method in proxyType.Methods)
            {
                //Empty or no return method is unnecessary weave in;
                var isVoid = method.Key.ReturnType.HaveSameIdentity(Context.TargetModule.TypeSystem.Void);
                if (isVoid && method.Key.Body.Instructions.Count < 2 || method.Key.Body.Instructions[^1].OpCode != OpCodes.Ret) continue;

                if (method.Value.Settings == ActivitySettings.NonActivityAndSuppressInstrumentation)
                    EmitSuppressInstrumentationScope(method.Key, isVoid);
                else if (method.Value.Settings == ActivitySettings.ActivityNameOnly)
                    EmitActivityName(method.Key, isVoid,
                        string.IsNullOrWhiteSpace(method.Value.Name)
                            ? $"{type.FullName}.{method.Key.Name}"
                            : method.Value.Name, method.Value.MaxUsableTimes);
                else if (method.Value.Settings == ActivitySettings.Activity)
                    EmitActivity(method.Key, isVoid,
                        activitySource ??= AddActivitySource(type, activitySourceName, version),
                        string.IsNullOrWhiteSpace(method.Value.Name)
                            ? $"{activitySourceName}.{method.Key.Name}"
                            : method.Value.Name!, method.Value.Kind);
            }
        }
    }

    public FieldReference AddActivitySource(TypeDefinition type, string name, string version)
    {
        var activitySource = new FieldDefinition("ActivitySource@", FieldAttributes.Private | FieldAttributes.Static,
            Context.ActivitySource);

        type.Fields.Add(activitySource);

        var cctor = type.GetStaticConstructor();
        if (cctor == null)
        {
            cctor = new(".cctor",
                MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName | MethodAttributes.Static, type.Module.TypeSystem.Void);

            type.Methods.Add(cctor);

            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }

        cctor.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldstr, name));

        cctor.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Ldstr, version));
        cctor.Body.Instructions.Insert(2, Instruction.Create(OpCodes.Newobj, Context.ActivitySourceCtor));

        cctor.Body.Instructions.Insert(3, Instruction.Create(OpCodes.Stsfld, activitySource));

        return activitySource;
    }

    public void EmitSuppressInstrumentationScope(MethodDefinition method, bool isVoid) =>
        EmitDisposable(method, isVoid, () =>
        {
            method.Body.Instructions.Insert(0, LdI4(1));

            method.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Call, Context.Begin));

            return 2;
        });

    // https://stackoverflow.com/questions/11074518/add-a-try-catch-with-mono-cecil
    private void EmitDisposable(MethodDefinition method, bool isVoid, Func<int> createDisposable)
    {
        method.Body.InitLocals = true;

        var isTypeAwaitable = CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo);
        var hasAsyncStateMachineAttribute =
            isTypeAwaitable && method.GetCustomAttribute(Context.AsyncStateMachineAttribute) != null;

        var (_, leave) = ProcessReturn(method, isVoid, hasAsyncStateMachineAttribute,
            isTypeAwaitable
                ? index => Ldloca(index, awaitableInfo.AwaitableInfo.AwaitableType.IsValueType, method.Body.Variables)
                : null);

        var disposeVariableIndex = method.Body.Variables.Count;
        method.Body.Variables.Add(new(Context.Disposable));

        var length = createDisposable();
        method.Body.Instructions.Insert(length++, Stloc(disposeVariableIndex, method.Body.Variables));

        var index = method.Body.Instructions.Count - (isVoid ? 1 : 2);

        if (leave != null)
        {
            if (!hasAsyncStateMachineAttribute)
            {
                var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
                {
                    CatchType = Context.TargetModule.TypeSystem.Object,
                    HandlerStart = Instruction.Create(OpCodes.Pop),
                    HandlerEnd = leave
                };

                ProcessHandler(method, isVoid, length, catchHandler);

                /*IL_0010: pop
                IL_0011: ldloc.0
                IL_0012: callvirt instance void [mscorlib]System.IDisposable::Dispose()
                IL_0017: rethrow*/
                method.Body.Instructions.Insert(index++, catchHandler.HandlerStart);
                method.Body.Instructions.Insert(index++, Ldloc(disposeVariableIndex, method.Body.Variables));
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.Dispose));
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Rethrow));
            }

            var awaiterVariableIndex = InvokeAwaiterIsCompleted(method, ref index, awaitableInfo, leave);

            var brfalse = Ldloca(awaiterVariableIndex,
                awaitableInfo.AwaitableInfo.AwaiterType.IsValueType, method.Body.Variables);

            /*IL_0028: brfalse.s IL_0032

            IL_002a: ldloc.0
            IL_002b: callvirt instance void [mscorlib]System.IDisposable::Dispose()
            IL_0030: br.s IL_0046*/
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, brfalse));
            method.Body.Instructions.Insert(index++, Ldloc(disposeVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.Dispose));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Br_S,
                method.Body.Instructions[isVoid ? ^1 : ^2]));

            /*IL_0032: ldloca.s 2
            IL_0034: ldloc.0
            IL_0035: dup
            IL_0036: ldvirtftn instance void [mscorlib]System.IDisposable::Dispose()
            IL_003c: newobj instance void [mscorlib]System.Action::.ctor(object, native int)
            IL_0041: call instance void valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::UnsafeOnCompleted(class [mscorlib]System.Action)*/
            method.Body.Instructions.Insert(index++, brfalse);
            method.Body.Instructions.Insert(index++, Ldloc(disposeVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Dup));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldvirtftn, Context.Dispose));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, Context.ActionCtor));

            var onCompleted = awaitableInfo.AwaitableInfo.AwaiterUnsafeOnCompletedMethod ??
                awaitableInfo.AwaitableInfo.AwaiterOnCompletedMethod;

            method.Body.Instructions.Insert(index, Instruction.Create(
                awaitableInfo.AwaitableInfo.AwaitableType.IsValueType || onCompleted.IsFinal
                    ? OpCodes.Call
                    : OpCodes.Callvirt,
                Context.TargetModule.ImportReference(onCompleted)
                    .MakeHostInstanceGeneric(method.Body.Variables[awaiterVariableIndex].VariableType)));
        }
        else
        {
            var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                HandlerStart = Ldloc(disposeVariableIndex, method.Body.Variables),
                HandlerEnd = method.Body.Instructions[isVoid ? ^1 : ^2]
            };

            ProcessHandler(method, isVoid, length, finallyHandler);

            /*IL_0042: ldloc.0
            IL_0043: brfalse.s IL_004b

            IL_0045: ldloc.0
            IL_0046: callvirt instance void System.IDisposable::Dispose()

            IL_004b: endfinally*/
            var endFinally = Instruction.Create(OpCodes.Endfinally);

            method.Body.Instructions.Insert(index++, finallyHandler.HandlerStart);
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, endFinally));
            method.Body.Instructions.Insert(index++, Ldloc(disposeVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.Dispose));
            method.Body.Instructions.Insert(index, endFinally);
        }
    }

    public void EmitActivityName(MethodDefinition method, bool isVoid, string? activityName,
        int maxUsableTimes)
    {
        if (maxUsableTimes != 0)
            EmitDisposable(method, isVoid, () =>
            {
                var index = 0;
                if (!GetActivityTags(method, ref index))
                    method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldnull));

                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldstr, activityName));
                method.Body.Instructions.Insert(index++, LdI4(maxUsableTimes));
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.SetName));

                return index;
            });
    }

    public void EmitActivity(MethodDefinition method, bool isVoid,
        FieldReference activitySource, string? activityName, int activityKind)
    {
        method.Body.InitLocals = true;

        var activityIndex = method.Body.Variables.Count;
        method.Body.Variables.Add(new(Context.Activity));

        var isTypeAwaitable = CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo);
        var hasAsyncStateMachineAttribute =
            isTypeAwaitable && method.GetCustomAttribute(Context.AsyncStateMachineAttribute) != null;

        var (returnVariableIndex, leave) = ProcessReturn(method, isVoid, hasAsyncStateMachineAttribute, isTypeAwaitable
            ? _ => Ldloc(activityIndex, method.Body.Variables)
            : null);

        /*IL_0000: ldsfld class [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivitySource OpenTelemetry.StaticProxy.Fody.TestClass::ActivitySource
        IL_0005: ldstr "Test.Activity"
        IL_000a: ldc.i4.0
        IL_000b: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivitySource::StartActivity(string, valuetype [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivityKind)
        IL_0010: stloc.0*/

        var length = 0;
        method.Body.Instructions.Insert(length++, Instruction.Create(OpCodes.Ldsfld, activitySource));
        method.Body.Instructions.Insert(length++, Instruction.Create(OpCodes.Ldstr, activityName));
        method.Body.Instructions.Insert(length++, LdI4(activityKind));
        method.Body.Instructions.Insert(length++,
            Instruction.Create(OpCodes.Callvirt, Context.ActivitySourceStartActivity));

        method.Body.Instructions.Insert(length++, Stloc(activityIndex, method.Body.Variables));

        var returnValueTagName = SetActivityTags(method, activityIndex, ref length);

        var exceptionIndex = method.Body.Variables.Count;
        method.Body.Variables.Add(new(Context.Exception));

        var index = method.Body.Instructions.Count - (isVoid ? 1 : 2);

        if (leave != null)
        {
            if (!hasAsyncStateMachineAttribute)
            {
                var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
                {
                    CatchType = Context.Exception,
                    HandlerStart = Stloc(exceptionIndex, method.Body.Variables),
                    HandlerEnd = leave
                };

                ProcessHandler(method, isVoid, length, catchHandler);

                EmitActivityCatch(method, ref index, catchHandler, activityIndex, exceptionIndex);
            }

            /*IL_0033: ldloc.0
            IL_0034: brfalse.s IL_006b*/
            method.Body.Instructions.Insert(index++, leave);

            leave = method.Body.Instructions[^2];

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, leave));

            var awaiterVariableIndex = InvokeAwaiterIsCompleted(method, ref index, awaitableInfo, Ldloca(
                returnVariableIndex, awaitableInfo.AwaitableInfo.AwaitableType.IsValueType,
                method.Body.Variables));

            var brfalse = Ldloca(awaiterVariableIndex, awaitableInfo.AwaitableInfo.AwaiterType.IsValueType,
                method.Body.Variables);

            var (ctor, instanceOnCompleted, staticOnCompleted) = Context.ActivityAwaiterEmitter.GetActivityAwaiter(
                method.Body.Variables[awaiterVariableIndex].VariableType,
                Context.TargetModule.ImportReference(awaitableInfo.AwaitableInfo.AwaiterGetResultMethod));

            /*IL_0047: brfalse.s IL_0052

            IL_0049: ldloc.0
            IL_004a: ldloc.2
            IL_004b: call void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::OnCompleted(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0>)
            IL_0050: br.s IL_006b*/
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, brfalse));
            method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Ldloc(awaiterVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, returnValueTagName == null
                ? Instruction.Create(OpCodes.Ldnull)
                : Instruction.Create(OpCodes.Ldstr, returnValueTagName));

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, staticOnCompleted));

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Br_S, leave));

            /*IL_0052: ldloca.s 2
    IL_0054: ldloc.0
    IL_0055: ldloc.2
    IL_0056: newobj instance void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::.ctor(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0>)
    IL_005b: ldftn instance void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::OnCompleted()
    IL_0061: newobj instance void [mscorlib]System.Action::.ctor(object, native int)
    IL_0066: call instance void valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::UnsafeOnCompleted(class [mscorlib]System.Action)

    IL_006b: ldloc.1
    IL_006c: ret*/
            method.Body.Instructions.Insert(index++, brfalse);
            method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Ldloc(awaiterVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, returnValueTagName == null
                ? Instruction.Create(OpCodes.Ldnull)
                : Instruction.Create(OpCodes.Ldstr, returnValueTagName));

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, ctor));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldftn, instanceOnCompleted));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, Context.ActionCtor));

            var onCompleted = awaitableInfo.AwaitableInfo.AwaiterUnsafeOnCompletedMethod ??
                awaitableInfo.AwaitableInfo.AwaiterOnCompletedMethod;

            method.Body.Instructions.Insert(index, Instruction.Create(
                awaitableInfo.AwaitableInfo.AwaiterType.IsValueType || onCompleted.IsFinal
                    ? OpCodes.Call
                    : OpCodes.Callvirt,
                Context.TargetModule.ImportReference(onCompleted)
                    .MakeHostInstanceGeneric(method.Body.Variables[awaiterVariableIndex].VariableType)));
        }
        else
        {
            var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                HandlerStart = Ldloc(activityIndex, method.Body.Variables),
                HandlerEnd = method.Body.Instructions[isVoid ? ^1 : ^2]
            };

            finallyHandler.TryEnd = finallyHandler.HandlerStart;

            var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                CatchType = Context.Exception,
                HandlerStart = Stloc(exceptionIndex, method.Body.Variables),
                HandlerEnd = finallyHandler.HandlerStart
            };

            ProcessHandler(method, isVoid, length, catchHandler);

            finallyHandler.TryStart = catchHandler.TryStart;

            method.Body.ExceptionHandlers.Add(finallyHandler);

            if (returnValueTagName != null)
            {
                /*IL_003e: ldloc.1
                  IL_003f: brfalse.s IL_0053

                  IL_0041: ldloc.1
                  IL_0042: ldstr "def"
                  IL_0047: ldloc.0
                  IL_0048: box [mscorlib]System.Int32
                  IL_004d: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::SetTag(string, object)
                  IL_0052: pop*/
                var skip = method.Body.Instructions[--index];

                method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, skip));
                method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldstr, returnValueTagName));
                method.Body.Instructions.Insert(index++, Ldloc(returnVariableIndex, method.Body.Variables));
                if (method.ReturnType.IsValueType || method.ReturnType.IsGenericParameter)
                    method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Box, method.ReturnType));

                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.ActivitySetTag));
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Pop));

                index++;
            }

            EmitActivityCatch(method, ref index, catchHandler, activityIndex, exceptionIndex);

            /*IL_0042: ldloc.0
            IL_0043: brfalse.s IL_004b

            IL_0045: ldloc.0
            IL_0046: call instance void [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::Dispose()

            IL_004b: endfinally*/
            var endFinally = Instruction.Create(OpCodes.Endfinally);

            method.Body.Instructions.Insert(index++, finallyHandler.HandlerStart);
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, endFinally));
            method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.ActivityDispose));
            method.Body.Instructions.Insert(index, endFinally);
        }
    }

    private void EmitActivityCatch(MethodDefinition method, ref int index, ExceptionHandler catchHandler,
        int activityIndex, int exceptionIndex)
    {
        /*IL_004c: stloc.3
        IL_004d: ldloc.0
        IL_004e: brfalse.s IL_0063

        IL_0050: ldloc.0
        IL_0051: ldc.i4.2
        IL_0052: ldloc.3
        IL_0053: callvirt instance string [netstandard]System.Exception::get_Message()
        IL_0058: call instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::SetStatus(valuetype [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivityStatusCode, string)
        IL_005d: ldloc.3
        IL_005e: call void [OpenTelemetry.Api]OpenTelemetry.Trace.ActivityExtensions::RecordException(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, class [netstandard]System.Exception)

        IL_0063: rethrow*/
        var catchEnd = Instruction.Create(OpCodes.Rethrow);

        method.Body.Instructions.Insert(index++, catchHandler.HandlerStart);
        method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, catchEnd));

        method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
        method.Body.Instructions.Insert(index++, LdI4(2));
        method.Body.Instructions.Insert(index++, Ldloc(exceptionIndex, method.Body.Variables));
        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.GetMessage));

        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.ActivitySetStatus));
        method.Body.Instructions.Insert(index++, Ldloc(exceptionIndex, method.Body.Variables));
        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.RecordException));
        method.Body.Instructions.Insert(index++, catchEnd);
    }

    /// <returns>返回值变量索引，-1则表示是void</returns>
    private static (int, Instruction?) ProcessReturn(MethodDefinition method, bool isVoid,
        bool hasAsyncStateMachineAttribute, Func<int, Instruction>? createLeave)
    {
        var checkLeaveS = method.Body.ExceptionHandlers.Count < 1 ||
            method.Body.ExceptionHandlers[^1].HandlerEnd !=
            method.Body.Instructions[isVoid ? ^1 : ^2];

        var leaveCode = method.Body.Instructions[^1].Offset > byte.MaxValue ? OpCodes.Leave : OpCodes.Leave_S;

        var variableIndex = -1;
        Instruction? leave = null;
        for (var index = method.Body.Instructions.Count - 1; index > 0; index--)
        {
            if (method.Body.Instructions[index].OpCode != OpCodes.Ret)
            {
                var opCode = method.Body.Instructions[index].OpCode;
                if (opCode != OpCodes.Br_S && opCode != OpCodes.Br) continue;

                method.Body.Instructions[index].OpCode = opCode == OpCodes.Br ? OpCodes.Leave : OpCodes.Leave_S;
                method.Body.Instructions[index].Operand = leave;

                opCode = method.Body.Instructions[index - 1].OpCode;
                if (opCode == OpCodes.Stloc_0 || opCode == OpCodes.Stloc_1 || opCode == OpCodes.Stloc_2 ||
                    opCode == OpCodes.Stloc_3 || opCode == OpCodes.Stloc_S || opCode == OpCodes.Stloc)
                    continue;
            }

            if (!isVoid && !IsLdloc(method.Body.Instructions[index - 1], method.Body.Variables, out _))
            {
                if (leave == null)
                {
                    variableIndex = method.Body.Variables.Count;
                    method.Body.Variables.Add(new(method.ReturnType));
                }

                method.Body.Instructions.Insert(index++, Stloc(variableIndex, method.Body.Variables));
            }

            if (leave != null) // Replace ret with Leave_S
                method.Body.Instructions[index] = Instruction.Create(leaveCode, leave);
            else
            {
                if (isVoid)
                {
                    leave = createLeave == null ? method.Body.Instructions[index] : createLeave(variableIndex);

                    if (checkLeaveS && CheckLeaveS(method, hasAsyncStateMachineAttribute, index, leave)) index++;
                }
                else if (variableIndex < 0 && IsLdloc(method.Body.Instructions[index - 1], method.Body.Variables, out variableIndex))
                {
                    leave = createLeave == null ? method.Body.Instructions[index - 1] : createLeave(variableIndex);

                    if (checkLeaveS && CheckLeaveS(method, hasAsyncStateMachineAttribute, index - 1, leave)) index++;
                }
                else if (createLeave == null)
                {
                    leave = Ldloc(variableIndex, method.Body.Variables);

                    if (checkLeaveS && CheckLeaveS(method, hasAsyncStateMachineAttribute, index, leave)) index++;

                    method.Body.Instructions.Insert(index++, leave);
                }
                else
                {
                    if (checkLeaveS && CheckLeaveS(method, hasAsyncStateMachineAttribute, index,
                            leave = createLeave(variableIndex))) index++;

                    method.Body.Instructions.Insert(index++, Ldloc(variableIndex, method.Body.Variables));
                }
            }

            if (hasAsyncStateMachineAttribute) break;
        }

        return (variableIndex, createLeave == null ? null : leave);

        static bool IsLdloc(Instruction instruction, Collection<VariableDefinition> variables, out int variableIndex)
        {
            if (instruction.OpCode == OpCodes.Ldloc_0)
            {
                variableIndex = 0;
                return true;
            }

            if (instruction.OpCode == OpCodes.Ldloc_1)
            {
                variableIndex = 1;
                return true;
            }

            if (instruction.OpCode == OpCodes.Ldloc_2)
            {
                variableIndex = 2;
                return true;
            }

            if (instruction.OpCode == OpCodes.Ldloc_3)
            {
                variableIndex = 3;
                return true;
            }

            if (instruction.OpCode == OpCodes.Ldloc_S)
            {
                variableIndex = instruction.Operand is byte b ? b : variables.IndexOf((VariableDefinition)instruction.Operand);

                return true;
            }

            if (instruction.OpCode == OpCodes.Ldloc)
            {
                variableIndex = instruction.Operand is ushort b ? b : variables.IndexOf((VariableDefinition)instruction.Operand);

                return true;
            }

            variableIndex = -1;
            return false;
        }

        static bool CheckLeaveS(MethodDefinition method, bool hasAsyncStateMachineAttribute, int index,
            Instruction leave)
        {
            if (hasAsyncStateMachineAttribute) return false;

            var instruction = method.Body.Instructions[index - 1];

            if (instruction.Operand == leave)
            {
                if (instruction.OpCode == OpCodes.Leave_S || instruction.OpCode == OpCodes.Leave) return false;

                if (instruction.OpCode == OpCodes.Br_S)
                {
                    instruction.OpCode = OpCodes.Leave_S;

                    return false;
                }

                if (instruction.OpCode == OpCodes.Br)
                {
                    instruction.OpCode = OpCodes.Leave;

                    return false;
                }
            }

            if (instruction.OpCode == OpCodes.Br_S && instruction.Operand != leave)
            {
                instruction.OpCode = OpCodes.Leave_S;
                instruction.Operand = leave;

                return false;
            }

            if (instruction.OpCode == OpCodes.Br && instruction.Operand != leave)
            {
                instruction.OpCode = OpCodes.Leave;
                instruction.Operand = leave;

                return false;
            }

            method.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Leave_S, leave));

            return true;
        }
    }

    private static void ProcessHandler(MethodDefinition method, bool isVoid, int index,
        ExceptionHandler exceptionHandler)
    {
        if (method.Body.ExceptionHandlers.Count > 0 &&
            method.Body.ExceptionHandlers[^1].HandlerEnd ==
            method.Body.Instructions[isVoid ? ^1 : ^2])
            method.Body.ExceptionHandlers[^1].HandlerEnd = exceptionHandler.HandlerStart;

        exceptionHandler.TryStart = method.Body.Instructions[index];
        exceptionHandler.TryEnd = exceptionHandler.HandlerStart;

        method.Body.ExceptionHandlers.Add(exceptionHandler);
    }

    private static int InvokeAwaiterIsCompleted(MethodDefinition method, ref int index,
        CoercedAwaitableInfo awaitableInfo, Instruction ldReturn)
    {
        var awaiterVariableIndex = method.Body.Variables.Count;

        method.Body.Variables.Add(new(method.Module.ImportReference(awaitableInfo.AwaitableInfo.AwaiterType)));

        /*IL_0019: ldloca.s 1
        IL_001b: call instance valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0> valuetype [System.Threading.Tasks.Extensions]System.Threading.Tasks.ValueTask`1<int32>::GetAwaiter()
        IL_0020: stloc.2*/
        method.Body.Instructions.Insert(index++, ldReturn);

        if (awaitableInfo.CoercerExpression != null)
            foreach (var instruction in awaitableInfo.CoercerExpression)
                method.Body.Instructions.Insert(index++, instruction);

        method.Body.Instructions.Insert(index++,
            Instruction.Create(
                awaitableInfo.AwaitableInfo.AwaitableType.IsValueType ||
                awaitableInfo.AwaitableInfo.GetAwaiterMethod.IsFinal
                    ? OpCodes.Call
                    : OpCodes.Callvirt,
                method.Module.ImportReference(awaitableInfo.AwaitableInfo.GetAwaiterMethod)
                    .MakeHostInstanceGeneric(awaitableInfo.AwaitableInfo.AwaitableType)));

        method.Body.Instructions.Insert(index++, Stloc(awaiterVariableIndex, method.Body.Variables));

        /*IL_0021: ldloca.s 2
        IL_0023: call instance bool valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::get_IsCompleted()*/
        method.Body.Instructions.Insert(index++,
            Ldloca(awaiterVariableIndex, awaitableInfo.AwaitableInfo.AwaiterType.IsValueType, method.Body.Variables));

        method.Body.Instructions.Insert(index++, Instruction.Create(
            awaitableInfo.AwaitableInfo.AwaiterType.IsValueType ||
            awaitableInfo.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod.IsFinal
                ? OpCodes.Call
                : OpCodes.Callvirt,
            method.Module.ImportReference(awaitableInfo.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod)
                .MakeHostInstanceGeneric(awaitableInfo.AwaitableInfo.AwaiterType)));

        return awaiterVariableIndex;
    }

    private bool GetActivityTags(MethodDefinition method, ref int index)
    {
        var list = GetActivityTags(method, (method.GetCustomAttribute(Context.ActivityNameAttribute) ??
                method.DeclaringType.GetCustomAttribute(Context.ActivityNameAttribute))?
            .GetValue<string[]>("Tags", new ArrayType(method.Module.TypeSystem.String))?.ToList(), out _);

        if (list.Count < 1) return false;

        /*IL_0550: ldc.i4.2
          IL_0551: newarr       valuetype [netstandard]System.Collections.Generic.KeyValuePair`2<string, object>
          IL_056c: dup
          IL_056d: ldc.i4.1
          IL_056e: ldstr        "123"
          IL_0573: ldstr        "aaa"
          IL_0578: newobj       instance void valuetype [netstandard]System.Collections.Generic.KeyValuePair`2<string, object>::.ctor()
          IL_057d: stelem       valuetype [netstandard]System.Collections.Generic.KeyValuePair`2<string, object>*/
        method.Body.Instructions.Insert(index++, LdI4(list.Count));
        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newarr, Context.KeyValuePair));

        for (var i = 0; i < list.Count; i++)
        {
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Dup));
            method.Body.Instructions.Insert(index++, LdI4(i));

            foreach (var instruction in list[i]) method.Body.Instructions.Insert(index++, instruction);

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, Context.KeyValuePairCtor));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Stelem_Any, Context.KeyValuePair));
        }

        return true;
    }

    private string? SetActivityTags(MethodDefinition method, int activityIndex, ref int index)
    {
        var list = GetActivityTags(method, method.GetCustomAttribute(Context.ActivityAttribute)?
                .GetValue<string[]>("Tags", new ArrayType(method.Module.TypeSystem.String))?.ToList(),
            out var returnValueTagName);

        if (list.Count < 1) return returnValueTagName;

        method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
        method.Body.Instructions.Insert(index,
            Instruction.Create(OpCodes.Brfalse_S, method.Body.Instructions[index++]));

        method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));

        /*IL_0550: Ldloc.0
          IL_0573: ldstr        "aaa"
          IL_0574: ldarg.0
          IL_0578: callvirt       instance void SetTag*/
        foreach (var tag in list)
        {
            foreach (var instruction in tag) method.Body.Instructions.Insert(index++, instruction);

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.ActivitySetTag));
        }

        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Pop));

        return returnValueTagName;
    }

    private IReadOnlyList<IReadOnlyList<Instruction>> GetActivityTags(MethodDefinition method,
        ICollection<string>? tags, out string? returnValueTagName)
    {
        if (!method.ReturnType.HaveSameIdentity(Context.TargetModule.TypeSystem.Void) &&
            (!CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo) ||
                !awaitableInfo.AwaitableInfo.AwaiterGetResultMethod.ReturnType.HaveSameIdentity(Context.TargetModule
                    .TypeSystem.Void)))
            TryGetName(tags, method.MethodReturnType.GetCustomAttribute(Context.ActivityTagAttribute),
                "$returnvalue", out returnValueTagName);
        else returnValueTagName = null;

        var list = new List<IReadOnlyList<Instruction>>();

        foreach (var field in method.DeclaringType.Fields)
            if ((!method.IsStatic || field.IsStatic) && TryGetName(tags,
                    field.GetCustomAttribute(Context.ActivityTagAttribute),
                    field.Name, out var name))
            {
                /*IL_0558: ldstr        "123"
                  IL_055d: ldarg_0
                  IL_055d: ldfld        "aaa"*/
                var instructions = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldstr, name)
                };

                if (!field.IsStatic) instructions.Add(Instruction.Create(OpCodes.Ldarg_0));

                instructions.Add(Instruction.Create(OpCodes.Ldfld, field));

                if (field.FieldType.IsValueType || field.FieldType.IsGenericParameter)
                    instructions.Add(Instruction.Create(OpCodes.Box, field.FieldType));

                list.Add(instructions);
            }

        foreach (var property in method.DeclaringType.Properties)
            if (property.GetMethod != null && (!method.IsStatic || property.GetMethod.IsStatic) &&
                TryGetName(tags, property.GetCustomAttribute(Context.ActivityTagAttribute),
                    property.Name, out var name))
            {
                /*IL_0558: ldstr        "123"
                  IL_055d: ldarg_0
                  IL_055d: callvirt        "aaa"*/
                var instructions = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldstr, name)
                };

                if (!property.GetMethod.IsStatic) instructions.Add(Instruction.Create(OpCodes.Ldarg_0));

                instructions.Add(Instruction.Create(
                    method.DeclaringType.IsValueType || property.GetMethod.IsFinal || property.GetMethod.IsStatic
                        ? OpCodes.Call
                        : OpCodes.Callvirt,
                    method.Module.ImportReference(property.GetMethod)));

                if (property.PropertyType.IsValueType || property.PropertyType.IsGenericParameter)
                    instructions.Add(Instruction.Create(OpCodes.Box, property.PropertyType));

                list.Add(instructions);
            }

        for (var i = 0; i < method.Parameters.Count; i++)
            if (TryGetName(tags, method.Parameters[i].GetCustomAttribute(Context.ActivityTagAttribute),
                    method.Parameters[i].Name, out var name))
            {
                /*IL_0558: ldstr        "123"
                  IL_055d: ldarg_0
                  IL_055d: ldarg_1*/
                var instructions = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldstr, name),
                    Ldarg(i, method.IsStatic, method.Parameters)
                };

                if (method.Parameters[i].ParameterType.IsValueType ||
                    method.Parameters[i].ParameterType.IsGenericParameter)
                    instructions.Add(Instruction.Create(OpCodes.Box, method.Parameters[i].ParameterType));

                list.Add(instructions);
            }

        return list;
    }

    private bool TryGetName(ICollection<string>? tags, ICustomAttribute? attr, string memberName,
        [NotNullWhen(true)] out string? name)
    {
        name = null;

        if (attr == null)
        {
            if (tags == null || !tags.Contains(memberName)) return false;

            name = memberName;
        }
        else name = attr.GetValue<string>("", Context.TargetModule.TypeSystem.String) ?? memberName;

        tags?.Remove(memberName);

        return true;
    }

    private static Instruction Ldarg(int index, bool isStaticMethod, IList<ParameterDefinition> parameters) =>
        (isStaticMethod ? index : index + 1) switch
        {
            < 0 or > ushort.MaxValue => throw new ArgumentOutOfRangeException(nameof(index)),
            0 => Instruction.Create(OpCodes.Ldarg_0),
            1 => Instruction.Create(OpCodes.Ldarg_1),
            2 => Instruction.Create(OpCodes.Ldarg_2),
            3 => Instruction.Create(OpCodes.Ldarg_3),
            <= byte.MaxValue => Instruction.Create(OpCodes.Ldarg_S, parameters[index]),
            _ => Instruction.Create(OpCodes.Ldarg, parameters[index])
        };

    private static Instruction Ldloc(int index, IList<VariableDefinition> variables) => index switch
    {
        < 0 or > ushort.MaxValue => throw new ArgumentOutOfRangeException(nameof(index)),
        0 => Instruction.Create(OpCodes.Ldloc_0),
        1 => Instruction.Create(OpCodes.Ldloc_1),
        2 => Instruction.Create(OpCodes.Ldloc_2),
        3 => Instruction.Create(OpCodes.Ldloc_3),
        <= byte.MaxValue => Instruction.Create(OpCodes.Ldloc_S, variables[index]),
        _ => Instruction.Create(OpCodes.Ldloc, variables[index])
    };

    private static Instruction Ldloca(int index, bool isValueType, IList<VariableDefinition> variables) => isValueType
        ? index switch
        {
            < 0 or > ushort.MaxValue => throw new ArgumentOutOfRangeException(nameof(index)),
            <= byte.MaxValue => Instruction.Create(OpCodes.Ldloca_S, variables[index]),
            _ => Instruction.Create(OpCodes.Ldloca, variables[index])
        }
        : Ldloc(index, variables);

    private static Instruction Stloc(int index, IList<VariableDefinition> variables) => index switch
    {
        < 0 or > ushort.MaxValue => throw new ArgumentOutOfRangeException(nameof(index)),
        0 => Instruction.Create(OpCodes.Stloc_0),
        1 => Instruction.Create(OpCodes.Stloc_1),
        2 => Instruction.Create(OpCodes.Stloc_2),
        3 => Instruction.Create(OpCodes.Stloc_3),
        <= byte.MaxValue => Instruction.Create(OpCodes.Stloc_S, variables[index]),
        _ => Instruction.Create(OpCodes.Stloc, variables[index])
    };

    private static Instruction LdI4(int value) => value switch
    {
        -1 => Instruction.Create(OpCodes.Ldc_I4_M1),
        0 => Instruction.Create(OpCodes.Ldc_I4_0),
        1 => Instruction.Create(OpCodes.Ldc_I4_1),
        2 => Instruction.Create(OpCodes.Ldc_I4_2),
        3 => Instruction.Create(OpCodes.Ldc_I4_3),
        4 => Instruction.Create(OpCodes.Ldc_I4_4),
        5 => Instruction.Create(OpCodes.Ldc_I4_5),
        6 => Instruction.Create(OpCodes.Ldc_I4_6),
        7 => Instruction.Create(OpCodes.Ldc_I4_7),
        8 => Instruction.Create(OpCodes.Ldc_I4_8),
        <= sbyte.MaxValue and >= sbyte.MinValue => Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)value),
        _ => Instruction.Create(OpCodes.Ldc_I4, value)
    };
}
