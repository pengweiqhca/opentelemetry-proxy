using Mono.Cecil;
using Mono.Cecil.Rocks;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace OpenTelemetry.StaticProxy.Fody;

internal static class Utilities
{
    private static readonly List<AssemblyNameReference> CoreLibRef =
    [
        new("System.Runtime", typeof(object).Assembly.GetName().Version)
        {
            PublicKeyToken = [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a]
        },

        new("System.Private.CoreLib", new(6, 0, 0, 0))
        {
            PublicKeyToken = [0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e]
        },

        new("netstandard", new(2, 0, 0, 0))
        {
            PublicKeyToken = [0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51]
        },

        new("mscorlib", new(4, 0, 0, 0))
        {
            PublicKeyToken = [0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89]
        }
    ];

    private static readonly AssemblyNameReference FSharpCore = new("FSharp.Core", new(4, 0, 0, 0))
    {
        PublicKeyToken = [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a]
    };

    public static IEnumerable<MethodDefinition> GetMethods(this TypeDefinition type, string name)
    {
        var methods = type.GetMethods();

        while (type.BaseType != null)
        {
            type = type.BaseType.Resolve();

            if (type != null) methods = methods.Union(type.GetMethods());
            else break;
        }

        return methods.Where(m => m.Name == name);
    }

    public static MethodDefinition? GetParameterlessMethod(this TypeDefinition type, string name) =>
        type.GetMethods(name).FirstOrDefault(static m => m.Parameters.Count < 1);

    public static PropertyDefinition? GetProperty(this TypeDefinition type, string name) =>
        type.Properties.FirstOrDefault(p => p.Name == name) ?? type.BaseType?.Resolve()?.GetProperty(name);

    public static TypeReference GetCoreType<T>(this ModuleDefinition module) => module.GetCoreType(typeof(T));

    public static TypeReference GetCoreType(this ModuleDefinition module, Type type) =>
        new(type.Namespace, type.Name, module, module.TypeSystem.CoreLibrary)
        {
            IsValueType = type.IsValueType
        };

    /*public static CustomAttribute? GetCustomAttribute(this TypeDefinition? type, TypeReference attributeType)
    {
        while (type != null)
        {
            var customAttribute = GetCustomAttribute((ICustomAttributeProvider)type, attributeType);
            if (customAttribute != null) return customAttribute;

            type = type.BaseType?.Resolve();
        }

        return null;
    }

    public static CustomAttribute? GetCustomAttribute(this MethodDefinition? method, TypeReference attributeType)
    {
        while (method != null)
        {
            var attr = GetCustomAttribute((ICustomAttributeProvider)method, attributeType);
            if (attr != null) return attr;

            var @base = method.GetBaseMethod();
            if (@base == null || @base == method) return null;

            method = @base;
        }

        return null;
    }*/

    public static CustomAttribute? GetCustomAttribute(this ICustomAttributeProvider provider,
        TypeReference attributeType) =>
        provider.CustomAttributes.FirstOrDefault(attr => attr.AttributeType.HaveSameIdentity(attributeType));

    // https://www.meziantou.net/working-with-types-in-a-roslyn-analyzer.htm
    public static T? GetValue<T>(this ICustomAttribute attr, string property, TypeReference type,
        T? defaultValue = default)
    {
        foreach (var arg in attr.ConstructorArguments.Where(a => a.Type.HaveSameIdentity(type))) return ReadValue(arg.Value);

        foreach (var p in attr.Properties.Where(p => p.Name == property)) return ReadValue(p.Argument.Value);

        foreach (var f in attr.Fields.Where(f => f.Name == property)) return ReadValue(f.Argument.Value);

        return defaultValue;

        static T? ReadValue(object? value)
        {
            if (value is not CustomAttributeArgument[] args || !typeof(T).IsArray) return (T?)value;

            var array = Array.CreateInstance(typeof(T).GetElementType()!, args.Length);

            for (var index = 0; index < args.Length; index++) array.SetValue(args[index].Value, index);

            return (T?)(object)array;
        }
    }

    public static MethodReference MakeHostInstanceGeneric(this MethodReference self,
        TypeReference declaringType)
    {
        if (declaringType is not GenericInstanceType git || self.DeclaringType is GenericInstanceType) return self;

        var reference = new MethodReference(
            self.Name,
            self.ReturnType,
            self.DeclaringType.MakeGenericInstanceType([.. git.GenericArguments]))
        {
            HasThis = self.HasThis,
            ExplicitThis = self.ExplicitThis,
            CallingConvention = self.CallingConvention
        };

        foreach (var parameter in self.Parameters) reference.Parameters.Add(new(parameter.ParameterType));

        foreach (var genericParam in self.GenericParameters)
            reference.GenericParameters.Add(new(genericParam.Name, reference));

        return reference;
    }

    public static TypeReference MakeHostInstanceGeneric(this TypeReference self,
        TypeReference declaringType)
    {
        if (declaringType is not GenericInstanceType git) return self;

        var reference = new GenericInstanceType(self.GetElementType());

        foreach (var type in git.GenericArguments) reference.GenericArguments.Add(type);

        return reference;
    }

    public static bool HaveSameIdentity(this TypeReference type1, TypeReference? type2)
    {
        if (type2 == null ||
            !HaveSameIdentityOrCoreLib(type1.Scope, type2.Scope) ||
            !string.Equals(type1.Namespace, type2.Namespace, StringComparison.Ordinal) ||
            !string.Equals(type1.Name, type2.Name, StringComparison.Ordinal)) return false;

        if (type1 is not GenericInstanceType gt1) return type2 is not GenericInstanceType;

        if (type2 is not GenericInstanceType gt2 ||
            gt1.GenericArguments.Count != gt2.GenericArguments.Count) return false;

        if (gt1.GenericArguments.Where((t, index) => !HaveSameIdentity(t, gt2.GenericArguments[index])).Any())
            return false;

        if (type1.DeclaringType == null) return type2.DeclaringType == null;

        return type2.DeclaringType != null && HaveSameIdentity(type1.DeclaringType, type2.DeclaringType);
    }

    private static bool HaveSameIdentityOrCoreLib(this IMetadataScope scope1, IMetadataScope scope2) =>
        HaveSameIdentity(scope1, scope2) || IsCoreLib(scope1) && IsCoreLib(scope2);

    public static bool HaveSameIdentity(this IMetadataScope scope1, IMetadataScope scope2)
    {
        if (scope1 == scope2) return true;

        if (scope1 is ModuleDefinition md1) scope1 = md1.Assembly.Name;
        if (scope2 is ModuleDefinition md2) scope2 = md2.Assembly.Name;

        if (scope1.MetadataScopeType != scope2.MetadataScopeType) return false;

        return scope1 is AssemblyNameReference anr1 && scope2 is AssemblyNameReference anr2
            ? string.Equals(anr1.Name, anr2.Name, StringComparison.Ordinal) &&
            string.Equals(BitConverter.ToString(anr1.PublicKeyToken), BitConverter.ToString(anr2.PublicKeyToken),
                StringComparison.OrdinalIgnoreCase)
            : scope1 == scope2;
    }

    public static bool IsFSharpCore(this IMetadataScope scope) => HaveSameIdentity(scope, FSharpCore);

    public static bool IsCoreLib(this IMetadataScope scope) => CoreLibRef.Any(x => x.HaveSameIdentity(scope));

    // https://github.com/vescon/MethodBoundaryAspect.Fody/blob/master/src/MethodBoundaryAspect.Fody/MethodWeaver.cs#L84
    public static MethodDefinition CreateCopy(this MethodDefinition rawMethod, string newMethodName)
    {
        var newMethod = new MethodDefinition(newMethodName, rawMethod.Attributes, rawMethod.ReturnType)
        {
            AggressiveInlining = true, // try to get rid of additional stack frame
            HasThis = rawMethod.HasThis,
            ExplicitThis = rawMethod.ExplicitThis,
            CallingConvention = rawMethod.CallingConvention
        };

        newMethod.Attributes |= MethodAttributes.Private | MethodAttributes.Final;

        newMethod.Attributes &= ~(MethodAttributes.Public | MethodAttributes.Family | MethodAttributes.Virtual);

        foreach (var parameter in rawMethod.Parameters)
            newMethod.Parameters.Add(parameter);

        if (rawMethod.HasGenericParameters)
            //Contravariant:
            //  The generic type parameter is contravariant. A contravariant type parameter can appear as a parameter type in method signatures.
            //Covariant:
            //  The generic type parameter is covariant. A covariant type parameter can appear as the result type of a method, the type of a read-only field, a declared base type, or an implemented interface.
            //DefaultConstructorConstraint:
            //  A type can be substituted for the generic type parameter only if it has a parameterless constructor.
            //None:
            //  There are no special flags.
            //NotNullableValueTypeConstraint:
            //  A type can be substituted for the generic type parameter only if it is a value type and is not nullable.
            //ReferenceTypeConstraint:
            //  A type can be substituted for the generic type parameter only if it is a reference type.
            //SpecialConstraintMask:
            //  Selects the combination of all special constraint flags. This value is the result of using logical OR to combine the following flags: DefaultConstructorConstraint, ReferenceTypeConstraint, and NotNullableValueTypeConstraint.
            //VarianceMask:
            //  Selects the combination of all variance flags. This value is the result of using logical OR to combine the following flags: Contravariant and Covariant.
            foreach (var parameter in rawMethod.GenericParameters)
            {
                var clonedParameter = new GenericParameter(parameter.Name, newMethod);
                if (parameter.HasConstraints)
                    foreach (var parameterConstraint in parameter.Constraints)
                    {
                        clonedParameter.Attributes = parameter.Attributes;
                        clonedParameter.Constraints.Add(parameterConstraint);
                    }

                if (parameter.HasReferenceTypeConstraint)
                {
                    clonedParameter.Attributes |= GenericParameterAttributes.ReferenceTypeConstraint;
                    clonedParameter.HasReferenceTypeConstraint = true;
                }

                if (parameter.HasNotNullableValueTypeConstraint)
                {
                    clonedParameter.Attributes |= GenericParameterAttributes.NotNullableValueTypeConstraint;
                    clonedParameter.HasNotNullableValueTypeConstraint = true;
                }

                if (parameter.HasDefaultConstructorConstraint)
                {
                    clonedParameter.Attributes |= GenericParameterAttributes.DefaultConstructorConstraint;
                    clonedParameter.HasDefaultConstructorConstraint = true;
                }

                newMethod.GenericParameters.Add(clonedParameter);
            }

        if (!rawMethod.HasBody) return newMethod;

        newMethod.Body.InitLocals = rawMethod.Body.InitLocals;

        foreach (var variableDefinition in rawMethod.Body.Variables)
            newMethod.Body.Variables.Add(variableDefinition);

        foreach (var exceptionHandler in rawMethod.Body.ExceptionHandlers)
            newMethod.Body.ExceptionHandlers.Add(exceptionHandler);

        var targetProcessor = newMethod.Body.GetILProcessor();

        foreach (var instruction in rawMethod.Body.Instructions)
            targetProcessor.Append(instruction);

        if (rawMethod.DebugInformation.HasSequencePoints)
            foreach (var sequencePoint in rawMethod.DebugInformation.SequencePoints)
                newMethod.DebugInformation.SequencePoints.Add(sequencePoint);

        newMethod.DebugInformation.Scope = new(rawMethod.Body.Instructions.First(), rawMethod.Body.Instructions.Last());

        if (rawMethod.DebugInformation?.Scope?.Variables != null)
            foreach (var variableDebugInformation in rawMethod.DebugInformation.Scope.Variables)
                newMethod.DebugInformation.Scope.Variables.Add(variableDebugInformation);

        newMethod.Body.OptimizeMacros();

        return newMethod;
    }

    public static void Clean(this MethodDefinition method)
    {
        var body = method.Body;

        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.Instructions.Clear();
    }

    public static TypeReference FixTypeReference(TypeReference typeReference)
    {
        if (!typeReference.HasGenericParameters)
            return typeReference;

        // workaround for method in generic type
        // https://stackoverflow.com/questions/4968755/mono-cecil-call-generic-base-class-method-from-other-assembly
        var genericParameters = typeReference.GenericParameters
            .Select(x => x.GetElementType())
            .ToArray();

        return typeReference.MakeGenericType(genericParameters);
    }

    public static MethodReference FixMethodReference(TypeReference declaringType, MethodReference targetMethod)
    {
        // Taken and adapted from
        // https://stackoverflow.com/questions/4968755/mono-cecil-call-generic-base-class-method-from-other-assembly
        if (targetMethod is MethodDefinition)
        {
            var newTargetMethod = new MethodReference(targetMethod.Name, targetMethod.ReturnType, declaringType)
            {
                HasThis = targetMethod.HasThis,
                ExplicitThis = targetMethod.ExplicitThis,
                CallingConvention = targetMethod.CallingConvention
            };

            foreach (var p in targetMethod.Parameters)
                newTargetMethod.Parameters.Add(new(p.Name, p.Attributes, p.ParameterType));

            foreach (var gp in targetMethod.GenericParameters)
                newTargetMethod.GenericParameters.Add(new(gp.Name, newTargetMethod));

            targetMethod = newTargetMethod;
        }
        else
            targetMethod.DeclaringType = declaringType;

        return targetMethod.HasGenericParameters ? targetMethod.MakeGeneric() : targetMethod;
    }

    public static TypeReference MakeGenericType(this TypeReference self, params TypeReference[] arguments)
    {
        if (self.GenericParameters.Count != arguments.Length)
            throw new ArgumentException("self.GenericParameters.Count != arguments.Length", nameof(arguments));

        var instance = new GenericInstanceType(self);

        foreach (var argument in arguments)
            instance.GenericArguments.Add(argument);

        return instance;
    }

    public static MethodReference MakeGeneric(this MethodReference self, params TypeReference[] arguments)
    {
        var baseReference = self.DeclaringType.Module.ImportReference(self);
        var reference = new GenericInstanceMethod(baseReference);

        foreach (var genericParameter in baseReference.GenericParameters)
            reference.GenericArguments.Add(genericParameter);

        return reference;
    }
}
