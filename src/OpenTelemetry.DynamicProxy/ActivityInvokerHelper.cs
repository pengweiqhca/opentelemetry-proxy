using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using System.Reflection.Emit;

namespace OpenTelemetry.DynamicProxy;

internal static class ActivityInvokerHelper
{
    public static IActivityInvoker Noop { get; } = new NoopActivityInvoker();

    private static readonly AssemblyBuilder DynamicAssembly;
    private static readonly ModuleBuilder DynamicModule;

    private static readonly Dictionary<Type, (Type, ConcurrentDictionary<Type, Type>?)> ActivityInvokerType = new();
    private static readonly Dictionary<Type, (Type, ConcurrentDictionary<Type, Type>?)> ActivityNameInvokerType = new();

    static ActivityInvokerHelper()
    {
        var name = typeof(ActivityInvokerHelper).Assembly.GetName().Name + ".Generated";
#if DEBUG && NETFRAMEWORK
        DynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new(name), AssemblyBuilderAccess.RunAndSave);

        DynamicModule = DynamicAssembly.DefineDynamicModule(name, name + ".dll");
#else
        DynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new(name), AssemblyBuilderAccess.Run);

        DynamicModule = DynamicAssembly.DefineDynamicModule(name);
#endif
    }

    /// <returns>0: Not a activity</returns>
    public static ActivitySettings GetActivityName(MethodInfo method, Type type, out string? activityName,
        out ActivityKind kind, out int maxUseableTimes)
    {
        activityName = null;
        kind = ActivityKind.Internal;
        maxUseableTimes = 0;

        if (method.GetCustomAttribute<NonActivityAttribute>(true) is { } naa)
            return naa.SuppressInstrumentation
                ? ActivitySettings.NonActivityAndSuppressInstrumentation
                : ActivitySettings.NonActivity;

        if (method.GetCustomAttribute<ActivityAttribute>(true) is { } attr)
        {
            activityName = attr.ActivityName;

            kind = attr.Kind;

            return ActivitySettings.Activity;
        }

        if (type.GetCustomAttribute<ActivitySourceAttribute>(true) is { } asa)
        {
            kind = asa.Kind;

            return ActivitySettings.Activity;
        }

        if (method.GetCustomAttribute<ActivityNameAttribute>(true) is not { } ana || ana.MaxUseableTimes < 1)
        {
            ana = type.GetCustomAttribute<ActivityNameAttribute>(true);

            if (ana == null || ana.MaxUseableTimes < 1) return ActivitySettings.NonActivity;

            if (!string.IsNullOrWhiteSpace(ana.ActivityName))
                activityName = $"{ana.ActivityName}.{method.Name}";
        }
        else activityName = ana.ActivityName;

        maxUseableTimes = ana.MaxUseableTimes;

        return ActivitySettings.ActivityNameOnly;
    }

    public static Type GetActivityInvokerType(Type returnType)
    {
        if (typeof(Task).IsAssignableFrom(returnType)) returnType = typeof(Task);

        var rawType = returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : returnType;

        if (ActivityInvokerType.TryGetValue(rawType, out var cache)) return GetType(cache, returnType);

        lock (ActivityInvokerType)
        {
            if (ActivityInvokerType.TryGetValue(rawType, out cache)) return GetType(cache, returnType);

            if (!CoercedAwaitableInfo.IsTypeAwaitable(rawType, out var info)) return typeof(ActivityInvoker);

            var tb = CreateType(typeof(ActivityInvoker), ActivityInvokerType.Count, rawType);

            ActivityInvoker.BuildAwaitableActivityInvoker(tb, rawType, info);

            return GetType(ActivityInvokerType[rawType] = BuildType(tb), returnType);
        }
    }

    public static Type GetActivityNameInvokerType(Type returnType)
    {
        if (typeof(Task).IsAssignableFrom(returnType)) returnType = typeof(Task);

        var rawType = returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : returnType;

        if (ActivityNameInvokerType.TryGetValue(rawType, out var cache)) return GetType(cache, returnType);

        lock (ActivityNameInvokerType)
        {
            if (ActivityNameInvokerType.TryGetValue(rawType, out cache)) return GetType(cache, returnType);

            if (!CoercedAwaitableInfo.IsTypeAwaitable(rawType, out var info)) return typeof(ActivityNameInvoker);

            var tb = CreateType(typeof(ActivityNameInvoker), ActivityNameInvokerType.Count, rawType);

            ActivityNameInvoker.BuildAwaitableActivityNameInvoker(tb, rawType, info);

            return GetType(ActivityNameInvokerType[rawType] = BuildType(tb), returnType);
        }
    }

    private static Type GetType((Type Type, ConcurrentDictionary<Type, Type>? Types) cache, Type type) =>
        cache.Types == null
            ? cache.Type
            : cache.Types.GetOrAdd(type, static (type, invokerType) =>
                invokerType.MakeGenericType(type.GetGenericArguments()), cache.Type);

    private static (Type, ConcurrentDictionary<Type, Type>?) BuildType(TypeBuilder tb)
    {
#if DEBUG && NETFRAMEWORK
        try
        {
            return (tb.CreateType(), tb.IsGenericTypeDefinition ? new ConcurrentDictionary<Type, Type>() : null);
        }
        finally
        {
            // DynamicAssembly.Save(DynamicAssembly.GetName().Name + ".dll");
        }
#else
        return (tb.CreateType()!, tb.IsGenericTypeDefinition ? new ConcurrentDictionary<Type, Type>() : null);
#endif
    }

    private static TypeBuilder CreateType(Type baseType, int count, Type rawType)
    {
        var tb = DynamicModule.DefineType(baseType.FullName + count,
            TypeAttributes.BeforeFieldInit, baseType);

        if (!rawType.IsGenericTypeDefinition) return tb;

        var parameters = rawType.GetTypeInfo().GenericTypeParameters;
        var parameterBuilders =
            tb.DefineGenericParameters(parameters.Select(static t => t.Name).ToArray());

        for (var index = 0; index < parameters.Length; index++)
        {
            parameterBuilders[index].SetGenericParameterAttributes(parameters[index].GenericParameterAttributes);

            if (parameters[index].BaseType != null)
                parameterBuilders[index].SetBaseTypeConstraint(parameters[index].BaseType);

            parameterBuilders[index].SetInterfaceConstraints(parameters[index].GetInterfaces());

            foreach (var attr in parameters[index].CustomAttributes)
                parameterBuilders[index].SetCustomAttribute(new(attr.Constructor,
                    attr.ConstructorArguments.Select(static a => a.Value).ToArray(),
                    attr.NamedArguments.Where(static a => !a.IsField).Select(static a => (PropertyInfo)a.MemberInfo)
                        .ToArray(),
                    attr.NamedArguments.Where(static a => !a.IsField).Select(static a => a.TypedValue.Value).ToArray(),
                    attr.NamedArguments.Where(static a => a.IsField).Select(static a => (FieldInfo)a.MemberInfo)
                        .ToArray(),
                    attr.NamedArguments.Where(static a => a.IsField).Select(static a => a.TypedValue.Value).ToArray()));
        }

        return tb;
    }
}
