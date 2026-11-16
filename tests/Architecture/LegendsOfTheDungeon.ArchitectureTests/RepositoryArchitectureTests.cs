using Xunit;

namespace LegendsOfTheDungeon.ArchitectureTests;

public sealed class RepositoryArchitectureTests
{
    [Fact]
    public void ProductionProjectGraphMatchesExplicitArchitecturePolicy()
    {
        var (_, graph, _) = LoadRepository();

        var violations = ArchitectureRuleEngine
            .CheckExplicitWhitelist(graph)
            .Concat(ArchitectureRuleEngine.CheckForbiddenProjectReferences(graph))
            .Concat(ArchitectureRuleEngine.CheckAcyclicReferences(graph))
            .Concat(ArchitectureRuleEngine.CheckSharedHubReviews(graph))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionPublicApiDoesNotLeakForbiddenImplementationProjects()
    {
        var (_, graph, assemblies) = LoadRepository();
        var exposures = RepositoryAssemblyInspector.ScanPublicApi(graph, assemblies);

        var violations = ArchitectureRuleEngine.CheckPublicApiExposures(
            graph,
            exposures);

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionInternalsVisibleToMatchesExplicitTestWhitelist()
    {
        var (_, graph, assemblies) = LoadRepository();
        var friendAccesses = RepositoryAssemblyInspector.ScanInternalsVisibleTo(assemblies);

        var violations = ArchitectureRuleEngine.CheckInternalsVisibleTo(
            graph,
            friendAccesses);

        Assert.Empty(violations);
    }

    [Fact]
    public void CiRunsArchitectureTestsAsBlockingGate()
    {
        var root = RepositoryArchitectureLoader.FindRepositoryRoot();
        var verifyScript = File.ReadAllText(
            System.IO.Path.Combine(root, "eng", "verify.sh"));
        var workflow = File.ReadAllText(
            System.IO.Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Contains(
            "tests/Architecture/LegendsOfTheDungeon.ArchitectureTests/LegendsOfTheDungeon.ArchitectureTests.csproj",
            verifyScript,
            StringComparison.Ordinal);

        Assert.Contains("./eng/verify.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error: true", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CiInjectedArchitectureFailureIsDetected()
    {
        if (Environment.GetEnvironmentVariable("LOTD_INJECT_ARCHITECTURE_FAILURE") == "1")
        {
            Assert.Fail("P03 intentional architecture-test failure gate.");
        }
    }

    private static (
        string Root,
        ProjectGraph Graph,
        IReadOnlyDictionary<string, System.Reflection.Assembly> Assemblies)
        LoadRepository()
    {
        var root = RepositoryArchitectureLoader.FindRepositoryRoot();
        var graph = RepositoryArchitectureLoader.Load(root);
        var assemblies = RepositoryAssemblyInspector.LoadProductionAssemblies(root, graph);

        return (root, graph, assemblies);
    }
}
