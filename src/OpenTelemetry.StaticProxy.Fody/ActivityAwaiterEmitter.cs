using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Text;
using TypeCache =
    System.ValueTuple<Mono.Cecil.TypeReference, Mono.Cecil.MethodReference, Mono.Cecil.MethodReference,
        Mono.Cecil.MethodReference>;

namespace OpenTelemetry.StaticProxy.Fody;

internal class ActivityAwaiterEmitter
{
    private static readonly string CoreLibKey = Guid.NewGuid().ToString();

    private readonly Dictionary<TypeReference, TypeCache> _awaiterTypes = new(new TypeReferenceComparer());
    private readonly EmitContext _context;

    public ActivityAwaiterEmitter(EmitContext context) => _context = context;

    public (MethodReference ctor, MethodReference instanceOnCompleted, MethodReference staticOnCompleted)
        GetActivityAwaiter(TypeReference awaiterType, MethodReference getResult)
    {
        ICollection<TypeReference> genericArguments = Array.Empty<TypeReference>();
        var elementType = awaiterType;
        if (awaiterType is GenericInstanceType git)
        {
            elementType = git.ElementType;

            genericArguments = git.GenericArguments;
        }

        if (!_awaiterTypes.TryGetValue(elementType, out var awaiter))
            _awaiterTypes[elementType] = awaiter = CreateActivityAwaiter(elementType, getResult);

        if (genericArguments.Count < 1) return (awaiter.Item2, awaiter.Item3, awaiter.Item4);

        awaiterType = awaiter.Item1.MakeGenericInstanceType(genericArguments.ToArray());

        return (awaiter.Item2.MakeHostInstanceGeneric(awaiterType), awaiter.Item3.MakeHostInstanceGeneric(awaiterType),
            awaiter.Item4.MakeHostInstanceGeneric(awaiterType));
    }

    private TypeCache CreateActivityAwaiter(TypeReference awaiterType, MethodReference getResult)
    {
        var type = new TypeDefinition("", "@ActivityAwaiter@" + _awaiterTypes.Count,
            TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit)
        {
            IsBeforeFieldInit = true,
            BaseType = _context.TargetModule.TypeSystem.Object
        };

        type.CustomAttributes.Add(new(_context.CompilerGeneratedAttributeCtor));

        _context.TargetModule.Types.Add(type);

        foreach (var genericParameter in awaiterType.GenericParameters) type.GenericParameters.Add(genericParameter);

        if (awaiterType.HasGenericParameters)
            awaiterType = awaiterType.MakeGenericInstanceType(awaiterType.GenericParameters.ToArray<TypeReference>());

        #region field and method

        /*.field private initonly class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity _activity
    .field private initonly valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!T> _awaiter*/
        type.Fields.Add(new("_activity", FieldAttributes.Private | FieldAttributes.InitOnly,
            _context.Activity));

        type.Fields.Add(new("_awaiter", FieldAttributes.Private | FieldAttributes.InitOnly, awaiterType));

        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName, _context.TargetModule.TypeSystem.Void)
        {
            Parameters =
            {
                new ParameterDefinition("activity", ParameterAttributes.None, _context.Activity),
                new ParameterDefinition("awaiter", ParameterAttributes.None, awaiterType),
            },
        };

        type.Methods.Add(ctor);

        var onCompleted = new MethodDefinition("OnCompleted", MethodAttributes.Public | MethodAttributes.HideBySig,
            _context.TargetModule.TypeSystem.Void);

        type.Methods.Add(onCompleted);

        var onCompletedStatic = new MethodDefinition("OnCompleted",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static,
            _context.TargetModule.TypeSystem.Void)
        {
            Parameters =
            {
                new ParameterDefinition("activity", ParameterAttributes.None, _context.Activity),
                new ParameterDefinition("awaiter", ParameterAttributes.None, awaiterType),
            },
            Body =
            {
                InitLocals = true
            }
        };

        type.Methods.Add(onCompletedStatic);

        TypeReference type2 = type.HasGenericParameters
            ? type.MakeGenericInstanceType(type.GenericParameters.ToArray<TypeReference>())
            : type;

        var activity = new FieldReference("_activity", _context.Activity, type2);
        var awaiter = new FieldReference("_awaiter", awaiterType, type2);

        #endregion

        #region ctor

        /*IL_0000: ldarg.0
    IL_0001: call instance void [mscorlib]System.Object::.ctor()
    IL_0006: ldarg.0
    IL_0007: ldarg.1
    IL_0008: stfld class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<!T>::_activity
    IL_000d: ldarg.0
    IL_000e: ldarg.2
    IL_000f: stfld valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!0> class OpenTelemetry.StaticProxy.Fody.Tests.TestClass/ActivityAwaiter`1<!T>::_awaiter*/
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, _context.ObjectCtor));

        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, _context.TargetModule.ImportReference(activity)));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, _context.TargetModule.ImportReference(awaiter)));

        /*IL_0014: ldarg.1
        IL_0015: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::get_Parent()
        IL_001a: brfalse.s IL_0027

        IL_001c: ldarg.1
        IL_001d: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::get_Parent()
        IL_0022: call void [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::set_Current(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity)

        IL_0027: ret*/
        var ret = Instruction.Create(OpCodes.Ret);

        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, _context.ActivityGetParent));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse_S, ret));

        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, _context.ActivityGetParent));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, _context.ActivitySetCurrent));

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
        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld,
            _context.TargetModule.ImportReference(activity)));

        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld,
            _context.TargetModule.ImportReference(awaiter)));

        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Call,
            new MethodReference("OnCompleted", _context.TargetModule.TypeSystem.Void, type2)
            {
                Parameters =
                {
                    new ParameterDefinition(_context.Activity),
                    new ParameterDefinition(awaiterType),
                }
            }));

        onCompleted.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        #endregion

        #region static OnCompleted

        /*.try
        {
            .try
            {
                IL_0000: ldarga.s awaiter
                IL_0002: call instance !0 valuetype [System.Threading.Tasks.Extensions]System.Runtime.CompilerServices.ValueTaskAwaiter`1<!T>::GetResult()
                IL_0007: pop
                IL_0008: leave.s IL_0027
            } // end .try
            catch [mscorlib]System.Exception
            {
                IL_000a: stloc.0
                IL_000b: ldarg.0
                IL_000c: ldc.i4.2
                IL_000d: ldloc.0
                IL_000e: callvirt instance string [mscorlib]System.Exception::get_Message()
                IL_0013: callvirt instance class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::SetStatus(valuetype [System.Diagnostics.DiagnosticSource]System.Diagnostics.ActivityStatusCode, string)
                IL_0018: ldloc.0
                IL_0019: call void [OpenTelemetry.Api]OpenTelemetry.Trace.ActivityExtensions::RecordException(class [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity, class [mscorlib]System.Exception)
                IL_001e: leave.s IL_0027
            } // end handler
        } // end .try
        finally
        {
            IL_0020: ldarg.0
            IL_0021: callvirt instance void [System.Diagnostics.DiagnosticSource]System.Diagnostics.Activity::Dispose()
            IL_0026: endfinally
        } // end handler

        IL_0027: ret*/
        onCompletedStatic.Body.Variables.Add(new(_context.Exception));

        var leave = Instruction.Create(OpCodes.Ret);

        var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = Instruction.Create(awaiterType.IsValueType ? OpCodes.Ldarga_S : OpCodes.Ldarg_S,
                onCompletedStatic.Parameters[1]),
            HandlerStart = Instruction.Create(OpCodes.Ldarg_0),
            HandlerEnd = leave
        };

        finallyHandler.TryEnd = finallyHandler.HandlerStart;

        var catchHandler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = finallyHandler.TryStart,
            CatchType = _context.Exception,
            HandlerStart = Instruction.Create(OpCodes.Stloc_0),
            HandlerEnd = finallyHandler.HandlerStart
        };

        catchHandler.TryEnd = catchHandler.HandlerStart;

        onCompletedStatic.Body.ExceptionHandlers.Add(catchHandler);
        onCompletedStatic.Body.ExceptionHandlers.Add(finallyHandler);

        onCompletedStatic.Body.Instructions.Add(finallyHandler.TryStart);

        onCompletedStatic.Body.Instructions.Add(Instruction.Create(awaiterType.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
            getResult.MakeHostInstanceGeneric(awaiterType)));

        if (!getResult.ReturnType.HaveSameIdentity(_context.TargetModule.TypeSystem.Void))
            onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));

        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Leave_S, leave));

        onCompletedStatic.Body.Instructions.Add(catchHandler.HandlerStart);
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, _context.GetMessage));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Call, _context.ActivitySetStatus));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Call, _context.RecordException));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Leave_S, leave));

        onCompletedStatic.Body.Instructions.Add(finallyHandler.HandlerStart);
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, _context.ActivityDispose));
        onCompletedStatic.Body.Instructions.Add(Instruction.Create(OpCodes.Endfinally));

        onCompletedStatic.Body.Instructions.Add(leave);

        #endregion

        return (type, ctor, onCompleted, onCompletedStatic);
    }

    private sealed class TypeReferenceComparer : IEqualityComparer<TypeReference>
    {
        public bool Equals(TypeReference? x, TypeReference? y) =>
            x is null ? y is null : y is not null && GetName(x) == GetName(y);

        private static string GetName(TypeReference type)
        {
            var sb = new StringBuilder();
            sb.Append(type.FullName).Append(",").Append(type.Scope.Name);
            if (type.Scope is ModuleReference m)
                sb.Append(",").Append(m.MetadataToken.ToUInt32());
            else
            {
                if (type.Scope is not AssemblyNameReference a)
                {
                    if (type.Scope is ModuleDefinition d) a = d.Assembly.Name;
                    else return sb.ToString();
                }

                sb.Append(",");

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
