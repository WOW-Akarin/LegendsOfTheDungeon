namespace LegendsOfTheDungeon.ArchitectureTests;

internal static class ArchitectureRuleEngine
{
    public const string ExplicitWhitelistRule = "P03-001";
    public const string DomainIsolationRule = "P03-002";
    public const string CrossModuleImplementationRule = "P03-003";
    public const string ModuleHostRule = "P03-004";
    public const string AcyclicReferenceRule = "P03-005";
    public const string PublicApiLeakRule = "P03-006";
    public const string InternalsVisibleToRule = "P03-007";
    public const string SharedHubReviewRule = "P03-008";

    public static IReadOnlyList<ArchitectureViolation> CheckExplicitWhitelist(ProjectGraph graph)
    {
        var violations = new List<ArchitectureViolation>();

        foreach (var projectPath in graph.SolutionProductionProjectPaths)
        {
            if (!graph.Projects.ContainsKey(projectPath))
            {
                violations.Add(new ArchitectureViolation(
                    ExplicitWhitelistRule,
                    projectPath,
                    null,
                    "Solution 中的生产项目未出现在显式架构清单。"));
            }
        }

        foreach (var policy in graph.Projects.Values)
        {
            if (!graph.SolutionProductionProjectPaths.Contains(policy.Path))
            {
                violations.Add(new ArchitectureViolation(
                    ExplicitWhitelistRule,
                    policy.Path,
                    null,
                    "架构清单项目不在 Solution 的生产项目集合中。"));
                continue;
            }

            var actual = graph.References.TryGetValue(policy.Path, out var references)
                ? references
                : EmptySet();

            foreach (var target in actual.Except(policy.AllowedProjectReferences, StringComparer.Ordinal))
            {
                violations.Add(new ArchitectureViolation(
                    ExplicitWhitelistRule,
                    policy.Path,
                    target,
                    "真实 ProjectReference 未进入显式白名单。"));
            }

            foreach (var target in policy.AllowedProjectReferences.Except(actual, StringComparer.Ordinal))
            {
                violations.Add(new ArchitectureViolation(
                    ExplicitWhitelistRule,
                    policy.Path,
                    target,
                    "白名单声明了并不存在的 ProjectReference；清单必须精确反映真实项目图。"));
            }

            foreach (var target in policy.AllowedProjectReferences)
            {
                if (!graph.Projects.ContainsKey(target))
                {
                    violations.Add(new ArchitectureViolation(
                        ExplicitWhitelistRule,
                        policy.Path,
                        target,
                        "白名单目标不是已声明的生产项目。"));
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<ArchitectureViolation> CheckForbiddenProjectReferences(ProjectGraph graph)
    {
        var violations = new List<ArchitectureViolation>();

        foreach (var (sourcePath, targets) in graph.References)
        {
            if (!graph.Projects.TryGetValue(sourcePath, out var source))
            {
                continue;
            }

            foreach (var targetPath in targets)
            {
                if (!graph.Projects.TryGetValue(targetPath, out var target))
                {
                    continue;
                }

                if (source.Role == ArchitectureRole.Domain
                    && target.Role is ArchitectureRole.PlatformAdapter or ArchitectureRole.Host)
                {
                    violations.Add(new ArchitectureViolation(
                        DomainIsolationRule,
                        sourcePath,
                        targetPath,
                        "Domain 不得依赖 Platform Adapter 或 Host。"));
                }

                if (source.Module is not null
                    && target.Module is not null
                    && !string.Equals(source.Module, target.Module, StringComparison.Ordinal)
                    && target.Role is ArchitectureRole.Infrastructure or ArchitectureRole.Internal)
                {
                    violations.Add(new ArchitectureViolation(
                        CrossModuleImplementationRule,
                        sourcePath,
                        targetPath,
                        "模块不得引用其他模块的 Infrastructure/Internal。"));
                }

                if (source.Module is not null && target.Role == ArchitectureRole.Host)
                {
                    violations.Add(new ArchitectureViolation(
                        ModuleHostRule,
                        sourcePath,
                        targetPath,
                        "模块不得依赖 Host。"));
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<ArchitectureViolation> CheckAcyclicReferences(ProjectGraph graph)
    {
        var indegree = graph.Projects.Keys.ToDictionary(
            path => path,
            _ => 0,
            StringComparer.Ordinal);

        foreach (var (source, targets) in graph.References)
        {
            if (!graph.Projects.ContainsKey(source))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (indegree.TryGetValue(target, out var targetIndegree))
                {
                    indegree[target] = targetIndegree + 1;
                }
            }
        }

        var queue = new Queue<string>(
            indegree
                .Where(pair => pair.Value == 0)
                .Select(pair => pair.Key)
                .OrderBy(path => path, StringComparer.Ordinal));

        var visited = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;

            if (!graph.References.TryGetValue(current, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (!indegree.TryGetValue(target, out var targetIndegree))
                {
                    continue;
                }

                targetIndegree--;
                indegree[target] = targetIndegree;

                if (targetIndegree == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        if (visited == graph.Projects.Count)
        {
            return [];
        }

        var cycleMembers = indegree
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key)
            .OrderBy(path => path, StringComparer.Ordinal);

        return
        [
            new ArchitectureViolation(
                AcyclicReferenceRule,
                string.Join(", ", cycleMembers),
                null,
                "生产 ProjectReference 图存在循环。")
        ];
    }

    public static IReadOnlyList<ArchitectureViolation> CheckPublicApiExposures(
        ProjectGraph graph,
        IEnumerable<ApiExposure> exposures)
    {
        var violations = new List<ArchitectureViolation>();

        foreach (var exposure in exposures)
        {
            if (!graph.Projects.TryGetValue(exposure.SourceProjectPath, out var source)
                || !graph.Projects.TryGetValue(exposure.TargetProjectPath, out var target)
                || string.Equals(source.Path, target.Path, StringComparison.Ordinal))
            {
                continue;
            }

            var forbiddenImplementation = target.Role is
                ArchitectureRole.Infrastructure
                or ArchitectureRole.Internal
                or ArchitectureRole.PlatformAdapter
                or ArchitectureRole.Host;

            if (forbiddenImplementation
                || !source.AllowedPublicApiReferences.Contains(target.Path))
            {
                violations.Add(new ArchitectureViolation(
                    PublicApiLeakRule,
                    source.Path,
                    target.Path,
                    $"公共 API 泄漏未批准的生产类型: {exposure.Member}。"));
            }
        }

        return violations;
    }

    public static IReadOnlyList<ArchitectureViolation> CheckInternalsVisibleTo(
        ProjectGraph graph,
        IEnumerable<FriendAccess> friendAccesses)
    {
        var violations = new List<ArchitectureViolation>();
        var actualBySource = friendAccesses
            .GroupBy(access => access.SourceProjectPath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(access => access.FriendAssemblyName)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        foreach (var project in graph.Projects.Values)
        {
            var actual = actualBySource.TryGetValue(project.Path, out var friends)
                ? friends
                : EmptySet();

            foreach (var friend in actual.Except(
                project.AllowedInternalsVisibleTo,
                StringComparer.Ordinal))
            {
                violations.Add(new ArchitectureViolation(
                    InternalsVisibleToRule,
                    project.Path,
                    friend,
                    "InternalsVisibleTo 未进入显式白名单。"));
            }

            foreach (var friend in project.AllowedInternalsVisibleTo.Except(
                actual,
                StringComparer.Ordinal))
            {
                violations.Add(new ArchitectureViolation(
                    InternalsVisibleToRule,
                    project.Path,
                    friend,
                    "IVT 白名单声明了并不存在的友元关系。"));
            }

            foreach (var friend in actual)
            {
                if (!graph.TestAssemblyNames.Contains(friend))
                {
                    violations.Add(new ArchitectureViolation(
                        InternalsVisibleToRule,
                        project.Path,
                        friend,
                        "生产项目只能向 Solution 中明确的测试 Assembly 开放 InternalsVisibleTo。"));
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<ArchitectureViolation> CheckSharedHubReviews(ProjectGraph graph)
    {
        var incomingModules = graph.Projects.Keys.ToDictionary(
            path => path,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var (sourcePath, targets) in graph.References)
        {
            if (!graph.Projects.TryGetValue(sourcePath, out var source)
                || source.Module is null)
            {
                continue;
            }

            foreach (var targetPath in targets)
            {
                if (incomingModules.TryGetValue(targetPath, out var modules))
                {
                    modules.Add(source.Module);
                }
            }
        }

        var violations = new List<ArchitectureViolation>();

        foreach (var project in graph.Projects.Values)
        {
            var moduleCount = incomingModules[project.Path].Count;

            if (moduleCount < graph.SharedHubModuleFanInThreshold)
            {
                continue;
            }

            var review = project.ResponsibilityReview;

            if (review is null
                || !review.Approved
                || string.IsNullOrWhiteSpace(review.Reason))
            {
                violations.Add(new ArchitectureViolation(
                    SharedHubReviewRule,
                    project.Path,
                    null,
                    $"项目被 {moduleCount} 个模块引用，达到共享职责审查阈值，但缺少已批准的职责说明。"));
            }
        }

        return violations;
    }

    private static HashSet<string> EmptySet()
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }
}
