namespace LegendsOfTheDungeon.ArchitectureTests;

internal enum ArchitectureRole
{
    Foundation,
    Abstraction,
    Contract,
    Domain,
    Core,
    Infrastructure,
    Internal,
    PlatformAdapter,
    Host,
    Tool
}

internal sealed record SharedHubReview(bool Approved, string Reason);

internal sealed record ProjectPolicy(
    string Path,
    string AssemblyName,
    ArchitectureRole Role,
    string? Module,
    IReadOnlySet<string> AllowedProjectReferences,
    IReadOnlySet<string> AllowedPublicApiReferences,
    IReadOnlySet<string> AllowedInternalsVisibleTo,
    SharedHubReview? ResponsibilityReview);

internal sealed record ProjectGraph(
    IReadOnlyDictionary<string, ProjectPolicy> Projects,
    IReadOnlyDictionary<string, IReadOnlySet<string>> References,
    IReadOnlySet<string> SolutionProductionProjectPaths,
    IReadOnlySet<string> TestAssemblyNames,
    int SharedHubModuleFanInThreshold);

internal sealed record ArchitectureViolation(
    string RuleId,
    string Source,
    string? Target,
    string Message);

internal sealed record ApiExposure(
    string SourceProjectPath,
    string TargetProjectPath,
    string Member);

internal sealed record FriendAccess(
    string SourceProjectPath,
    string FriendAssemblyName);
