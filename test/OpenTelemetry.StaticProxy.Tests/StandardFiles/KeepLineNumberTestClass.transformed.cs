using OpenTelemetry.Proxy;
[assembly: OpenTelemetry.Proxy.ProxyHasGeneratedAttribute]

namespace OpenTelemetry.StaticProxy.TestClass;




/// <summary>
/// KeepLineNumberTestClass
/// </summary>
[ActivitySource]
[OpenTelemetry.Proxy.ProxyHasGeneratedAttribute]
#line 12 "/0/Test0.cs"
public class KeepLineNumberTestClass<T>
{
    [Activity]
    [return: ActivityTag]
    public static int TestMethod(int size)
{
#line hidden
        var activity@ = KeepLineNumberTestClass<T>.@ActivitySource@.StartActivity("KeepLineNumberTestClass`1.TestMethod", default);
        try
    {
        if (size < 10) {
#line 18 "/0/Test0.cs"
                var return = 1;
#line hidden
                       if(activity@ != null) activity@.SetTag("$returnvalue", @return);
#line 18 "/0/Test0.cs"
return return;
}
        Console.WriteLine(DateTime.Now);

        throw new();
    }
#line hidden
        catch(Exception ex) when(OpenTelemetry.Proxy.ActivityExtensions.SetExceptionStatus(activity@, ex)){
        throw;
}
        finally{
        if(activity@ != null) activity@.Dispose();
}
#line 23 "/0/Test0.cs"
}
#line 25 "/0/Test0.cs"
    public static class NormalClass
    {
        public static Exception TestMethod() => throw new();
#line 28 "/0/Test0.cs"
    }
    private static readonly System.Diagnostics.ActivitySource @ActivitySource@ = new System.Diagnostics.ActivitySource("OpenTelemetry.StaticProxy.TestClass.KeepLineNumberTestClass`1", typeof(KeepLineNumberTestClass<>).Assembly.GetName().Version?.ToString());
#line 29 "/0/Test0.cs"
}
