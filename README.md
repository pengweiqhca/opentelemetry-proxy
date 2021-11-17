# OpenTelemetry.DynamicProxy

Automatically generate activity by declare [Activity] or [ActivitySource].

## [ActivitySource]
If declare [ActivitySource] in type, all method(interface) or virtaul method(class) will automatically generate activity.

## [Activity]
If declare [Activity] in method(interface method or virtaul method), call method will automatically generate activity.

## [NonActivity]
If declare [NonActivity] in method, call method will not automatically generate activity.
