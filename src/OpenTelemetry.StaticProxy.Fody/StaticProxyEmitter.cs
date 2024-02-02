using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using OpenTelemetry.Proxy;
using System.Diagnostics.CodeAnalysis;

namespace OpenTelemetry.StaticProxy.Fody;

internal class StaticProxyEmitter(EmitContext context)
{
    private TypeDefinition? _activitySource;

    public EmitContext Context { get; } = context;

    public void Emit()
    {
        var version = Context.TargetModule.Assembly.Name.Version?.ToString() ?? string.Empty;

        var assemblyEmitted = false;
        foreach (var type in Context.TargetModule.Types.SelectMany(GetTypes).ToArray())
        {
            if (type.IsInterface || type.IsValueType ||
                type.GetCustomAttribute(Context.CompilerGeneratedAttribute) != null) continue;

            var proxyType = ActivityInvokerHelper.GetProxyType(type, Context);
            if (proxyType.Methods.Count < 1) continue;

            FieldReference? activitySource = null;
            var activitySourceName = string.IsNullOrWhiteSpace(proxyType.ActivitySourceName)
                ? type.FullName
                : proxyType.ActivitySourceName!;

            var typeEmitted = false;

            foreach (var method in proxyType.Methods)
            {
                //Empty or no return method is unnecessary weave in;
                var isVoid = method.Key.ReturnType.HaveSameIdentity(Context.TargetModule.TypeSystem.Void);
                if (isVoid && method.Key.Body.Instructions.Count < 2 ||
                    method.Key.Body.Instructions[^1].OpCode != OpCodes.Ret) continue;

                if (method.Value.Settings == ActivitySettings.SuppressInstrumentation)
                    EmitSuppressInstrumentationScope(method.Key, isVoid);
                else if (method.Value.Settings == ActivitySettings.ActivityName)
                    EmitActivityName(method.Key, isVoid, string.IsNullOrWhiteSpace(method.Value.Name)
                        ? $"{type.FullName}.{method.Key.Name}"
                        : method.Value.Name, method.Value.MaxUsableTimes);
                else if (method.Value.Settings == ActivitySettings.Activity)
                {
                    EmitActivity(method.Key, isVoid,
                        activitySource ??= AddActivitySource(activitySourceName, version),
                        string.IsNullOrWhiteSpace(method.Value.Name)
                            ? $"{activitySourceName}.{method.Key.Name}"
                            : method.Value.Name!, method.Value.Kind);

                    if (typeEmitted) continue;

                    type.CustomAttributes.Add(new(type.Module.ImportReference(Context.ProxyHasGeneratedAttributeCtor)));

                    assemblyEmitted = typeEmitted = true;
                }
            }
        }

        if (assemblyEmitted)
            Context.TargetModule.Assembly.CustomAttributes.Add(
                new(Context.TargetModule.ImportReference(Context.ProxyHasGeneratedAttributeCtor)));

        static IEnumerable<TypeDefinition> GetTypes(TypeDefinition type)
        {
            yield return type;

            foreach (var nestedNestedType in type.NestedTypes.SelectMany(GetTypes))
                yield return nestedNestedType;
        }
    }

    public FieldReference AddActivitySource(string name, string version)
    {
        if (_activitySource == null)
        {
            _activitySource = new(string.Empty, "@ActivitySource@",
                TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed)
            {
                BaseType = Context.TargetModule.TypeSystem.Object
            };

            _activitySource.CustomAttributes.Add(new(Context.CompilerGeneratedAttributeCtor));

            Context.TargetModule.Types.Add(_activitySource);
        }

        var activitySource = _activitySource.Fields.FirstOrDefault(f => f.Name == name);
        if (activitySource != null) return activitySource;

        activitySource = new(name, FieldAttributes.InitOnly | FieldAttributes.Static | FieldAttributes.Public,
            Context.ActivitySource);

        _activitySource.Fields.Add(activitySource);

        var cctor = _activitySource.GetStaticConstructor();
        if (cctor == null)
        {
            cctor = new(".cctor",
                MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName | MethodAttributes.Static, _activitySource.Module.TypeSystem.Void);

            _activitySource.Methods.Add(cctor);
        }
        else cctor.Body.Instructions.RemoveAt(cctor.Body.Instructions.Count - 1);

        cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, name));
        cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, version));
        cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, Context.ActivitySourceCtor));
        cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, activitySource));
        cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

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
        var hasAsyncStateMachineAttribute = method.GetCustomAttribute(Context.AsyncStateMachineAttribute) != null;

        if (!hasAsyncStateMachineAttribute) ReplaceBody(method, isVoid);

        method.Body.InitLocals = true;

        var isTypeAwaitable = CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo);
        hasAsyncStateMachineAttribute = hasAsyncStateMachineAttribute && isTypeAwaitable;

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

            var awaiterVariableIndex = method.Body.Variables.Count;

            method.Body.Variables.Add(
                new(method.Module.ImportReference(awaitableInfo.AwaitableInfo.AwaiterType, method)));

            GetAwaiter(method, ref index, awaitableInfo, leave);

            method.Body.Instructions.Insert(index++, Stloc(awaiterVariableIndex, method.Body.Variables));

            /*IL_0021: ldloca.s 2
              IL_0023: call instance bool valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<int32>::get_IsCompleted()*/
            method.Body.Instructions.Insert(index++,
                Ldloca(awaiterVariableIndex, awaitableInfo.AwaitableInfo.AwaiterType.IsValueType,
                    method.Body.Variables));

            method.Body.Instructions.Insert(index++, Instruction.Create(
                awaitableInfo.AwaitableInfo.AwaiterType.IsValueType ||
                awaitableInfo.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod.IsFinal
                    ? OpCodes.Call
                    : OpCodes.Callvirt,
                method.Module.ImportReference(awaitableInfo.AwaitableInfo.AwaiterIsCompletedPropertyGetMethod, method)
                    .MakeHostInstanceGeneric(awaitableInfo.AwaitableInfo.AwaiterType)));

            var brfalse = Ldloca(awaiterVariableIndex,
                awaitableInfo.AwaitableInfo.AwaiterType.IsValueType, method.Body.Variables);

            /*IL_0028: brfalse.s IL_0032

            IL_002a: ldloc.0
            IL_002b: callvirt instance void [mscorlib]System.IDisposable::Dispose()
            IL_0030: br.s IL_0046*/
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse, brfalse));
            method.Body.Instructions.Insert(index++, Ldloc(disposeVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.Dispose));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Br,
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
                Context.TargetModule.ImportReference(onCompleted, method)
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
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse, endFinally));
            method.Body.Instructions.Insert(index++, Ldloc(disposeVariableIndex, method.Body.Variables));
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, Context.Dispose));
            method.Body.Instructions.Insert(index, endFinally);
        }

        method.Body.OptimizeMacros();
    }

    public void EmitActivityName(MethodDefinition method, bool isVoid, string? activityName, int maxUsableTimes)
    {
        if (maxUsableTimes > 0)
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
        var hasAsyncStateMachineAttribute = method.GetCustomAttribute(Context.AsyncStateMachineAttribute) != null;

        if (!hasAsyncStateMachineAttribute) ReplaceBody(method, isVoid);

        method.Body.InitLocals = true;

        var isTypeAwaitable = CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo);
        hasAsyncStateMachineAttribute = hasAsyncStateMachineAttribute && isTypeAwaitable;

        var activityIndex = method.Body.Variables.Count;
        method.Body.Variables.Add(new(Context.Activity));

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

        var setActivityTags = SetActivityTags(method, activityIndex, ref length, out var returnValueTagName, out var isVoid2);

        var index = method.Body.Instructions.Count - (isVoid ? 1 : 2);

        if (leave != null)
        {
            if (!hasAsyncStateMachineAttribute)
            {
                var exceptionIndex = method.Body.Variables.Count;
                method.Body.Variables.Add(new(Context.Exception));

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

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse, leave));

            GetAwaiter(method, ref index, awaitableInfo, Ldloca(
                returnVariableIndex, awaitableInfo.AwaitableInfo.AwaitableType.IsValueType,
                method.Body.Variables));

            method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));

            SetActivityTags(method, ref index, setActivityTags);

            if (!isVoid2)
                method.Body.Instructions.Insert(index++, returnValueTagName == null
                    ? Instruction.Create(OpCodes.Ldnull)
                    : Instruction.Create(OpCodes.Ldstr, returnValueTagName));

            method.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Call,
                Context.ActivityAwaiterEmitter.GetActivityAwaiter(awaitableInfo.AwaitableInfo, isVoid2)));
        }
        else
        {
            var exceptionIndex = method.Body.Variables.Count;
            method.Body.Variables.Add(new(Context.Exception));

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
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse, skip));
                method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldstr, returnValueTagName));
                method.Body.Instructions.Insert(index++, Ldloc(returnVariableIndex, method.Body.Variables));

                foreach (var instruction in LdindAndBox(method.ReturnType))
                    method.Body.Instructions.Insert(index++, instruction);

                method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.ActivitySetTagEnumerable));
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
            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse, endFinally));
            method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));

            SetActivityTags(method, ref index, setActivityTags);

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.ActivityDispose));
            method.Body.Instructions.Insert(index, endFinally);
        }

        method.Body.OptimizeMacros();
    }

    private static void ReplaceBody(MethodDefinition method, bool isVoid)
    {
        if (!ShouldGenerateMethod(method.Body.Instructions, isVoid)) return;

        var newMethod = method.CreateCopyAndCleanBody(method.Name + "@");

        method.DeclaringType.Methods.Add(newMethod);

        if (!method.IsStatic)
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));

        for (var i = 0; i < method.Parameters.Count; i++)
            method.Body.Instructions.Add(Ldarg(i, method.IsStatic, method.Parameters));

        MethodReference mr = newMethod;

        if (method.DeclaringType.HasGenericParameters)
        {
            var t = new GenericInstanceType(method.DeclaringType);

            foreach (var p in method.DeclaringType.GenericParameters) t.GenericArguments.Add(p);

            mr = new GenericInstanceMethod(mr.MakeHostInstanceGeneric(t));
        }

        if (newMethod.HasGenericParameters)
        {
            if (mr is not GenericInstanceMethod m) mr = m = new(newMethod);

            foreach (var p in method.GenericParameters) m.GenericArguments.Add(p);
        }

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, mr));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }

    private static readonly IReadOnlyList<OpCode> SupportedJump = [OpCodes.Br_S, OpCodes.Leave_S, OpCodes.Br, OpCodes.Leave];

    private static bool ShouldGenerateMethod(IList<Instruction> instructions, bool isVoid)
    {
        var list = new List<Instruction>(2) { instructions[^1] };

        if (!isVoid && IsLdloc(instructions[^2])) list.Add(instructions[^2]);

        for (var index = instructions.Count - 2; index > 0; index--)
        {
            var instruction = instructions[index];

            // Unsupported jump
            if (list.Contains(instruction.Operand) && !SupportedJump.Contains(instruction.OpCode)) return true;
        }

        return false;
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
        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Brfalse, catchEnd));

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
    private static Tuple<int, Instruction?> ProcessReturn(MethodDefinition method, bool isVoid,
        bool hasAsyncStateMachineAttribute, Func<int, Instruction>? createLeave)
    {
        var (variableIndex, raw) = RetOrBr2LeaveS(method.Body, hasAsyncStateMachineAttribute, isVoid);
        if (variableIndex < 0 || createLeave == null) return Tuple.Create<int, Instruction?>(variableIndex, null);

        var leave = createLeave(variableIndex);
        for (var index = method.Body.Instructions.Count - 2; index > 0; index--)
        {
            var instruction = method.Body.Instructions[index];

            if (instruction.Operand == raw && SupportedJump.Contains(instruction.OpCode)) instruction.Operand = leave;
        }

        return Tuple.Create<int, Instruction?>(variableIndex, leave);

        static Tuple<int, Instruction> RetOrBr2LeaveS(MethodBody body, bool hasAsyncStateMachineAttribute, bool isVoid)
        {
            var checkLeaveS = body.ExceptionHandlers.Count < 1 ||
                body.ExceptionHandlers[^1].HandlerEnd !=
                body.Instructions[isVoid ? ^1 : ^2];

            var ret = body.Instructions[^1];
            if (isVoid)
            {
                if (hasAsyncStateMachineAttribute) return Tuple.Create(-1, ret);

                if (!SupportedJump.Contains(body.Instructions[^2].OpCode) && body.Instructions[^2].Operand != ret &&
                    checkLeaveS)
                    body.Instructions.Insert(body.Instructions.Count - 1, Instruction.Create(OpCodes.Leave, ret));

                for (var index = body.Instructions.Count - 2; index > 0; index--)
                {
                    var instruction = body.Instructions[index];
                    if (instruction.OpCode == OpCodes.Ret)
                    {
                        instruction.OpCode = OpCodes.Leave;
                        instruction.Operand = ret;
                    }
                    else if (SupportedJump.Contains(instruction.OpCode) && instruction.Operand == ret)
                        instruction.OpCode = OpCodes.Leave;
                }

                return Tuple.Create(-1, ret);
            }

            var ldRet = body.Instructions[^2];
            if (!IsLdloc(ldRet, body.Variables, out var variableIndex))
            {
                variableIndex = body.Variables.Count;

                body.Variables.Add(new(body.Method.ReturnType));

                var i = body.Instructions.Count - 1;
                body.Instructions.Insert(i, ldRet = Ldloc(variableIndex, body.Variables));
                if (!hasAsyncStateMachineAttribute)
                    body.Instructions.Insert(i, Instruction.Create(OpCodes.Leave, ldRet));

                body.Instructions.Insert(i, Stloc(variableIndex, body.Variables));
            }
            else if (!SupportedJump.Contains(body.Instructions[^3].OpCode) && body.Instructions[^3].Operand != ldRet &&
                     checkLeaveS)
                body.Instructions.Insert(body.Instructions.Count - 2, Instruction.Create(OpCodes.Leave, ldRet));

            for (var index = body.Instructions.Count - 3; index > 0; index--)
            {
                var instruction = body.Instructions[index];
                if (instruction.OpCode == OpCodes.Ret)
                {
                    instruction.OpCode = OpCodes.Leave;
                    instruction.Operand = ldRet;

                    if (IsLdloc(body.Instructions[index - 1])) body.Instructions.RemoveAt(--index);
                    else body.Instructions.Insert(index, Stloc(variableIndex, body.Variables));
                }
                else if (SupportedJump.Contains(instruction.OpCode))
                {
                    if (instruction.Operand == ldRet) instruction.OpCode = OpCodes.Leave;
                    else if (instruction.Operand == ret)
                    {
                        instruction.OpCode = OpCodes.Leave;
                        instruction.Operand = ldRet;

                        if (IsLdloc(body.Instructions[index - 1])) body.Instructions.RemoveAt(--index);
                        else body.Instructions.Insert(index, Stloc(variableIndex, body.Variables));
                    }
                }
            }

            return Tuple.Create(variableIndex, ldRet);
        }
    }

  private  static bool IsLdloc(Instruction instruction, Collection<VariableDefinition> variables, out int variableIndex)
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
            variableIndex = instruction.Operand is byte b
                ? b
                : variables.IndexOf((VariableDefinition)instruction.Operand);

            return true;
        }

        if (instruction.OpCode == OpCodes.Ldloc)
        {
            variableIndex = instruction.Operand is ushort b
                ? b
                : variables.IndexOf((VariableDefinition)instruction.Operand);

            return true;
        }

        variableIndex = -1;
        return false;
    }

    private static bool IsLdloc(Instruction instruction) =>
        instruction.OpCode == OpCodes.Ldloc_0 || instruction.OpCode == OpCodes.Ldloc_1 ||
        instruction.OpCode == OpCodes.Ldloc_2 || instruction.OpCode == OpCodes.Ldloc_3 ||
        instruction.OpCode == OpCodes.Ldloc_S || instruction.OpCode == OpCodes.Ldloc;

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

    private static void GetAwaiter(MethodDefinition method, ref int index,
        CoercedAwaitableInfo awaitableInfo, Instruction ldReturn)
    {
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
                method.Module.ImportReference(awaitableInfo.AwaitableInfo.GetAwaiterMethod, method)
                    .MakeHostInstanceGeneric(awaitableInfo.AwaitableInfo.AwaitableType)));
    }

    private bool GetActivityTags(MethodDefinition method, ref int index)
    {
        var list = GetActivityTags(method, (method.GetCustomAttribute(Context.ActivityNameAttribute) ??
                method.DeclaringType.GetCustomAttribute(Context.ActivityNameAttribute))?
            .GetValue<string[]>("Tags", new ArrayType(method.Module.TypeSystem.String))?.ToList(), out _, out _).Item1;

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

    private List<IReadOnlyList<Instruction>> SetActivityTags(MethodDefinition method, int activityIndex, ref int index,
        out string? returnValueTagName, out bool isVoid)
    {
        var (startInstructions, endInstructions) = GetActivityTags(method,
            method.GetCustomAttribute(Context.ActivityAttribute)?
                .GetValue<string[]>("Tags", new ArrayType(method.Module.TypeSystem.String))?.ToList(),
            out returnValueTagName, out isVoid);

        if (startInstructions.Count < 1) return endInstructions;

        method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));
        method.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Brfalse,
            method.Body.Instructions[index++]));

        method.Body.Instructions.Insert(index++, Ldloc(activityIndex, method.Body.Variables));

        SetActivityTags(method, ref index, startInstructions);

        method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Pop));

        return endInstructions;
    }

    private void SetActivityTags(MethodDefinition method, ref int index, List<IReadOnlyList<Instruction>> setActivityTags)
    {
        /*IL_0550: Ldloc.0
          IL_0573: ldstr        "aaa"
          IL_0574: ldarg.0
          IL_0578: callvirt       instance void SetTag*/
        foreach (var tag in setActivityTags)
        {
            foreach (var instruction in tag) method.Body.Instructions.Insert(index++, instruction);

            method.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Call, Context.ActivitySetTagEnumerable));
        }
    }

    internal Tuple<List<IReadOnlyList<Instruction>>, List<IReadOnlyList<Instruction>>>
        GetActivityTags(MethodDefinition method, ICollection<string>? tags, out string? returnValueTagName, out bool isVoid)
    {
        isVoid = method.ReturnType.HaveSameIdentity(Context.TargetModule.TypeSystem.Void) ||
            CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var awaitableInfo) &&
            awaitableInfo.AwaitableInfo.AwaiterGetResultMethod.ReturnType
                .HaveSameIdentity(Context.TargetModule.TypeSystem.Void);

        if (isVoid) returnValueTagName = null;
        else
            TryGetName(tags, method.MethodReturnType.GetCustomAttribute(Context.ActivityTagAttribute),
                "$returnvalue", out returnValueTagName);

        var startInstructions = new List<IReadOnlyList<Instruction>>();
        var endInstructions = new List<IReadOnlyList<Instruction>>();

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

                instructions.AddRange(LdindAndBox(field.FieldType));

                startInstructions.Add(instructions);
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
                    method.Module.ImportReference(property.GetMethod, method)));

                instructions.AddRange(LdindAndBox(property.PropertyType));

                startInstructions.Add(instructions);
            }

        for (var i = 0; i < method.Parameters.Count; i++)
        {
            var parameter = method.Parameters[i];

            if (TryGetName(tags, parameter.GetCustomAttribute(Context.ActivityTagAttribute), parameter.Name,
                    out var name))
            {
                /*IL_0558: ldstr        "123"
                  IL_055d: ldarg_0
                  IL_055d: ldarg_1*/
                var instructions = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldstr, name),
                    Ldarg(i, method.IsStatic, method.Parameters)
                };

                instructions.AddRange(LdindAndBox(parameter.ParameterType));

                if (parameter.IsOut) endInstructions.Add(instructions);
                else if (parameter is { IsIn: false, ParameterType.IsByReference: true })
                {
                    startInstructions.Add(instructions);

                    instructions = new(instructions)
                    {
                        [0] = Instruction.Create(OpCodes.Ldstr, name + "$out")
                    };

                    endInstructions.Add(instructions);
                }
                else startInstructions.Add(instructions);
            }
        }

        return Tuple.Create(startInstructions, endInstructions);
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

    private IEnumerable<Instruction> LdindAndBox(TypeReference type)
    {
        if (type.IsByReference)
        {
            type = type.GetElementType() ?? type;

            var typeSystem = Context.TargetModule.TypeSystem;

            Instruction? instruction = null;
            OpCode code = default;
            if (type == typeSystem.IntPtr || type == typeSystem.UIntPtr) code = OpCodes.Ldind_I;
            else if (type == typeSystem.Byte) code = OpCodes.Ldind_U1;
            else if (type == typeSystem.SByte) code = OpCodes.Ldind_I1;
            else if (type == typeSystem.Int16) code = OpCodes.Ldind_I2;
            else if (type == typeSystem.UInt16) code = OpCodes.Ldind_U2;
            else if (type == typeSystem.Int32) code = OpCodes.Ldind_I4;
            else if (type == typeSystem.UInt32) code = OpCodes.Ldind_U4;
            else if (type == typeSystem.Int64 || type == typeSystem.UInt64) code = OpCodes.Ldind_I8;
            else if (type == typeSystem.Single) code = OpCodes.Ldind_R4;
            else if (type == typeSystem.Double) code = OpCodes.Ldind_R8;
            else if (type.IsValueType) instruction = Instruction.Create(OpCodes.Ldobj, type);
            else code = OpCodes.Ldind_Ref;

            yield return instruction ?? Instruction.Create(code);
        }

        if (type.IsValueType || type.IsGenericParameter) yield return Instruction.Create(OpCodes.Box, type);
    }
}
