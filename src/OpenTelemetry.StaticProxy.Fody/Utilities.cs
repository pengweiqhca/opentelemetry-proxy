using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace OpenTelemetry.StaticProxy.Fody;

internal static class Utilities
{
    private static readonly List<AssemblyNameReference> CoreLibRef = new()
    {
        new("System.Runtime", typeof(object).Assembly.GetName().Version)
        {
            PublicKeyToken = new byte[]
            {
                0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a
            }
        },
        new("System.Private.CoreLib", new(6,0,0,0))
        {
            PublicKeyToken = new byte[]
            {
                0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e
            }
        },
        new("netstandard", new(2, 0, 0, 0))
        {
            PublicKeyToken = new byte[]
            {
                0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51
            }
        },
        new("mscorlib", new(4, 0, 0, 0))
        {
            PublicKeyToken = new byte[]
            {
                0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89
            }
        }
    };

    private static readonly AssemblyNameReference FSharpCore = new("FSharp.Core", new(4, 0, 0, 0))
    {
        PublicKeyToken = new byte[]
        {
            0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a
        }
    };

    public static IEnumerable<MethodDefinition> GetMethods(this TypeDefinition type, string name)
    {
        var methods = type.GetMethods();

        while (type.BaseType != null) methods = methods.Union((type = type.BaseType.Resolve()).GetMethods());

        return methods.Where(m => m.Name == name);
    }

    public static MethodDefinition? GetParameterlessMethod(this TypeDefinition type, string name) =>
        type.GetMethods(name).FirstOrDefault(static m => m.Parameters.Count < 1);

    public static PropertyDefinition? GetProperty(this TypeDefinition type, string name) =>
        type.Properties.FirstOrDefault(p => p.Name == name) ?? type.BaseType?.Resolve().GetProperty(name);

    public static TypeReference GetCoreType<T>(this ModuleDefinition module) => module.GetCoreType(typeof(T));

    public static TypeReference GetCoreType(this ModuleDefinition module, Type type) =>
        new(type.Namespace, type.Name, module, module.TypeSystem.CoreLibrary)
        {
            IsValueType = type.IsValueType
        };

    public static CustomAttribute? GetCustomAttribute(this TypeDefinition type, TypeReference attributeType) =>
        type.CustomAttributes.FirstOrDefault(attr => attr.AttributeType.HaveSameIdentity(attributeType)) ??
        type.BaseType?.Resolve().GetCustomAttribute(attributeType);

    public static CustomAttribute? GetCustomAttribute(this MethodDefinition method, TypeReference attributeType)
    {
        var attr = method.CustomAttributes.FirstOrDefault(attr => attr.AttributeType.HaveSameIdentity(attributeType));
        if (attr != null) return attr;

        var @base = method.GetBaseMethod();
        return @base == null || @base == method ? null : @base.GetCustomAttribute(attributeType);
    }

    public static MethodReference MakeHostInstanceGeneric(this MethodReference self,
        TypeReference declaringType)
    {
        if (declaringType is not GenericInstanceType git || self.DeclaringType is GenericInstanceType) return self;

        var reference = new MethodReference(
            self.Name,
            self.ReturnType,
            self.DeclaringType.MakeGenericInstanceType(git.GenericArguments.ToArray()))
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

    public static bool HaveSameIdentity(this TypeReference type1, TypeReference type2)
    {
        if (!HaveSameIdentityOrCoreLib(type1.Scope, type2.Scope) ||
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
}
