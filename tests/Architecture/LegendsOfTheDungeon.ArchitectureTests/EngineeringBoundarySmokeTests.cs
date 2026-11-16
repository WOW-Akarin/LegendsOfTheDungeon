using System.Reflection;
using Xunit;

namespace LegendsOfTheDungeon.ArchitectureTests;

public sealed class EngineeringBoundarySmokeTests
{
    [Theory]
    [InlineData("LegendsOfTheDungeon.Kernel", "Kernel")]
    [InlineData("LegendsOfTheDungeon.Runtime.Abstractions", "Runtime.Abstractions")]
    public void ProductionAssemblyDeclaresItsEngineeringLayer(string assemblyName, string expectedLayer)
    {
        var assembly = Assembly.Load(assemblyName);
        var layer = assembly
.GetCustomAttributes<AssemblyMetadataAttribute>()
.Single(attribute => attribute.Key == "LegendsOfTheDungeon.Layer")
.Value;

        Assert.Equal(expectedLayer, layer);
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.EndsWith(".Tests", StringComparison.Ordinal) == true);
    }
}
