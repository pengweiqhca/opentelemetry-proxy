using OpenTelemetry.Trace;

namespace OpenTelemetry.Proxy.Demo;

public class DemoClassProxy : DemoClass
{
    private static readonly ActivitySource ActivitySource = new("DemoClass");

    public override async Task<T> Demo<T>(T arg)
    {
        var activity = ActivitySource.StartActivity(ActivityKind.Internal, links:
        [
            new(new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
                    ActivityTraceFlags.Recorded, "test"),
                new() { { "abc", "def" }, { "now", DateTime.Now } })
        ], name: "DemoClass.Demo");

        activity?.AddEvent(new("xxxxx", DateTimeOffset.Now, [
            KeyValuePair.Create("dddd", (object?)3333),
            KeyValuePair.Create("asdfasdfasd", (object?)Guid.NewGuid())
        ]));

        try
        {
            return await base.Demo(arg).ConfigureAwait(false);
        }
        catch (Exception ex) when (OpenTelemetry.Proxy.ActivityExtensions.SetExceptionStatus(activity, ex))
        {
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public override async ValueTask Demo2()
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo2");

        try
        {
            await base.Demo2().ConfigureAwait(false);
        }
        catch (Exception ex) when (OpenTelemetry.Proxy.ActivityExtensions.SetExceptionStatus(activity, ex))
        {
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public override async Task<int> Demo3()
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo3");

        try
        {
            var result = await base.Demo3().ConfigureAwait(false);

            activity?.SetTagEnumerable("$returnvalue", result);

            return result;
        }
        catch (Exception ex) when (OpenTelemetry.Proxy.ActivityExtensions.SetExceptionStatus(activity, ex))
        {
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public override async Task<DateTime> Demo4()
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo4");

        try
        {
            var result = await base.Demo4().ConfigureAwait(false);

            activity?.SetTagEnumerable("returnvalue", result);

            return result;
        }
        catch (Exception ex) when (OpenTelemetry.Proxy.ActivityExtensions.SetExceptionStatus(activity, ex))
        {
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public override async ValueTask Demo5()
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo5");

        try
        {
            await base.Demo5().ConfigureAwait(false);
        }
        catch (Exception ex) when (OpenTelemetry.Proxy.ActivityExtensions.SetExceptionStatus(activity, ex))
        {
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
