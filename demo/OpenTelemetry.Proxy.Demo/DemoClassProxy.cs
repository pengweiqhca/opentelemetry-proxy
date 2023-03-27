using OpenTelemetry.Trace;

namespace OpenTelemetry.Proxy.Demo;

public class DemoClassProxy : DemoClass
{
    private static readonly ActivitySource ActivitySource = new("DemoClass");

    public override async Task Demo()
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo");

        try
        {
            await base.Demo().ConfigureAwait(false);
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

    public override async Task Demo3()
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo3");

        try
        {
            await base.Demo3().ConfigureAwait(false);
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

    public override async Task Demo4()
    {
        var activity = ActivitySource.StartActivity("DemoClass.Demo4");

        try
        {
            await base.Demo4().ConfigureAwait(false);
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
