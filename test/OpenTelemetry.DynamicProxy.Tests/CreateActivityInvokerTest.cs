using Castle.DynamicProxy;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy.Tests;

public class CreateActivityInvokerTest
{
    [Fact]
    public void CreateInterfaceProxyWithoutTarget()
    {
        var methodInfo = typeof(ITestInterface).GetMethod(nameof(ITestInterface.TestMethod));

        Assert.NotNull(methodInfo);

        var mock = new Moq.Mock<IInvocation>();

        mock.SetupGet(x => x.Method).Returns(methodInfo);

        Assert.Equal(typeof(ITestInterface).FullName,
            Assert.IsType<ActivityInvoker>(new ActivityInvokerFactory().Create(mock.Object, default))
                .ActivitySourceName);
    }

    [Fact]
    public void CreateInterfaceProxyWithTarget()
    {
        var methodInfo = typeof(ITestInterface).GetMethod(nameof(ITestInterface.TestMethod));

        Assert.NotNull(methodInfo);

        var mock = new Moq.Mock<IInvocation>();

        mock.SetupGet(x => x.Method).Returns(methodInfo);
        mock.SetupGet(x => x.TargetType).Returns(typeof(TestInterface1));

        var methodInfo2 = typeof(TestInterface1).GetMethod(nameof(TestInterface1.TestMethod));

        Assert.NotNull(methodInfo2);

        mock.SetupGet(x => x.MethodInvocationTarget).Returns(methodInfo2);

        Assert.Equal(typeof(ITestInterface).FullName,
            Assert.IsType<ActivityInvoker>(new ActivityInvokerFactory().Create(mock.Object, default))
                .ActivitySourceName);

        mock = new();

        mock.SetupGet(x => x.Method).Returns(methodInfo);
        mock.SetupGet(x => x.TargetType).Returns(typeof(TestInterface2));

        methodInfo2 = typeof(TestInterface2).GetMethod(nameof(TestInterface2.TestMethod));

        Assert.NotNull(methodInfo2);

        mock.SetupGet(x => x.MethodInvocationTarget).Returns(methodInfo2);

        Assert.Equal(typeof(TestInterface2).FullName,
            Assert.IsType<ActivityInvoker>(new ActivityInvokerFactory().Create(mock.Object, default))
                .ActivitySourceName);
    }

    [Fact]
    public void CreateInterfaceProxyWithTargetInterface()
    {
        var methodInfo = typeof(ITestInterface).GetMethod(nameof(ITestInterface.TestMethod));

        Assert.NotNull(methodInfo);

        var mock = new Moq.Mock<IInvocation>();

        mock.SetupGet(x => x.Method).Returns(methodInfo);
        mock.SetupGet(x => x.TargetType).Returns(typeof(TestInterface1));

        var methodInfo2 = typeof(TestInterface2).GetMethod(nameof(TestInterface2.TestMethod));

        Assert.NotNull(methodInfo2);

        mock.SetupGet(x => x.MethodInvocationTarget).Returns(methodInfo2);

        Assert.Equal(typeof(TestInterface2).FullName,
            Assert.IsType<ActivityInvoker>(new ActivityInvokerFactory().Create(mock.Object, default))
                .ActivitySourceName);
    }

    [Fact]
    public void CreateClassProxy()
    {
        var methodInfo = typeof(TestInterface2).GetMethod(nameof(TestInterface2.TestMethod));

        Assert.NotNull(methodInfo);

        var mock = new Moq.Mock<IInvocation>();

        mock.SetupGet(x => x.Method).Returns(methodInfo);

        Assert.Equal(typeof(TestInterface2).FullName,
            Assert.IsType<ActivityInvoker>(new ActivityInvokerFactory().Create(mock.Object, default))
                .ActivitySourceName);
    }

    [Fact]
    public void CreateClassProxyWithTarget()
    {
        var methodInfo = typeof(TestInterface1).GetMethod(nameof(TestInterface1.TestMethod));

        Assert.NotNull(methodInfo);

        var mock = new Moq.Mock<IInvocation>();

        mock.SetupGet(x => x.Method).Returns(methodInfo);
        mock.SetupGet(x => x.TargetType).Returns(typeof(TestInterface2));

        var methodInfo2 = typeof(TestInterface2).GetMethod(nameof(TestInterface2.TestMethod));

        Assert.NotNull(methodInfo2);

        mock.SetupGet(x => x.MethodInvocationTarget).Returns(methodInfo2);

        Assert.Equal(typeof(TestInterface2).FullName,
            Assert.IsType<ActivityInvoker>(new ActivityInvokerFactory().Create(mock.Object, default))
                .ActivitySourceName);
    }

    [ActivitySource]
    public interface ITestInterface
    {
        Task TestMethod();
    }

    [ActivitySource]
    public class TestInterface1 : ITestInterface
    {
        public Task TestMethod() => Task.CompletedTask;
    }

    [ActivitySource]
    public class TestInterface2 : ITestInterface
    {
        public async Task TestMethod() => await Task.CompletedTask.ConfigureAwait(false);
    }
}
