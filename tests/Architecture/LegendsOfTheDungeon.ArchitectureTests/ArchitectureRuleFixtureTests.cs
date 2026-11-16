using Xunit;

namespace LegendsOfTheDungeon.ArchitectureTests;

public sealed class ArchitectureRuleFixtureTests
{
    [Fact]
    public void ExplicitWhitelistHasPositiveAndIntentionalNegativeFixtures()
    {
        var target = Project("src/B/B.csproj", ArchitectureRole.Contract);
        var source = Project(
            "src/A/A.csproj",
            ArchitectureRole.Core,
            allowedReferences: [target.Path]);

        var positive = Graph([source, target], [(source.Path, target.Path)]);
        Assert.Empty(ArchitectureRuleEngine.CheckExplicitWhitelist(positive));

        var negative = Graph([source, target], []);
        var violations = ArchitectureRuleEngine.CheckExplicitWhitelist(negative);

        Assert.Contains(
            violations,
            violation => violation.RuleId == ArchitectureRuleEngine.ExplicitWhitelistRule);
    }

    [Fact]
    public void DomainIsolationHasPositiveAndIntentionalNegativeFixtures()
    {
        var contract = Project("src/Contracts/C.csproj", ArchitectureRole.Contract);
        var adapter = Project("src/Platform/Adapter.csproj", ArchitectureRole.PlatformAdapter);
        var domain = Project(
            "src/Domain/D.csproj",
            ArchitectureRole.Domain,
            allowedReferences: [contract.Path, adapter.Path]);

        var positive = Graph([domain, contract], [(domain.Path, contract.Path)]);
        Assert.DoesNotContain(
            ArchitectureRuleEngine.CheckForbiddenProjectReferences(positive),
            violation => violation.RuleId == ArchitectureRuleEngine.DomainIsolationRule);

        var negative = Graph([domain, contract, adapter], [(domain.Path, adapter.Path)]);
        Assert.Contains(
            ArchitectureRuleEngine.CheckForbiddenProjectReferences(negative),
            violation => violation.RuleId == ArchitectureRuleEngine.DomainIsolationRule);
    }

    [Fact]
    public void CrossModuleImplementationIsolationHasPositiveAndIntentionalNegativeFixtures()
    {
        var moduleA = Project(
            "src/Modules/A/Core/A.csproj",
            ArchitectureRole.Core,
            module: "A");

        var moduleBContract = Project(
            "src/Modules/B/Contracts/B.Contracts.csproj",
            ArchitectureRole.Contract,
            module: "B");

        var moduleBInfrastructure = Project(
            "src/Modules/B/Infrastructure/B.Infrastructure.csproj",
            ArchitectureRole.Infrastructure,
            module: "B");

        var positive = Graph(
            [
                moduleA with
                {
                    AllowedProjectReferences = Set(moduleBContract.Path)
                },
                moduleBContract
            ],
            [(moduleA.Path, moduleBContract.Path)]);

        Assert.DoesNotContain(
            ArchitectureRuleEngine.CheckForbiddenProjectReferences(positive),
            violation => violation.RuleId == ArchitectureRuleEngine.CrossModuleImplementationRule);

        var negativeSource = moduleA with
        {
            AllowedProjectReferences = Set(moduleBInfrastructure.Path)
        };

        var negative = Graph(
            [negativeSource, moduleBInfrastructure],
            [(negativeSource.Path, moduleBInfrastructure.Path)]);

        Assert.Contains(
            ArchitectureRuleEngine.CheckForbiddenProjectReferences(negative),
            violation => violation.RuleId == ArchitectureRuleEngine.CrossModuleImplementationRule);
    }

    [Fact]
    public void ModuleToHostRuleHasPositiveAndIntentionalNegativeFixtures()
    {
        var module = Project(
            "src/Modules/A/Core/A.csproj",
            ArchitectureRole.Core,
            module: "A");

        var contract = Project(
            "src/Modules/B/Contracts/B.csproj",
            ArchitectureRole.Contract,
            module: "B");

        var host = Project(
            "src/Hosts/ServerHost/ServerHost.csproj",
            ArchitectureRole.Host);

        var positive = Graph(
            [
                module with
                {
                    AllowedProjectReferences = Set(contract.Path)
                },
                contract
            ],
            [(module.Path, contract.Path)]);

        Assert.DoesNotContain(
            ArchitectureRuleEngine.CheckForbiddenProjectReferences(positive),
            violation => violation.RuleId == ArchitectureRuleEngine.ModuleHostRule);

        var negativeSource = module with
        {
            AllowedProjectReferences = Set(host.Path)
        };

        var negative = Graph(
            [negativeSource, host],
            [(negativeSource.Path, host.Path)]);

        Assert.Contains(
            ArchitectureRuleEngine.CheckForbiddenProjectReferences(negative),
            violation => violation.RuleId == ArchitectureRuleEngine.ModuleHostRule);
    }

    [Fact]
    public void ReferenceGraphCycleRuleHasPositiveAndIntentionalNegativeFixtures()
    {
        var a = Project("src/A/A.csproj", ArchitectureRole.Core);
        var b = Project("src/B/B.csproj", ArchitectureRole.Contract);

        var positive = Graph(
            [
                a with { AllowedProjectReferences = Set(b.Path) },
                b
            ],
            [(a.Path, b.Path)]);

        Assert.Empty(ArchitectureRuleEngine.CheckAcyclicReferences(positive));

        var negative = Graph(
            [
                a with { AllowedProjectReferences = Set(b.Path) },
                b with { AllowedProjectReferences = Set(a.Path) }
            ],
            [(a.Path, b.Path), (b.Path, a.Path)]);

        Assert.Contains(
            ArchitectureRuleEngine.CheckAcyclicReferences(negative),
            violation => violation.RuleId == ArchitectureRuleEngine.AcyclicReferenceRule);
    }

    [Fact]
    public void PublicApiLeakRuleHasPositiveAndIntentionalNegativeFixtures()
    {
        var contract = Project("src/B/Contracts/B.csproj", ArchitectureRole.Contract);
        var infrastructure = Project(
            "src/B/Infrastructure/B.Infrastructure.csproj",
            ArchitectureRole.Infrastructure);

        var source = Project(
            "src/A/A.csproj",
            ArchitectureRole.Core,
            allowedPublicApiReferences: [contract.Path]);

        var graph = Graph([source, contract, infrastructure], []);

        Assert.Empty(
            ArchitectureRuleEngine.CheckPublicApiExposures(
                graph,
                [new ApiExposure(source.Path, contract.Path, "Method.ReturnType")]));

        Assert.Contains(
            ArchitectureRuleEngine.CheckPublicApiExposures(
                graph,
                [new ApiExposure(source.Path, infrastructure.Path, "Method.ReturnType")]),
            violation => violation.RuleId == ArchitectureRuleEngine.PublicApiLeakRule);
    }

    [Fact]
    public void InternalsVisibleToRuleHasPositiveAndIntentionalNegativeFixtures()
    {
        var source = Project(
            "src/A/A.csproj",
            ArchitectureRole.Core,
            allowedInternalsVisibleTo: ["A.Tests"]);

        var positive = Graph(
            [source],
            [],
            testAssemblyNames: ["A.Tests"]);

        Assert.Empty(
            ArchitectureRuleEngine.CheckInternalsVisibleTo(
                positive,
                [new FriendAccess(source.Path, "A.Tests")]));

        Assert.Contains(
            ArchitectureRuleEngine.CheckInternalsVisibleTo(
                positive,
                [new FriendAccess(source.Path, "A.OtherProduction")]),
            violation => violation.RuleId == ArchitectureRuleEngine.InternalsVisibleToRule);
    }

    [Fact]
    public void SharedHubReviewUsesDependencyConcentrationNotNames()
    {
        var shared = Project(
            "src/Foundation/SharedState.csproj",
            ArchitectureRole.Core,
            responsibilityReview: new SharedHubReview(
                true,
                "跨三个模块提供无状态只读契约；不拥有领域可变状态。"));

        var modules = new[]
        {
            Project("src/Modules/A/Core/A.csproj", ArchitectureRole.Core, module: "A"),
            Project("src/Modules/B/Core/B.csproj", ArchitectureRole.Core, module: "B"),
            Project("src/Modules/C/Core/C.csproj", ArchitectureRole.Core, module: "C")
        };

        var positiveProjects = modules
            .Select(module => module with
            {
                AllowedProjectReferences = Set(shared.Path)
            })
            .Append(shared)
            .ToArray();

        var edges = modules
            .Select(module => (module.Path, shared.Path))
            .ToArray();

        var positive = Graph(positiveProjects, edges, threshold: 3);
        Assert.Empty(ArchitectureRuleEngine.CheckSharedHubReviews(positive));

        var negativeShared = shared with { ResponsibilityReview = null };
        var negativeProjects = modules
            .Select(module => module with
            {
                AllowedProjectReferences = Set(negativeShared.Path)
            })
            .Append(negativeShared)
            .ToArray();

        var negative = Graph(negativeProjects, edges, threshold: 3);

        Assert.Contains(
            ArchitectureRuleEngine.CheckSharedHubReviews(negative),
            violation => violation.RuleId == ArchitectureRuleEngine.SharedHubReviewRule);
    }

    private static ProjectPolicy Project(
        string path,
        ArchitectureRole role,
        string? module = null,
        IEnumerable<string>? allowedReferences = null,
        IEnumerable<string>? allowedPublicApiReferences = null,
        IEnumerable<string>? allowedInternalsVisibleTo = null,
        SharedHubReview? responsibilityReview = null)
    {
        return new ProjectPolicy(
            path,
            System.IO.Path.GetFileNameWithoutExtension(path),
            role,
            module,
            Set(allowedReferences ?? []),
            Set(allowedPublicApiReferences ?? []),
            Set(allowedInternalsVisibleTo ?? []),
            responsibilityReview);
    }

    private static ProjectGraph Graph(
        IEnumerable<ProjectPolicy> projects,
        IEnumerable<(string Source, string Target)> edges,
        IEnumerable<string>? testAssemblyNames = null,
        int threshold = 3)
    {
        var projectDictionary = projects.ToDictionary(
            project => project.Path,
            StringComparer.Ordinal);

        var references = projectDictionary.Keys.ToDictionary(
            path => path,
            _ => (IReadOnlySet<string>)Set(),
            StringComparer.Ordinal);

        foreach (var group in edges.GroupBy(edge => edge.Source, StringComparer.Ordinal))
        {
            references[group.Key] = Set(group.Select(edge => edge.Target));
        }

        return new ProjectGraph(
            projectDictionary,
            references,
            projectDictionary.Keys.ToHashSet(StringComparer.Ordinal),
            Set(testAssemblyNames ?? []),
            threshold);
    }

    private static HashSet<string> Set(params string[] values)
    {
        return values.ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Set(IEnumerable<string> values)
    {
        return values.ToHashSet(StringComparer.Ordinal);
    }
}
