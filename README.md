# OpenTelemetry.DynamicProxy

Automatically generate activity by define [Activity] or [ActivitySource].

## [ActivitySource]
If add [ActivitySource] on type, all method(interface) or virtaul method(class) will automatically generate activity.

## [Activity]
If add [Activity] un method(interface method or virtaul method), call method will automatically generate activity.

## [NonActivity]
If add [NonActivity] on method, call method will not automatically generate activity.

## [ActivityName]
Modify inner activity DisplayName, must call `TracerProviderBuilder.AddProcessor(new ActivityNameProcessor())`. If type have [ActivitySource] or method have [Activity], [NonActivity], [ActivityName] will be disabled.

> Priority: [NonActivity] > [Activity] > [ActivitySource] > [ActivityName]
