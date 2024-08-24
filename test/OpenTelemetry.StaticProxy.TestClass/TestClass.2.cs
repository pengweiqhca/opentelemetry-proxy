using OpenTelemetry.Proxy;

namespace AssemblyToProcess;

public static partial class TestClass
{
    [Activity]
    [return: ActivityTag]
    public static Task<int> ReturnValue(int a)
    {
        return Task.FromResult(a + 1);
    }

    [Activity]
    [return: ActivityTag]
#pragma warning disable CS1998
    public static async Task<int> ReturnValueAsync(int a) => a + 1;
#pragma warning restore CS1998
    [ActivityName]
    public static bool Using(out DateTimeOffset now)
    {
        using (ActivityName.SetName("Using")) now = DateTimeOffset.Now;

        return true;
    }

    public delegate bool TestDelegate(out DateTimeOffset now);
}
