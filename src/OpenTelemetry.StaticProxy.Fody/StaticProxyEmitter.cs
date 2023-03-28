using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OpenTelemetry.Proxy;

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
            var proxyType = ActivityInvokerHelper.GetActivityName(type, Context);
            if (proxyType.Methods.Count < 1) continue;

            FieldReference? activitySource = null;
            var activitySourceName = string.IsNullOrWhiteSpace(proxyType.ActivitySourceName)
                ? type.FullName
                : proxyType.ActivitySourceName!;

            foreach (var method in proxyType.Methods)
            {
                //Empty method is unnecessary weave in;
                var isVoid = method.Key.ReturnType.HaveSameIdentity(Context.TargetModule.TypeSystem.Void);
                if (isVoid && method.Key.Body.Instructions.Count < 2) continue;

                if (method.Value.Settings == ActivitySettings.NonActivityAndSuppressInstrumentation)
                    EmitSuppressInstrumentationScope(method.Key, isVoid);
                else if (method.Value.Settings == ActivitySettings.ActivityNameOnly)
                    EmitActivityName(method.Key, isVoid,
                        string.IsNullOrWhiteSpace(method.Value.Name)
                            ? $"{type.FullName}.{method.Key.Name}"
                            : method.Value.Name, method.Value.MaxUsableTimes);
                else if (method.Value.Settings == ActivitySettings.Activity)
                    EmitActivity(method.Key, isVoid, activitySource ??= AddActivitySource(type, activitySourceName, version),
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

    // https://stackoverflow.com/questions/11074518/add-a-try-catch-with-mono-cecil
    public void EmitSuppressInstrumentationScope(MethodDefinition method, bool isVoid)
    {
        method.Body.InitLocals = true;

        var (_, leave) = ProcessReturn(method, isVoid,
            CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo)
                ? index => Ldloc(index, awaitableInfo.AwaitableInfo.AwaitableType.IsValueType, method.Body.Variables)
                : null);

        var disposeVariableIndex = method.Body.Variables.Count;
        method.Body.Variables.Add(new(Context.Disposable));

        /*ldc.i4.1
        call class System.IDisposable [OpenTelemetry]OpenTelemetry.SuppressInstrumentationScope::Begin(bool)
        stloc.0*/
        method.Body.Instructions.Insert(0, LdI4(1));
        method.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Call, Context.Begin));
        method.Body.Instructions.Insert(2, Stloc(disposeVariableIndex, method.Body.Variables));

        var index = method.Body.Instructions.Count - (isVoid ? 1 : 2);

        if (leave != null)
        {
            var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                CatchType = Context.TargetModule.TypeSystem.Object,
                HandlerStart = Instruction.Create(OpCodes.Pop),
                HandlerEnd = leave
            };

            ProcessHandler(method, isVoid, 3, catchHandler);

            /*IL_0010: pop
            IL_0011: ldloc.0
            IL_0012: callvirt instance void [mscorlib]System.IDisposable::Dispose()
            IL_0017: rethrow*/
            method.Body.Instructions.Insert(index++, catchHandler.HandlerStart);
            method.Body.Instructions.Insert(index++, Ldloc(disposeVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.Dispose));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Rethrow));

            var awaiterVariableIndex = GetAwaiter(method, ref index, awaitableInfo, catchHandler.HandlerEnd);
            var brfalse = Ldloc(awaiterVariableIndex,
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
                onCompleted.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
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

            ProcessHandler(method, isVoid, 3, finallyHandler);

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

    public void EmitActivityName(MethodDefinition method, bool isVoid, string? activityName, int maxUsableTimes)
    {
        if (maxUsableTimes == 0) return;

        method.Body.InitLocals = true;

        var (_, leave) = ProcessReturn(method, isVoid,
            CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo)
                ? index => Ldloc(index, awaitableInfo.AwaitableInfo.AwaitableType.IsValueType, method.Body.Variables)
                : null);

        method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldstr, activityName));
        method.Body.Instructions.Insert(1, LdI4(maxUsableTimes));
        method.Body.Instructions.Insert(2, Instruction.Create(OpCodes.Call, Context.SetName));

        var index = method.Body.Instructions.Count - (isVoid ? 1 : 2);

        if (leave != null)
        {
            var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                CatchType = Context.TargetModule.TypeSystem.Object,
                HandlerStart = Instruction.Create(OpCodes.Pop),
                HandlerEnd = leave
            };

            ProcessHandler(method, isVoid, 3, catchHandler);

            /*IL_003b: pop
            IL_003c: call void [OpenTelemetry.Proxy]OpenTelemetry.Proxy.ActivityName::Clear()
            IL_0041: rethrow*/
            method.Body.Instructions.Insert(index++, catchHandler.HandlerStart);
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.Clear));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Rethrow));

            var awaiterVariableIndex = GetAwaiter(method, ref index, awaitableInfo, catchHandler.HandlerEnd);
            var brfalse = Ldloc(awaiterVariableIndex,
                awaitableInfo.AwaitableInfo.AwaiterType.IsValueType, method.Body.Variables);

            /*IL_0052: brfalse.s IL_005b

            IL_0054: call void [OpenTelemetry.Proxy]OpenTelemetry.Proxy.ActivityName::Clear()
            IL_0059: br.s IL_007d*/
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse_S, brfalse));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.Clear));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Br_S,
                method.Body.Instructions[isVoid ? ^1 : ^2]));

            /*IL_0032: ldloca.s 2
            IL_0066: ldnull
            IL_0067: ldftn void [OpenTelemetry.Proxy]OpenTelemetry.Proxy.ActivityName::Clear()
            IL_006d: newobj instance void [mscorlib]System.Action::.ctor(object, native int)
            IL_0078: call instance void valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::UnsafeOnCompleted(class [mscorlib]System.Action)*/
            method.Body.Instructions.Insert(index++, brfalse);
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldnull));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldftn, Context.Clear)); //TODO optimize
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, Context.ActionCtor));

            var onCompleted = awaitableInfo.AwaitableInfo.AwaiterUnsafeOnCompletedMethod ??
                awaitableInfo.AwaitableInfo.AwaiterOnCompletedMethod;

            method.Body.Instructions.Insert(index,
                Instruction.Create(onCompleted.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
                    Context.TargetModule.ImportReference(onCompleted)
                        .MakeHostInstanceGeneric(method.Body.Variables[awaiterVariableIndex].VariableType)));
        }
        else
        {
            var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                HandlerStart = Instruction.Create(OpCodes.Call, Context.Clear),
                HandlerEnd = method.Body.Instructions[isVoid ? ^1 : ^2]
            };

            ProcessHandler(method, isVoid, 3, finallyHandler);

            /*IL_006f: call void [OpenTelemetry.Proxy]OpenTelemetry.Proxy.ActivityName::Clear()
            IL_0074: endfinally*/
            method.Body.Instructions.Insert(index++, finallyHandler.HandlerStart);
            method.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Endfinally));
        }
    }

    public void EmitActivity(MethodDefinition method, bool isVoid, FieldReference activitySource,
        string? activityName, int activityKind)
    {
        method.Body.InitLocals = true;

        var activityIndex = method.Body.Variables.Count;
        method.Body.Variables.Add(new(Context.Activity));

        var (returnVariableIndex, leave) = ProcessReturn(method, isVoid,
            CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo)
                ? _ => Ldloc(activityIndex, method.Body.Variables)
                : null);

        // TODO Optimize AsyncStateMachineAttribute, remove try catch
        /*IL_0000: ldsfld class [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivitySource OpenTelemetry.StaticProxy.Fody.TestClass::ActivitySource
        IL_0005: ldstr "Test.Activity"
        IL_000a: ldc.i4.0
        IL_000b: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivitySource::StartActivity(string, valuetype [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivityKind)
        IL_0010: stloc.0*/

        method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldsfld, activitySource));
        method.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Ldstr, activityName));
        method.Body.Instructions.Insert(2, LdI4(activityKind));
        method.Body.Instructions.Insert(3, Instruction.Create(OpCodes.Callvirt, Context.ActivitySourceStartActivity));
        method.Body.Instructions.Insert(4, Stloc(activityIndex, method.Body.Variables));

        var exceptionIndex = method.Body.Variables.Count;
        method.Body.Variables.Add(new(Context.Exception));

        var index = method.Body.Instructions.Count - (isVoid ? 1 : 2);

        if (leave != null)
        {
            var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                CatchType = Context.Exception,
                HandlerStart = Stloc(exceptionIndex, method.Body.Variables),
                HandlerEnd = leave
            };

            ProcessHandler(method, isVoid, 5, catchHandler);

            EmitActivityCatch(method, ref index, catchHandler, activityIndex, exceptionIndex);

            /*IL_0033: ldloc.0
            IL_0034: brtrue.s IL_0038

            IL_0036: ldloc.1
            IL_0037: ret*/
            var ldReturn = Ldloc(returnVariableIndex, awaitableInfo.AwaitableInfo.AwaitableType.IsValueType,
                method.Body.Variables);

            method.Body.Instructions.Insert(index++, leave);
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brtrue_S, ldReturn));

            index += 2;

            var awaiterVariableIndex = GetAwaiter(method, ref index, awaitableInfo, ldReturn);
            var brfalse = Ldloc(awaiterVariableIndex, awaitableInfo.AwaitableInfo.AwaiterType.IsValueType,
                method.Body.Variables);

            var br = Ldloc(returnVariableIndex, method.Body.Variables);

            var (ctor, instanceOnCompleted, staticOnCompleted) = Context.ActivityAwaiterEmitter.GetActivityAwaiter(
                method.Body.Variables[awaiterVariableIndex].VariableType,
                Context.TargetModule.ImportReference(awaitableInfo.AwaitableInfo.AwaiterGetResultMethod));

            /*IL_0047: brfalse.s IL_0052

            IL_0049: ldloc.0
            IL_004a: ldloc.2
            IL_004b: call void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::OnCompleted(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0>)
            IL_0050: br.s IL_006b*/
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse_S, brfalse));
            method.Body.Instructions.Add(Ldloc(activityIndex, method.Body.Variables));
            method.Body.Instructions.Add(Ldloc(awaiterVariableIndex, method.Body.Variables));

            method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, staticOnCompleted));

            method.Body.Instructions.Add(Instruction.Create(OpCodes.Br_S, br));

            /*IL_0052: ldloca.s 2
    IL_0054: ldloc.0
    IL_0055: ldloc.2
    IL_0056: newobj instance void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::.ctor(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0>)
    IL_005b: ldftn instance void class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<int32>::OnCompleted()
    IL_0061: newobj instance void [mscorlib]System.Action::.ctor(object, native int)
    IL_0066: call instance void valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::UnsafeOnCompleted(class [mscorlib]System.Action)

    IL_006b: ldloc.1
    IL_006c: ret*/
            method.Body.Instructions.Add(brfalse);
            method.Body.Instructions.Add(Ldloc(activityIndex, method.Body.Variables));
            method.Body.Instructions.Add(Ldloc(awaiterVariableIndex, method.Body.Variables));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldftn, instanceOnCompleted));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, Context.ActionCtor));

            var onCompleted = awaitableInfo.AwaitableInfo.AwaiterUnsafeOnCompletedMethod ??
                awaitableInfo.AwaitableInfo.AwaiterOnCompletedMethod;

            method.Body.Instructions.Add(Instruction.Create(onCompleted.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
                Context.TargetModule.ImportReference(onCompleted)
                    .MakeHostInstanceGeneric(method.Body.Variables[awaiterVariableIndex].VariableType)));

            method.Body.Instructions.Add(br);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
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

            ProcessHandler(method, isVoid, 5, catchHandler);

            finallyHandler.TryStart = catchHandler.TryStart;

            method.Body.ExceptionHandlers.Add(finallyHandler);

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
    private static (int, Instruction?) ProcessReturn(MethodDefinition method, bool isVoid, Func<int, Instruction>? createLeave)
    {
        var variableIndex = -1;
        Instruction? leave = null;
        for (var index = method.Body.Instructions.Count - 1; index > 0; index--)
        {
            if (method.Body.Instructions[index].OpCode != OpCodes.Ret) continue;

            if (!isVoid && !IsLdloc(method.Body.Instructions[index - 1], out _))
            {
                if (leave == null)
                {
                    variableIndex = method.Body.Variables.Count;
                    method.Body.Variables.Add(new(method.ReturnType));
                }

                method.Body.Instructions.Insert(index++, Stloc(variableIndex, method.Body.Variables));
            }

            if (leave != null) // Replace ret with Leave_S
                method.Body.Instructions[index] = Instruction.Create(OpCodes.Leave_S, leave);
            else
            {
                var checkLeaveS = method.Body.ExceptionHandlers.Count < 1 ||
                    method.Body.ExceptionHandlers[^1].HandlerEnd !=
                    method.Body.Instructions[isVoid ? ^1 : ^2];

                if (isVoid)
                {
                    leave = createLeave == null ? method.Body.Instructions[index] : createLeave(variableIndex);

                    if (checkLeaveS && CheckLeaveS(method, index, leave)) index++;
                }
                else if (variableIndex < 0 && IsLdloc(method.Body.Instructions[index - 1], out variableIndex))
                {
                    leave = createLeave == null ? method.Body.Instructions[index - 1] : createLeave(variableIndex);

                    if (checkLeaveS && CheckLeaveS(method, index - 1, leave)) index++;
                }
                else if (createLeave == null)
                {
                    leave = Ldloc(variableIndex, method.Body.Variables);

                    if (checkLeaveS && CheckLeaveS(method, index, leave)) index++;

                    method.Body.Instructions.Insert(index++, leave);
                }
                else
                {
                    if (checkLeaveS && CheckLeaveS(method, index, leave = createLeave(variableIndex))) index++;

                    method.Body.Instructions.Insert(index++, Ldloc(variableIndex, method.Body.Variables));
                }
            }
        }

        return (variableIndex, createLeave == null ? null : leave);

        static bool IsLdloc(Instruction instruction, out int variableIndex)
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
                variableIndex = (byte)instruction.Operand;
                return true;
            }

            if (instruction.OpCode == OpCodes.Ldloc)
            {
                variableIndex = (ushort)instruction.Operand;
                return true;
            }

            variableIndex = -1;
            return false;
        }

        static bool CheckLeaveS(MethodDefinition method, int index, Instruction leave)
        {
            var instruction = method.Body.Instructions[index - 1];

            if (instruction.Operand == leave)
            {
                if (instruction.OpCode == OpCodes.Leave_S) return false;

                if (instruction.OpCode == OpCodes.Br_S)
                {
                    instruction.OpCode = OpCodes.Leave_S;

                    return false;
                }
            }

            if (instruction.OpCode == OpCodes.Br_S && instruction.Operand != leave)
            {
                instruction.OpCode = OpCodes.Leave_S;
                instruction.Operand = leave;

                return false;
            }

            method.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Leave_S, leave));

            return true;
        }
    }

    private static void ProcessHandler(MethodDefinition method, bool isVoid, int index, ExceptionHandler exceptionHandler)
    {
        if (method.Body.ExceptionHandlers.Count > 0 &&
            method.Body.ExceptionHandlers[^1].HandlerEnd ==
            method.Body.Instructions[isVoid ? ^1 : ^2])
            method.Body.ExceptionHandlers[^1].HandlerEnd = exceptionHandler.HandlerStart;

        exceptionHandler.TryStart = method.Body.Instructions[index];
        exceptionHandler.TryEnd = exceptionHandler.HandlerStart;

        method.Body.ExceptionHandlers.Add(exceptionHandler);
    }

    private static int GetAwaiter(MethodDefinition method, ref int index, CoercedAwaitableInfo awaitableInfo,
        Instruction ldReturn)
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
            Instruction.Create(awaitableInfo.AwaitableInfo.GetAwaiterMethod.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
                method.Module.ImportReference(awaitableInfo.AwaitableInfo.GetAwaiterMethod)
                    .MakeHostInstanceGeneric(awaitableInfo.AwaitableInfo.AwaitableType)));

        method.Body.Instructions.Insert(index++, Stloc(awaiterVariableIndex, method.Body.Variables));

        /*IL_0021: ldloca.s 2
        IL_0023: call instance bool valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::get_IsCompleted()*/
        method.Body.Instructions.Insert(index++,
            Ldloc(awaiterVariableIndex, awaitableInfo.AwaitableInfo.AwaiterType.IsValueType, method.Body.Variables));

        method.Body.Instructions.Insert(index++, Instruction.Create(
            awaitableInfo.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod.IsFinal ? OpCodes.Call : OpCodes.Callvirt,
            method.Module.ImportReference(awaitableInfo.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod)
                .MakeHostInstanceGeneric(awaitableInfo.AwaitableInfo.AwaiterType)));

        return awaiterVariableIndex;
    }

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

    private static Instruction Ldloc(int index, bool isValueType, IList<VariableDefinition> variables) => isValueType
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
