using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Fody;

internal static class ActivityInvokerHelper
{
    /// <returns>0: Not a activity</returns>
    public static ProxyType<MethodDefinition> GetActivityName(TypeDefinition type, EmitContext context)
    {
        string? activitySourceName = null, name = null;
        var kind = -1;
        var includeNonAsyncStateMachineMethod = false;
        int? maxUsableTimes = null;

        if (type.GetCustomAttribute(context.ActivitySourceAttribute) is { } activitySource)
        {
            activitySourceName = GetValue<string>(activitySource, "", type.Module.TypeSystem.String);

            kind = GetValue<int>(activitySource, "Kind", type.Module.TypeSystem.Int32);
            includeNonAsyncStateMachineMethod = GetValue<bool>(activitySource, "IncludeNonAsyncStateMachineMethod", type.Module.TypeSystem.Boolean);
        }
        else if (type.GetCustomAttribute(context.ActivityNameAttribute) is { } activityName)
        {
            name = GetValue<string>(activityName, "", type.Module.TypeSystem.String);

            maxUsableTimes = GetValue(activityName, "MaxUsableTimes", type.Module.TypeSystem.Int32, 1);
        }

        var proxyType = new ProxyType<MethodDefinition>
        {
            ActivitySourceName = activitySourceName
        };

        foreach (var method in type.GetMethods())
            proxyType.AddMethod(method,
                GetActivityName(method, context, kind, includeNonAsyncStateMachineMethod, name, maxUsableTimes));

        return proxyType;
    }

    public static ProxyMethod GetActivityName(MethodDefinition method, EmitContext context, int kind,
        bool includeNonAsyncStateMachineMethod, string? activityName, int? maxUsableTimes)
    {
        if (method.GetCustomAttribute(context.NonActivityAttribute) is { } naa)
            return new(GetValue<bool>(naa, "", method.Module.TypeSystem.Boolean)
                ? ActivitySettings.NonActivityAndSuppressInstrumentation
                : ActivitySettings.NonActivity);

        if (method.GetCustomAttribute(context.ActivityAttribute) is { } attr)
            return new(ActivitySettings.Activity,
                Name: GetValue<string>(attr, "", method.Module.TypeSystem.String),
                Kind: GetValue<int>(attr, "Kind", method.Module.TypeSystem.Int32));

        if (method.GetCustomAttribute(context.ActivityNameAttribute) is not { } ana)
        {
            if (maxUsableTimes is null)
                return kind < 0 || !method.IsPublic
                    ? new(ActivitySettings.NonActivity)
                    : new(includeNonAsyncStateMachineMethod ||
                        method.GetCustomAttribute(context.AsyncStateMachineAttribute) != null &&
                        CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out _)
                            ? ActivitySettings.Activity
                            : ActivitySettings.NonActivity, Kind: kind);

            if (!string.IsNullOrWhiteSpace(activityName))
                activityName = $"{activityName}.{method.Name}";
        }
        else
        {
            activityName = GetValue<string>(ana, "", method.Module.TypeSystem.String);

            maxUsableTimes = GetValue(ana, "MaxUsableTimes", method.Module.TypeSystem.Int32, 1);
        }

        return new(ActivitySettings.ActivityNameOnly, activityName, MaxUsableTimes: (int)maxUsableTimes);
    }

    // https://www.meziantou.net/working-with-types-in-a-roslyn-analyzer.htm
    private static T? GetValue<T>(ICustomAttribute attr, string property, TypeReference type, T? defaultValue = default)
    {
        foreach (var arg in attr.ConstructorArguments.Where(a => a.Type == type)) return (T)arg.Value;

        foreach (var p in attr.Properties.Where(p => p.Name == property)) return (T)p.Argument.Value;

        foreach (var f in attr.Fields.Where(f => f.Name == property)) return (T)f.Argument.Value;

        return defaultValue;
    }
}
