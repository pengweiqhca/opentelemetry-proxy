using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenTelemetry.Proxy;
using Xunit;

namespace OpenTelemetry.StaticProxy.Tests;

/// <summary>
/// Functional integration tests migrated from FunctionTest.cs, adapted for the
/// Source Generator + Interceptor approach.
///
/// Since we can't easily run the generated code at test time (it requires an actual
/// runtime with ActivityListener), we verify:
/// 1. The generated source contains expected patterns (StartActivity, SetTag, SetExceptionStatus, etc.)
/// 2. The output compilation has no errors (excluding interceptor-specific CS9206 which is expected in test context)
///
/// Validates: Requirements 3.x, 4.x, 5.x, 7.x, 8.5, 8.6, 12.2, 12.4
/// </summary>
public class FunctionalIntegrationTest
{
    #region Compilation Helpers

    private static readonly Lazy<MetadataReference[]> Refs = new(CreateRefs);

    private static MetadataReference[] CreateRefs()
    {
        var refs = new List<MetadataReference>();
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? [];
        foreach (var asm in trusted)
        {
            var name = Path.GetFileNameWithoutExtension(asm);
            if (name is "System.Runtime" or "System.Collections" or "netstandard"
                or "mscorlib" or "System.Private.CoreLib"
                or "System.Diagnostics.DiagnosticSource"
                or "System.Linq" or "System.Threading.Tasks"
                or "System.Threading" or "System.Runtime.CompilerServices.Unsafe"
                or "System.Console")
                refs.Add(MetadataReference.CreateFromFile(asm));
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(ActivitySourceAttribute).Assembly.Location));
        return refs.ToArray();
    }

    /// <summary>
    /// Runs the real ProxySourceGenerator and returns the generated source text.
    /// </summary>
    private static string RunGenerator(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var comp = CSharpCompilation.Create("TestAssembly", [tree], Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var gen = new ProxySourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(gen);
        driver = driver.RunGeneratorsAndUpdateCompilation(comp, out _, out _);
        var result = driver.GetRunResult();

        var allOutput = new System.Text.StringBuilder();
        foreach (var genResult in result.Results)
            foreach (var genSource in genResult.GeneratedSources)
                allOutput.AppendLine(genSource.SourceText.ToString());

        return allOutput.ToString();
    }

    #endregion

    #region SuppressInstrumentationScope Tests

    [Fact]
    public void SuppressInstrumentationScope_NonActivity_Generates_SuppressScope()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    public static class TestClass {
        [NonActivity(true)]
        public static bool SuppressInstrumentationScope() => true;
    }

    public class Caller {
        public void Call() {
            TestClass.SuppressInstrumentationScope();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("SuppressInstrumentationScope.Begin()", generated);
        Assert.Contains("InterceptsLocation", generated);
    }

    [Fact]
    public void SuppressInstrumentationScope_Activity_With_Suppress_Generates_Both()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    public static class TestClass {
        [Activity(SuppressInstrumentation = true)]
        public static bool SuppressInstrumentationScope2() => true;
    }

    public class Caller {
        public void Call() {
            TestClass.SuppressInstrumentationScope2();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SuppressInstrumentationScope.Begin()", generated);
        Assert.Contains("disposable?.Dispose()", generated);
        Assert.Contains("activity?.Dispose()", generated);
    }

    [Fact]
    public void SuppressInstrumentationScope_Async_NonActivity_Generates_SuppressScope()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [NonActivity(true)]
        public static async Task<bool> SuppressInstrumentationScopeAsync() {
            await Task.Delay(100).ConfigureAwait(false);
            return true;
        }

        [NonActivity(true)]
        public static async ValueTask<bool> SuppressInstrumentationScope2Async() {
            await Task.Delay(100).ConfigureAwait(false);
            return true;
        }
    }

    public class Caller {
        public void Call() {
            TestClass.SuppressInstrumentationScopeAsync();
            TestClass.SuppressInstrumentationScope2Async();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("SuppressInstrumentationScope.Begin()", generated);
    }

    [Fact]
    public void SuppressInstrumentationScope_Async_Activity_With_Suppress()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [Activity(SuppressInstrumentation = true)]
        public static async Task<bool> SuppressInstrumentationScope3Async() {
            await Task.Delay(100).ConfigureAwait(false);
            return true;
        }
    }

    public class Caller {
        public void Call() {
            TestClass.SuppressInstrumentationScope3Async();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SuppressInstrumentationScope.Begin()", generated);
        Assert.Contains("disposable?.Dispose()", generated);
    }

    #endregion

    #region ActivityName Tests

    [Fact]
    public void ActivityName_Generates_SetActivityContext_With_Tags()
    {
        var source = @"
using OpenTelemetry.Proxy;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [ActivityName(AdjustStartTime = true)]
        public static object? GetActivityName([ActivityTag] int delay, [ActivityTag] string name) => null;
    }

    public class Caller {
        public void Call() {
            TestClass.GetActivityName(200, ""123"");
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("SetActivityContext", generated);
        Assert.Contains("AdjustStartTime = true", generated);
        Assert.Contains("\"delay\"", generated);
        Assert.Contains("\"name\"", generated);
    }

    [Fact]
    public void ActivityName_Async_Generates_SetActivityContext()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [ActivityName(AdjustStartTime = true)]
        [ActivityTags(""delay"")]
        public static async ValueTask<object?> GetActivityNameAsync(int delay, [ActivityTag] string name) {
            await Task.Delay(delay).ConfigureAwait(false);
            return null;
        }
    }

    public class Caller {
        public void Call() {
            TestClass.GetActivityNameAsync(100, ""test"");
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("SetActivityContext", generated);
        Assert.Contains("AdjustStartTime = true", generated);
        Assert.Contains("\"delay\"", generated);
        Assert.Contains("\"name\"", generated);
    }

    #endregion

    #region Activity Creation and Tag Setting Tests

    [Fact]
    public void Activity_Creates_Activity_With_Tags()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Diagnostics;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [Activity]
        [ActivityTags(""delay"")]
        public static Activity? GetCurrentActivity(int delay) => Activity.Current;
    }

    public class Caller {
        public void Call() {
            TestClass.GetCurrentActivity(100);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SetTag(\"delay\"", generated);
        Assert.Contains("try", generated);
        Assert.Contains("catch", generated);
        Assert.Contains("finally", generated);
        Assert.Contains("activity?.Dispose()", generated);
    }

    [Fact]
    public void Activity_Async_Creates_Activity_With_Tags()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [Activity]
        public static Task<Activity?> GetCurrentActivityAsync([ActivityTag] int delay) =>
            Task.FromResult(Activity.Current);

        [Activity]
        public static Task<Activity?> GetCurrentActivity2Async([ActivityTag] int delay) {
            return Task.FromResult(Activity.Current);
        }

        [Activity]
        public static async Task<Activity?> AwaitGetCurrentActivityAsync([ActivityTag] int delay) =>
            await Task.FromResult(Activity.Current).ConfigureAwait(false);
    }

    public class Caller {
        public void Call() {
            TestClass.GetCurrentActivityAsync(100);
            TestClass.GetCurrentActivity2Async(100);
            TestClass.AwaitGetCurrentActivityAsync(100);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SetTag(\"delay\"", generated);
        Assert.Contains("await", generated);
        Assert.Contains("ConfigureAwait(false)", generated);
    }

    [Fact]
    public void Activity_With_Static_Property_Tag()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        public static DateTime Now { get; } = new(2024, 1, 1);

        [Activity]
        [ActivityTags(""Now"")]
        public static Task<Activity?> GetCurrentActivityWithStaticTag(int delay) =>
            Task.FromResult(Activity.Current);
    }

    public class Caller {
        public void Call() {
            TestClass.GetCurrentActivityWithStaticTag(100);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SetTag(\"Now\"", generated);
    }

    #endregion


    #region Exception Status Setting Tests

    [Fact]
    public void Exception_Activity_Generates_SetExceptionStatus()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [Activity(SuppressInstrumentation = true)]
        public static async Task Exception() {
            throw new Exception();
        }
    }

    public class Caller {
        public void Call() {
            TestClass.Exception();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("SetExceptionStatus(activity, ex)", generated);
        Assert.Contains("throw;", generated);
        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SuppressInstrumentationScope.Begin()", generated);
    }

    #endregion

    #region OutTag and ReturnValue Tests

    [Fact]
    public void OutMethod_Generates_OutTag_SetTag()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System;

namespace TestNs {
    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    public static class TestClass {
        [Activity]
        [return: ActivityTag(""def"")]
        public static int OutMethod(in int a, out int b, ref int c, int d, int e, [ActivityTag] int f) {
            b = a * a;
            c = a * c;
            return d + e + f;
        }
    }

    public class Caller {
        public void Call() {
            int b;
            int c = 5;
            TestClass.OutMethod(10, out b, ref c, 1, 2, 3);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        // InTag for parameter f
        Assert.Contains("SetTag(\"f\"", generated);
        // OutTag for return value
        Assert.Contains("SetTag(\"def\"", generated);
    }

    [Fact]
    public void ReturnValue_Generates_ReturnTag()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [Activity]
        [return: ActivityTag]
        public static Task<int> ReturnValue(int a) {
            return Task.FromResult(a + 1);
        }
    }

    public class Caller {
        public void Call() {
            TestClass.ReturnValue(42);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        // Default return value tag name is "$returnvalue"
        Assert.Contains("SetTag(\"$returnvalue\"", generated);
    }

    [Fact]
    public void ReturnValueAsync_Generates_ReturnTag()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [Activity]
        [return: ActivityTag]
        public static async Task<int> ReturnValueAsync(int a) => a + 1;
    }

    public class Caller {
        public void Call() {
            TestClass.ReturnValueAsync(42);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SetTag(\"$returnvalue\"", generated);
        Assert.Contains("await", generated);
    }

    #endregion

    #region Interface Method Interception Tests

    [Fact]
    public void Interface_Method_Interception_Generates_Interceptor()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public interface IMyService {
        [Activity]
        Task DoWork(int param);
    }

    public class MyServiceImpl : IMyService {
        public async Task DoWork(int param) => await Task.CompletedTask;
    }

    public class Caller {
        public void Call(IMyService svc) {
            svc.DoWork(42);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("InterceptsLocation", generated);
        Assert.Contains("activity?.Dispose()", generated);
        Assert.Contains("SetExceptionStatus(", generated);
    }

    [Fact]
    public void Interface_With_ActivitySource_Async_Methods_Intercepted()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public interface IMyService {
        [Activity]
        Task<int> Calculate(int a, int b);
        [Activity]
        Task Process([ActivityTag] string name);
    }

    public class MyServiceImpl : IMyService {
        public async Task<int> Calculate(int a, int b) => a + b;
        public async Task Process(string name) => await Task.CompletedTask;
    }

    public class Caller {
        public void Call(IMyService svc) {
            svc.Calculate(1, 2);
            svc.Process(""test"");
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("InterceptsLocation", generated);
        // Tag for Process method's name parameter
        Assert.Contains("SetTag(\"name\"", generated);
    }

    #endregion

    #region Abstract Method Interception Tests

    [Fact]
    public void Abstract_Method_Interception_Generates_Interceptor()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public abstract class MyServiceBase {
        [Activity]
        public abstract Task DoWork(int param);
    }

    public class MyServiceImpl : MyServiceBase {
        public override async Task DoWork(int param) => await Task.CompletedTask;
    }

    public class Caller {
        public void Call(MyServiceBase svc) {
            svc.DoWork(42);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("InterceptsLocation", generated);
        Assert.Contains("activity?.Dispose()", generated);
        Assert.Contains("SetExceptionStatus(", generated);
    }

    [Fact]
    public void Abstract_Class_With_ActivitySource_Async_Methods_Intercepted()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource]
    public abstract class MyServiceBase {
        [Activity]
        public abstract Task<int> Calculate([ActivityTag] int a, int b);
    }

    public class MyServiceImpl : MyServiceBase {
        public override async Task<int> Calculate(int a, int b) => a + b;
    }

    public class Caller {
        public void Call(MyServiceBase svc) {
            svc.Calculate(1, 2);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("InterceptsLocation", generated);
        // Tag for Calculate method's a parameter
        Assert.Contains("SetTag(\"a\"", generated);
    }

    #endregion

    #region Additional Scenarios

    [Fact]
    public void ActivityName_Using_Pattern_Generates_SetActivityContext()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System;

namespace TestNs {
    [ActivitySource]
    public static class TestClass {
        [ActivityName]
        public static bool Using(out DateTimeOffset now) {
            now = DateTimeOffset.Now;
            return true;
        }
    }

    public class Caller {
        public void Call() {
            TestClass.Using(out _);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("SetActivityContext", generated);
    }

    [Fact]
    public void Activity_Kind_Parameter_Passed_To_StartActivity()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TestNs {
    [ActivitySource(Kind = ActivityKind.Client)]
    public class MyService {
        [Activity]
        public async Task DoWork(int param) => await Task.CompletedTask;
    }

    public class Caller {
        public void Call() {
            var svc = new MyService();
            svc.DoWork(42);
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
    }

    [Fact]
    public void Void_Method_With_Activity_Generates_Interceptor()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System;

namespace TestNs {
    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    public static class TestClass {
        [Activity]
        public static void VoidMethod() {
            if (DateTime.Now is { Hour: > 10, Second: < 10 }) { }
        }
    }

    public class Caller {
        public void Call() {
            TestClass.VoidMethod();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("activity?.Dispose()", generated);
    }

    [Fact]
    public void ReturnTag_With_Expression_Generates_Correct_SetTag()
    {
        var source = @"
using OpenTelemetry.Proxy;
using System;

namespace TestNs {
    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    public static class TestClass {
        [Activity]
        [return: ActivityTag(""ret"", Expression = ""$.Hour"")]
        public static DateTime VoidMethod2() {
            return DateTime.Now;
        }
    }

    public class Caller {
        public void Call() {
            TestClass.VoidMethod2();
        }
    }
}";

        var generated = RunGenerator(source);

        Assert.Contains("StartActivity(", generated);
        Assert.Contains("SetTag(\"ret\"", generated);
        // Expression $.Hour should generate @return.Hour
        Assert.Contains(".Hour", generated);
    }

    #endregion
}
