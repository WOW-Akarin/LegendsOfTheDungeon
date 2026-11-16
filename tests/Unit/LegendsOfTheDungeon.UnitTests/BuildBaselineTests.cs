using System.Reflection;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class BuildBaselineTests
{
    [Fact]
    public void TestRuntimeUsesNet10()
    {
        Assert.Equal(10, Environment.Version.Major);
    }

    [Fact]
    public void ProductionAssembliesCanBeLoaded()
    {
        Assert.NotNull(Assembly.Load("LegendsOfTheDungeon.Kernel"));
        Assert.NotNull(Assembly.Load("LegendsOfTheDungeon.Runtime.Abstractions"));
    }

    [Fact]
    public void CiInjectedTestFailureIsDetected()
    {
        if (Environment.GetEnvironmentVariable("LOTD_INJECT_TEST_FAILURE") == "1")
        {
            Assert.Fail("P02 intentional test failure gate.");
        }
    }
}
