namespace OpenTelemetry.StaticProxy.SourceTransformer.Tests;

public class SpecificTest
{
    [Fact]
    public async Task KeepColumnNumberTest()
    {
        var ex = Assert.ThrowsAny<Exception>(() => KeepLineNumberTestClass<int>.TestMethod(10));

        var stackFrame = new EnhancedStackTrace(ex).GetFrame(0);

        var fileName = stackFrame.GetFileName();
        var fileLineNumber = stackFrame.GetFileLineNumber();
        var fileColumnNumber = stackFrame.GetFileColumnNumber();

        Assert.NotNull(fileName);

        Assert.Equal(Path.GetFullPath(
                "../../../../OpenTelemetry.StaticProxy.SourceTransformer.TestClass/KeepLineNumberTestClass.cs"),
            fileName);

        using var sr = new StreamReader(fileName);

        var i = 0;
        while (await sr.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            i++;

            var index = line.IndexOf("throw new();", StringComparison.Ordinal);

            if (index >= 0)
            {
                Assert.Equal(fileLineNumber, i);
                Assert.Equal(index + 1, fileColumnNumber);

                return;
            }
        }

        Assert.Fail();
    }

    [Fact]
    public async Task KeepLineNumberTest()
    {
        var ex = Assert.ThrowsAny<Exception>(KeepLineNumberTestClass<long>.NormalClass.Exception);

        var stackFrame = new EnhancedStackTrace(ex).GetFrame(0);

        var fileName = stackFrame.GetFileName();
        var fileLineNumber = stackFrame.GetFileLineNumber();

        Assert.NotNull(fileName);

        Assert.Equal(Path.GetFullPath(
                "../../../../OpenTelemetry.StaticProxy.SourceTransformer.TestClass/KeepLineNumberTestClass.cs"),
            fileName);

        using var sr = new StreamReader(fileName);

        var index = 0;
        while (await sr.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            index++;

            if (line.Contains("public static Exception Exception()"))
            {
                Assert.Equal(fileLineNumber, index);

                return;
            }
        }

        Assert.Fail();
    }
}
