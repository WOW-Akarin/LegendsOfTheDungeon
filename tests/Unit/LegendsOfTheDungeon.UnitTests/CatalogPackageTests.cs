using LegendsOfTheDungeon.StaticData.Abstractions;
using LegendsOfTheDungeon.StaticData.Core;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class CatalogPackageTests
{
    private static StaticCatalogSchema Schema(string name) => new(name, 1,
    [
        new StaticFieldSchema("name", StaticFieldType.Text, true),
        new StaticFieldSchema("ref", StaticFieldType.Text, false),
        new StaticFieldSchema("link", StaticFieldType.Text, false)
    ]);

    private static StaticDataRow Row(
        string id, string name, string? reference = null, string? link = null)
    {
        var fields = new List<KeyValuePair<string, StaticScalar>>
        {
            new("name", StaticScalar.FromText(name))
        };
        if (reference is not null)
        {
            fields.Add(new("ref", StaticScalar.FromText(reference)));
        }

        if (link is not null)
        {
            fields.Add(new("link", StaticScalar.FromText(link)));
        }

        return new StaticDataRow(id, fields);
    }

    private static FrozenStaticCatalog Catalog(
        string name, int version, params StaticDataRow[] rows) =>
        StaticCatalogBuilder.Build(Schema(name), version, rows, "memory_fixture");

    private static CatalogPackageSnapshot Package(
        int version, params StaticDataRow[] rows) =>
        CatalogPackageBuilder.Build(
            version, [Catalog("nodes", version, rows)],
            Array.Empty<CatalogReferenceRule>());

    private static CatalogReferenceRule Rule(
        string source = "nodes", string field = "ref", string target = "nodes") =>
        new(source, field, target);

    [Fact]
    public void PackageHashIsIndependentOfSourceAndRuleOrdering()
    {
        var a = Catalog("nodes", 3,
            Row("b", "beta", "a"), Row("a", "alpha"));
        var b = Catalog("links", 3, Row("x", "link", "b"));
        var c = Catalog("nodes", 3,
            Row("a", "alpha"), Row("b", "beta", "a"));

        var rules = new[] { Rule(), Rule("links", "ref", "nodes") };
        var first = CatalogPackageBuilder.Build(3, [a, b], rules);
        var second = CatalogPackageBuilder.Build(3, [b, c], rules.Reverse());

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(64, first.ContentHash.Length);
        Assert.Equal(["links", "nodes"], first.Catalogs.Select(x => x.Schema.CatalogName));
        Assert.Equal(first.Find("nodes")!.ContentHash, second.Find("nodes")!.ContentHash);
        Assert.Null(first.Find("missing"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<FrozenStaticCatalog>)first.Catalogs).Clear());
    }

    [Fact]
    public void MissingReferencedRowReportsSourceCatalogFieldAndStableRow()
    {
        var source = Catalog("nodes", 1, Row("a", "A"), Row("b", "B", "not_here"));
        var error = Assert.Throws<CatalogPackageException>(() =>
            CatalogPackageBuilder.Build(1, [source], [Rule()]));
        Assert.Equal(CatalogPackageFault.MissingReference, error.Fault);
        Assert.Equal("nodes", error.Catalog);
        Assert.Equal(2, error.RowIndex);
        Assert.Equal("ref", error.Field);
        Assert.DoesNotContain("not_here", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateCatalogAndVersionMismatchAreRejected()
    {
        var a = Catalog("nodes", 1, Row("a", "A"));
        var duplicate = Assert.Throws<CatalogPackageException>(() =>
            CatalogPackageBuilder.Build(1, [a, a], Array.Empty<CatalogReferenceRule>()));
        Assert.Equal(CatalogPackageFault.DuplicateCatalog, duplicate.Fault);

        var version = Assert.Throws<CatalogPackageException>(() =>
            CatalogPackageBuilder.Build(2, [a], Array.Empty<CatalogReferenceRule>()));
        Assert.Equal(CatalogPackageFault.VersionMismatch, version.Fault);
        Assert.Throws<CatalogPackageException>(() =>
            CatalogPackageBuilder.Build(0, [a], Array.Empty<CatalogReferenceRule>()));
    }

    [Fact]
    public void MissingCatalogAndInvalidFieldTypeAreRejected()
    {
        var a = Catalog("nodes", 1, Row("a", "A"));
        Assert.Equal(CatalogPackageFault.MissingCatalog,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1, [a],
                    [Rule("nodes", "ref", "missing")])).Fault);
        Assert.Equal(CatalogPackageFault.InvalidReferenceRule,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1, [a],
                    [Rule("nodes", "unknown", "nodes")])).Fault);
        var typed = StaticCatalogBuilder.Build(new StaticCatalogSchema("numeric", 1,
        [
            new StaticFieldSchema("amount", StaticFieldType.WholeNumber, true)
        ]), 1, [new StaticDataRow("a", [
            new KeyValuePair<string, StaticScalar>("amount", StaticScalar.FromInt64(3))
        ])], "memory_fixture");
        Assert.Equal(CatalogPackageFault.InvalidReferenceRule,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1, [typed],
                    [Rule("numeric", "amount", "numeric")])).Fault);
    }

    [Fact]
    public void SelfAndCrossCatalogCyclesAreRejected()
    {
        var one = Catalog("nodes", 1, Row("a", "A", "a"));
        Assert.Equal(CatalogPackageFault.ReferenceCycle,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1, [one], [Rule()])).Fault);
        var nodes = Catalog("nodes", 1, Row("a", "A", link: "b"));
        var links = Catalog("links", 1, Row("b", "B", reference: "a"));
        var cycle = Assert.Throws<CatalogPackageException>(() =>
            CatalogPackageBuilder.Build(1, [nodes, links],
            [
                Rule("nodes", "link", "links"),
                Rule("links", "ref", "nodes")
            ]));
        Assert.Equal(CatalogPackageFault.ReferenceCycle, cycle.Fault);
        Assert.Equal("nodes", cycle.Catalog);
        Assert.Equal(1, cycle.RowIndex);
    }

    [Fact]
    public void DeepAcyclicChainDoesNotUseRecursiveCallStack()
    {
        var records = Enumerable.Range(0, 10_000)
            .Select(static n => Row(n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "node",
                n == 9_999 ? null : (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        var catalog = Catalog("nodes", 1, records);
        var package = CatalogPackageBuilder.Build(1, [catalog], [Rule()]);
        Assert.Equal(10_000, package.Find("nodes")!.Records.Count);
        Assert.Equal(64, package.ContentHash.Length);
    }

    [Fact]
    public void DuplicateRulesAndEmptyPackageAreRejected()
    {
        var a = Catalog("nodes", 1, Row("a", "A"));
        Assert.Equal(CatalogPackageFault.InvalidReferenceRule,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1, [a], [Rule(), Rule()])).Fault);
        Assert.Equal(CatalogPackageFault.MissingCatalog,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1,
                    Array.Empty<FrozenStaticCatalog>(),
                    Array.Empty<CatalogReferenceRule>())).Fault);
    }

    [Fact]
    public void PackageRejectsCatalogAndRuleBudgetsInsteadOfAllocatingUnboundedGraphs()
    {
        var tooMany = Enumerable.Range(0, CatalogPackageBuilder.MaximumCatalogs + 1)
            .Select(i => Catalog("catalog_" + i, 1, Row("id", "name"))).ToArray();
        Assert.Equal(CatalogPackageFault.LimitExceeded,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1, tooMany,
                    Array.Empty<CatalogReferenceRule>())).Fault);

        var catalogs = Enumerable.Range(0, CatalogPackageBuilder.MaximumCatalogs)
            .Select(i => Catalog("catalog_" + i, 1, Row("id", "name"))).ToArray();
        var rules = Enumerable.Range(0, CatalogPackageBuilder.MaximumReferenceRules + 1)
            .Select(i => Rule("catalog_" + (i % CatalogPackageBuilder.MaximumCatalogs),
                "ref", "catalog_" + (i / CatalogPackageBuilder.MaximumCatalogs))).ToArray();
        Assert.Equal(CatalogPackageFault.LimitExceeded,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(1, catalogs, rules)).Fault);
    }

    [Fact]
    public void EmptyReferencedCatalogDoesNotCrashCycleDetector()
    {
        var empty = Catalog("nodes", 1);
        var package = CatalogPackageBuilder.Build(1, [empty], [Rule()]);
        Assert.Empty(package.Find("nodes")!.Records);
        Assert.Equal(64, package.ContentHash.Length);
    }

    [Fact]
    public void UnpinnedOldSnapshotsAreEvictedAtBoundedHistoryLimit()
    {
        var original = Package(1, Row("a", "first"));
        var registry = new CatalogReloadCoordinator(original, maximumHistory: 2);
        using (var tick = registry.CaptureTick(1)) { }
        registry.CommitAtBoundary(1, registry.Stage(Package(2, Row("a", "second"))));
        Assert.Same(original, registry.FindRetained(1, original.ContentHash));
        using (var tick = registry.CaptureTick(2)) { }
        registry.CommitAtBoundary(2, registry.Stage(Package(3, Row("a", "third"))));
        Assert.Null(registry.FindRetained(1, original.ContentHash));
        Assert.Equal([2, 3], registry.RetainedSnapshots().Select(x => x.Version));
    }

    [Fact]
    public void AllViewsOfTickKeepSamePackageUntilBoundaryCommit()
    {
        var first = Package(1, Row("a", "original"));
        var second = Package(2, Row("a", "updated"), Row("b", "new"));
        var registry = new CatalogReloadCoordinator(first, 3);
        using var tick1 = registry.CaptureTick(1);
        using var concurrentView = registry.CaptureTick(1);
        var ticket = registry.Stage(second);
        Assert.Same(first, registry.Current);
        Assert.Same(first, tick1.Package);
        Assert.Same(first, concurrentView.Package);
        Assert.Equal(CatalogReloadFault.TickActive,
            Assert.Throws<CatalogReloadException>(() =>
                registry.CommitAtBoundary(1, ticket)).Fault);
        Assert.Equal(CatalogReloadFault.TickActive,
            Assert.Throws<CatalogReloadException>(() => registry.CaptureTick(2)).Fault);
        concurrentView.Dispose();
        Assert.Equal(CatalogReloadFault.TickActive,
            Assert.Throws<CatalogReloadException>(() =>
                registry.CommitAtBoundary(1, ticket)).Fault);
        tick1.Dispose();
        Assert.Equal(CatalogReloadFault.InvalidBoundary,
            Assert.Throws<CatalogReloadException>(() =>
                registry.CommitAtBoundary(2, ticket)).Fault);
        registry.CommitAtBoundary(1, ticket);
        Assert.Same(second, registry.Current);
        Assert.Same(first, tick1.Package);
        Assert.Same(first, registry.FindRetained(1, first.ContentHash));
        using var tick2 = registry.CaptureTick(2);
        Assert.Same(second, tick2.Package);
        Assert.Null(tick2.Package.Find("links"));
        Assert.Equal(CatalogReloadFault.StaleTicket,
            Assert.Throws<CatalogReloadException>(() =>
                registry.CommitAtBoundary(1, ticket)).Fault);
    }

    [Fact]
    public async Task MalformedNewPackageAndCancelledPreparationLeavePreviousVisibleAsync()
    {
        var old = Package(1, Row("a", "good"));
        var registry = new CatalogReloadCoordinator(old);
        Assert.Equal(CatalogPackageFault.MissingReference,
            Assert.Throws<CatalogPackageException>(() =>
                CatalogPackageBuilder.Build(2,
                    [Catalog("nodes", 2, Row("a", "bad", "missing"))],
                    [Rule()])).Fault);
        Assert.Same(old, registry.Current);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.PrepareAsync(_ => Task.FromResult(Package(2, Row("a", "new"))),
                cancelled.Token));
        Assert.Same(old, registry.Current);
        using var firstTick = registry.CaptureTick(1);
        firstTick.Dispose();
        var staged = registry.Stage(Package(2, Row("a", "new")));
        registry.Abandon(staged);
        Assert.Equal(CatalogReloadFault.StaleTicket,
            Assert.Throws<CatalogReloadException>(() =>
                registry.CommitAtBoundary(1, staged)).Fault);
        Assert.Same(old, registry.Current);
    }

    [Fact]
    public void RejectChangeAllowsAppendButForbidsActiveDeletionOrMutation()
    {
        var old = Package(1, Row("a", "original"));
        var registry = new CatalogReloadCoordinator(old);
        using var use = registry.RegisterActiveDefinition(
            Guid.NewGuid(), "nodes", "a", ActiveDefinitionPolicy.RejectChange);
        Assert.Equal(CatalogReloadFault.ActiveDefinition,
            Assert.Throws<CatalogReloadException>(() =>
                registry.Stage(Package(2, Row("a", "mutated")))).Fault);
        Assert.Equal(CatalogReloadFault.ActiveDefinition,
            Assert.Throws<CatalogReloadException>(() =>
                registry.Stage(Package(2, Row("other", "added")))).Fault);
        var allowed = registry.Stage(Package(2, Row("a", "original"), Row("b", "append")));
        using var tick = registry.CaptureTick(1);
        tick.Dispose();
        registry.CommitAtBoundary(1, allowed);
        Assert.Equal(2, registry.Current.Version);
        Assert.Equal("original", registry.Current.Find("nodes")!.Find("a")!.Fields["name"].CanonicalText);
    }

    [Fact]
    public void RequireMigrationNeverPretendsToMigrateActiveInstance()
    {
        var registry = new CatalogReloadCoordinator(Package(1, Row("a", "old")));
        using var use = registry.RegisterActiveDefinition(
            Guid.NewGuid(), "nodes", "a", ActiveDefinitionPolicy.RequireMigration);
        Assert.Equal(CatalogReloadFault.MigrationRequired,
            Assert.Throws<CatalogReloadException>(() =>
                registry.Stage(Package(2, Row("a", "changed")))).Fault);
        Assert.Equal(CatalogReloadFault.MigrationRequired,
            Assert.Throws<CatalogReloadException>(() =>
                registry.Stage(Package(2, Row("b", "replacement")))).Fault);
        Assert.Equal(1, registry.Current.Version);
    }

    [Fact]
    public void PinnedOldVersionRemainsUsableAndLimitsNewReload()
    {
        var v1 = Package(1, Row("a", "old"));
        var registry = new CatalogReloadCoordinator(v1, maximumHistory: 2);
        using var oldLease = registry.RegisterActiveDefinition(
            Guid.NewGuid(), "nodes", "a", ActiveDefinitionPolicy.PinOldVersion);
        using (var tick = registry.CaptureTick(1)) { }
        registry.CommitAtBoundary(1, registry.Stage(Package(2, Row("b", "new"))));
        Assert.Same(v1, registry.FindRetained(1, v1.ContentHash));
        using var newLease = registry.RegisterActiveDefinition(
            Guid.NewGuid(), "nodes", "b", ActiveDefinitionPolicy.PinOldVersion);
        Assert.Equal(CatalogReloadFault.RetentionFull,
            Assert.Throws<CatalogReloadException>(() =>
                registry.Stage(Package(3, Row("c", "newest")))).Fault);
        Assert.Equal(2, registry.Current.Version);
        oldLease.Dispose();
        var next = registry.Stage(Package(3, Row("c", "newest")));
        using (var tick = registry.CaptureTick(2)) { }
        registry.CommitAtBoundary(2, next);
        Assert.Null(registry.FindRetained(1, v1.ContentHash));
        Assert.Equal([2, 3], registry.RetainedSnapshots().Select(x => x.Version));
        Assert.NotNull(registry.FindRetained(3, next.Candidate.ContentHash));
        Assert.Equal(2, registry.RetainedSnapshots().Count);
    }

    [Fact]
    public void StagedReloadRechecksActiveBindingsAtActualCommitBoundary()
    {
        var registry = new CatalogReloadCoordinator(Package(1, Row("a", "old")));
        var ticket = registry.Stage(Package(2, Row("a", "changed")));
        using (var tick = registry.CaptureTick(1)) { }
        using var use = registry.RegisterActiveDefinition(
            Guid.NewGuid(), "nodes", "a", ActiveDefinitionPolicy.RejectChange);
        Assert.Equal(CatalogReloadFault.ActiveDefinition,
            Assert.Throws<CatalogReloadException>(() =>
                registry.CommitAtBoundary(1, ticket)).Fault);
        Assert.Equal(1, registry.Current.Version);
        use.Dispose();
        registry.CommitAtBoundary(1, ticket);
        Assert.Equal(2, registry.Current.Version);
    }

    [Fact]
    public async Task AsyncPreparationDoesNotExposeCandidateBeforeCommit()
    {
        var first = Package(1, Row("a", "old"));
        var candidate = Package(2, Row("a", "new"));
        var registry = new CatalogReloadCoordinator(first);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var awaiting = registry.PrepareAsync(async token =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return candidate;
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, registry.Current);
        release.SetResult();
        var ticket = await awaiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, registry.Current);
        Assert.Equal(CatalogReloadFault.AlreadyStaged,
            Assert.Throws<CatalogReloadException>(() =>
                registry.Stage(Package(3, Row("a", "newer")))).Fault);
        using (var tick = registry.CaptureTick(1)) { }
        registry.CommitAtBoundary(1, ticket);
        Assert.Same(candidate, registry.Current);
    }
}
