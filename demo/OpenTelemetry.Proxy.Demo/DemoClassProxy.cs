using OpenTelemetry.Trace;

namespace OpenTelemetry.Proxy.Demo;

public class DemoClassProxy : DemoClass
{
    private static readonly ActivitySource ActivitySource = new("DemoClass");

    public override async Task<T> Demo<T>(T arg)
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo");

        try
        {
            return await base.Demo(arg).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

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
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

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

            activity?.SetTag(ActivityTagAttribute.ReturnValueTagName, result);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

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

            activity?.SetTag("returnvalue", result);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

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
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
