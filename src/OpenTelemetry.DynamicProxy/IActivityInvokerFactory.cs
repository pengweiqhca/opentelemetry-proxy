﻿namespace OpenTelemetry.DynamicProxy;

public interface IActivityInvokerFactory
{
    void Invoke(IInvocation invocation, InvokeContext context);
}
