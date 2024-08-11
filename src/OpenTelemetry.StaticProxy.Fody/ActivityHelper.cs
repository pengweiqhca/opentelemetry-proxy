using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using OpenTelemetry.Proxy;
using System.Text.RegularExpressions;

namespace OpenTelemetry.StaticProxy.Fody;

internal static class ActivityInvokerHelper
{
    /// <returns>0: Not an activity</returns>
    public static ProxyType<MethodDefinition> GetProxyType(TypeDefinition type, EmitContext context)
    {
        var (tuple1, tuple2) = GetTypeAttribute(type, context);

        var proxyType = new ProxyType<MethodDefinition> { ActivitySourceName = tuple1?.Item4 };

        foreach (var method in type.GetMethods())
            if (!method.IsSpecialName)
                proxyType.AddMethod(method, GetProxyMethod(type, method, context, tuple1, tuple2));

        return proxyType;
    }

    public static Tuple<Tuple<int, bool, bool, string?>?, Tuple<string?, int>?> GetTypeAttribute(TypeDefinition type,
        EmitContext context)
    {
        Tuple<int, bool, bool, string?>? tuple1 = null;
        Tuple<string?, int>? tuple2 = null;

        if (type.GetCustomAttribute(context.ActivitySourceAttribute) is { } activitySource)
        {
            var activitySourceName = activitySource.GetValue<string>("", type.Module.TypeSystem.String);

            tuple1 = new(activitySource.GetValue<int>("Kind", type.Module.TypeSystem.Int32),
                activitySource.GetValue<bool>("IncludeNonAsyncStateMachineMethod", type.Module.TypeSystem.Boolean),
                activitySource.GetValue<bool>("SuppressInstrumentation", type.Module.TypeSystem.Boolean),
                activitySourceName);
        }

        if (type.GetCustomAttribute(context.ActivityNameAttribute) is { } activityName)
        {
            var maxUsableTimes = activityName.GetValue("MaxUsableTimes", type.Module.TypeSystem.Int32, 1);

            if (maxUsableTimes != 0)
                tuple2 = new(activityName.GetValue<string>("", type.Module.TypeSystem.String), maxUsableTimes);
        }

        return Tuple.Create(tuple1, tuple2);
    }

    public static IProxyMethod? GetProxyMethod(TypeDefinition type, MethodDefinition method, EmitContext context,
        Tuple<int, bool, bool, string?>? activitySource, Tuple<string?, int>? activityName)
    {
        if (method.GetCustomAttribute(context.NonActivityAttribute) is { } naa)
            return naa.GetValue<bool>("", method.Module.TypeSystem.Boolean)
                ? SuppressInstrumentationMethod.Instance
                : null;

        if (method.GetCustomAttribute(context.ActivityAttribute) is { } attr)
            return new ActivityMethod(
                GetActivityName(context, method, type, attr.GetValue<string>("", method.Module.TypeSystem.String)),
                attr.GetValue<int>("Kind", method.Module.TypeSystem.Int32),
                attr.GetValue<bool>("SuppressInstrumentation", method.Module.TypeSystem.Boolean));

        if (method.GetCustomAttribute(context.ActivityNameAttribute) is not { } ana ||
            ana.GetValue("MaxUsableTimes", method.Module.TypeSystem.Int32, 1) is var maxUsableTimes &&
            maxUsableTimes == 0)
        {
            if (!type.IsInterface && !method.IsPublic ||
                method.GetCustomAttribute(context.CompilerGeneratedAttribute) != null)
                return null;

            if (activitySource != null)
                return activitySource.Item2 || method.GetCustomAttribute(context.AsyncStateMachineAttribute) != null &&
                    CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out _)
                        ? new ActivityMethod(GetActivityName(context, method, type, null, activitySource.Item4),
                            activitySource.Item1, activitySource.Item3)
                        : null;

            return activityName == null
                ? null
                : new ActivityNameMethod(GetActivityName(context, method, type, null, activityName.Item1), activityName.Item2);
        }

        return new ActivityNameMethod(GetActivityName(context, method, type,
            ana.GetValue<string>("", method.Module.TypeSystem.String),
            activitySource == null ? activityName?.Item1 : null), maxUsableTimes);
    }

    public static string GetActivityName(EmitContext context, MethodDefinition method, TypeDefinition type,
        string? activityName, string? activityBaseName = null)
    {
        if (!string.IsNullOrWhiteSpace(activityName)) return activityName!;

        if (string.IsNullOrWhiteSpace(activityBaseName)) activityBaseName = type.Name;

        if (method.GetCustomAttribute(context.CompilerGeneratedAttribute) == null)
            return $"{activityBaseName}.{method.Name}";

        // Match inline method name
        var match = Regex.Match(method.Name, @"^<(?<Method>\w+)>g__(?<InlineMethod>\w+)\|\d+_\d+$");

        return match.Success
            ? $"{activityBaseName}.{match.Groups["Method"].Value}+{match.Groups["InlineMethod"].Value}"
            : $"{activityBaseName}.{method.Name}";
    }
}
