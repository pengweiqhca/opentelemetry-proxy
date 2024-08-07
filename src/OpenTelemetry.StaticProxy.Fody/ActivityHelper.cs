﻿using Microsoft.Extensions.Internal;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Fody;

internal static class ActivityInvokerHelper
{
    /// <returns>0: Not an activity</returns>
    public static ProxyType<MethodDefinition> GetProxyType(TypeDefinition type, EmitContext context)
    {
        string? activitySourceName = null;
        Tuple<int, bool, bool>? tuple1 = null;
        Tuple<string?, int>? tuple2 = null;

        if (type.GetCustomAttribute(context.ActivitySourceAttribute) is { } activitySource)
        {
            activitySourceName = activitySource.GetValue<string>("", type.Module.TypeSystem.String);

            tuple1 = new(activitySource.GetValue<int>("Kind", type.Module.TypeSystem.Int32),
                activitySource.GetValue<bool>("IncludeNonAsyncStateMachineMethod", type.Module.TypeSystem.Boolean),
                activitySource.GetValue<bool>("SuppressInstrumentation", type.Module.TypeSystem.Boolean));
        }
        else if (type.GetCustomAttribute(context.ActivityNameAttribute) is { } activityName)
        {
            var maxUsableTimes = activityName.GetValue("MaxUsableTimes", type.Module.TypeSystem.Int32, 1);

            if (maxUsableTimes != 0)
                tuple2 = new(activityName.GetValue<string>("", type.Module.TypeSystem.String), maxUsableTimes);
        }

        var proxyType = new ProxyType<MethodDefinition> { ActivitySourceName = activitySourceName };

        foreach (var method in type.GetMethods())
            if (!method.IsSpecialName)
                proxyType.AddMethod(method, GetProxyMethod(method, context, tuple1, tuple2));

        return proxyType;
    }

    public static ProxyMethod GetProxyMethod(MethodDefinition method, EmitContext context,
        Tuple<int, bool, bool>? activitySource, Tuple<string?, int>? activityName)
    {
        if (method.GetCustomAttribute(context.NonActivityAttribute) is { } naa)
            return new(naa.GetValue<bool>("", method.Module.TypeSystem.Boolean)
                ? ActivitySettings.SuppressInstrumentation
                : ActivitySettings.None);

        if (method.GetCustomAttribute(context.ActivityAttribute) is { } attr)
            return new(attr.GetValue<bool>("SuppressInstrumentation", method.Module.TypeSystem.Boolean)
                    ? ActivitySettings.ActivityAndSuppressInstrumentation
                    : ActivitySettings.Activity,
                attr.GetValue<string>("", method.Module.TypeSystem.String),
                attr.GetValue<int>("Kind", method.Module.TypeSystem.Int32));

        if (method.GetCustomAttribute(context.ActivityNameAttribute) is not { } ana)
        {
            if (!method.IsPublic || method.GetCustomAttribute(context.CompilerGeneratedAttribute) != null)
                return default;

            if (activitySource != null)
                return activitySource.Item2 || method.GetCustomAttribute(context.AsyncStateMachineAttribute) != null &&
                    CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out _)
                        ? new(activitySource.Item3
                            ? ActivitySettings.ActivityAndSuppressInstrumentation
                            : ActivitySettings.Activity, Kind: activitySource.Item1)
                        : default;

            if (activityName == null) return default;

            if (!string.IsNullOrWhiteSpace(activityName.Item1))
                activityName = new($"{activityName.Item1}.{method.Name}", activityName.Item2);
        }
        else
        {
            var maxUsableTimes = ana.GetValue("MaxUsableTimes", method.Module.TypeSystem.Int32, 1);

            if (maxUsableTimes != 0)
                activityName = new(ana.GetValue<string>("", method.Module.TypeSystem.String), maxUsableTimes);
            else return default;
        }

        return new(ActivitySettings.ActivityName, activityName.Item1, MaxUsableTimes: activityName.Item2);
    }
}
