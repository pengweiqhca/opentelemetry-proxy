using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Fody;

internal static class ActivityInvokerHelper
{
    /// <returns>0: Not a activity</returns>
    public static ProxyType<MethodDefinition> GetProxyType(TypeDefinition type, EmitContext context)
    {
        string? activitySourceName = null, name = null;
        var kind = -1;
        var includeNonAsyncStateMachineMethod = false;
        int? maxUsableTimes = null;

        if (type.GetCustomAttribute(context.ActivitySourceAttribute) is { } activitySource)
        {
            activitySourceName = activitySource.GetValue<string>("", type.Module.TypeSystem.String);

            kind = activitySource.GetValue<int>("Kind", type.Module.TypeSystem.Int32);
            includeNonAsyncStateMachineMethod = activitySource.GetValue<bool>("IncludeNonAsyncStateMachineMethod",
                type.Module.TypeSystem.Boolean);
        }
        else if (type.GetCustomAttribute(context.ActivityNameAttribute) is { } activityName)
        {
            name = activityName.GetValue<string>("", type.Module.TypeSystem.String);

            maxUsableTimes = activityName.GetValue("MaxUsableTimes", type.Module.TypeSystem.Int32, 1);
        }

        var proxyType = new ProxyType<MethodDefinition> { ActivitySourceName = activitySourceName };

        var propertyMethods = new HashSet<MethodDefinition>();

        foreach (var property in type.Properties)
        {
            if (property.GetMethod != null) propertyMethods.Add(property.GetMethod);
            if (property.SetMethod != null) propertyMethods.Add(property.SetMethod);
        }

        foreach (var method in type.GetMethods())
            if (!propertyMethods.Contains(method))
                proxyType.AddMethod(method, GetProxyMethod(method, context, kind,
                    includeNonAsyncStateMachineMethod, name, maxUsableTimes));

        return proxyType;
    }

    public static ProxyMethod GetProxyMethod(MethodDefinition method, EmitContext context, int kind,
        bool includeNonAsyncStateMachineMethod, string? activityName, int? maxUsableTimes)
    {
        if (method.GetCustomAttribute(context.NonActivityAttribute) is { } naa)
            return new(naa.GetValue<bool>("", method.Module.TypeSystem.Boolean)
                ? ActivitySettings.SuppressInstrumentation
                : ActivitySettings.None);

        if (method.GetCustomAttribute(context.ActivityAttribute) is { } attr)
            return new(ActivitySettings.Activity,
                Name: attr.GetValue<string>("", method.Module.TypeSystem.String),
                Kind: attr.GetValue<int>("Kind", method.Module.TypeSystem.Int32));

        if (method.GetCustomAttribute(context.ActivityNameAttribute) is not { } ana)
        {
            if (!method.IsPublic || method.GetCustomAttribute(context.CompilerGeneratedAttribute) != null)
                return new(ActivitySettings.None);

            if (maxUsableTimes is null)
                return kind < 0
                    ? new(ActivitySettings.None)
                    : new(includeNonAsyncStateMachineMethod ||
                        method.GetCustomAttribute(context.AsyncStateMachineAttribute) != null &&
                        CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out _)
                            ? ActivitySettings.Activity
                            : ActivitySettings.None, Kind: kind);

            if (!string.IsNullOrWhiteSpace(activityName))
                activityName = $"{activityName}.{method.Name}";
        }
        else
        {
            activityName = ana.GetValue<string>("", method.Module.TypeSystem.String);

            maxUsableTimes = ana.GetValue("MaxUsableTimes", method.Module.TypeSystem.Int32, 1);
        }

        return new(ActivitySettings.ActivityName, activityName, MaxUsableTimes: (int)maxUsableTimes);
    }
}
