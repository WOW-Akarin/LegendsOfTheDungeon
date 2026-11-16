using System.Text.Json;
using System.Xml.Linq;

namespace LegendsOfTheDungeon.ArchitectureTests;

internal static class RepositoryArchitectureLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "LegendsOfTheDungeon.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法从测试输出目录定位 LegendsOfTheDungeon.slnx。");
    }

    public static ProjectGraph Load(string repositoryRoot)
    {
        var manifestPath = System.IO.Path.Combine(
            repositoryRoot,
            "eng",
            "architecture",
            "production-projects.json");

        var manifest = JsonSerializer.Deserialize<ArchitectureManifestDto>(
            File.ReadAllText(manifestPath),
            JsonOptions)
            ?? throw new InvalidDataException("架构清单为空。");

        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException($"不支持的架构清单 schemaVersion: {manifest.SchemaVersion}。");
        }

        if (manifest.SharedHubModuleFanInThreshold < 2)
        {
            throw new InvalidDataException("共享职责审查 fan-in 阈值必须至少为 2。");
        }

        var solutionPath = System.IO.Path.Combine(repositoryRoot, "LegendsOfTheDungeon.slnx");
        var solution = XDocument.Load(solutionPath);
        var solutionProjects = solution
            .Descendants("Project")
            .Select(element => NormalizePath((string?)element.Attribute("Path") ?? string.Empty))
            .Where(path => path.Length > 0)
            .ToArray();

        var productionPaths = solutionProjects
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var testAssemblyNames = solutionProjects
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal))
            .Select(path => ReadAssemblyName(repositoryRoot, path))
            .ToHashSet(StringComparer.Ordinal);

        var projects = new Dictionary<string, ProjectPolicy>(StringComparer.Ordinal);

        foreach (var dto in manifest.Projects)
        {
            var path = NormalizePath(dto.Path);

            if (path.Length == 0)
            {
                throw new InvalidDataException("架构清单包含空 project path。");
            }

            if (!Enum.TryParse<ArchitectureRole>(dto.Role, ignoreCase: true, out var role))
            {
                throw new InvalidDataException($"项目 {path} 的架构角色无效: {dto.Role}。");
            }

            var policy = new ProjectPolicy(
                path,
                dto.AssemblyName,
                role,
                string.IsNullOrWhiteSpace(dto.Module) ? null : dto.Module,
                NormalizeSet(dto.AllowedProjectReferences),
                NormalizeSet(dto.AllowedPublicApiReferences),
                dto.AllowedInternalsVisibleTo.ToHashSet(StringComparer.Ordinal),
                dto.ResponsibilityReview is null
                    ? null
                    : new SharedHubReview(
                        dto.ResponsibilityReview.Approved,
                        dto.ResponsibilityReview.Reason));

            if (!projects.TryAdd(path, policy))
            {
                throw new InvalidDataException($"架构清单重复声明项目: {path}。");
            }
        }

        var references = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var projectPath in productionPaths)
        {
            references[projectPath] = ReadProjectReferences(repositoryRoot, projectPath);
        }

        return new ProjectGraph(
            projects,
            references,
            productionPaths,
            testAssemblyNames,
            manifest.SharedHubModuleFanInThreshold);
    }

    private static IReadOnlySet<string> ReadProjectReferences(
        string repositoryRoot,
        string projectPath)
    {
        var fullProjectPath = System.IO.Path.Combine(
            repositoryRoot,
            projectPath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        var document = XDocument.Load(fullProjectPath);
        var projectDirectory = System.IO.Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidDataException($"无法确定项目目录: {projectPath}。");

        return document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => System.IO.Path.GetFullPath(
                System.IO.Path.Combine(projectDirectory, include!)))
            .Select(fullPath => NormalizePath(
                System.IO.Path.GetRelativePath(repositoryRoot, fullPath)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ReadAssemblyName(string repositoryRoot, string projectPath)
    {
        var fullProjectPath = System.IO.Path.Combine(
            repositoryRoot,
            projectPath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        var document = XDocument.Load(fullProjectPath);
        var explicitName = document
            .Descendants("AssemblyName")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        return explicitName
            ?? System.IO.Path.GetFileNameWithoutExtension(projectPath);
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string> paths)
    {
        return paths
            .Select(NormalizePath)
            .Where(path => path.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static string NormalizePath(string path)
    {
        return path
            .Replace('\\', '/')
            .Trim()
            .TrimStart('.', '/');
    }

    private sealed class ArchitectureManifestDto
    {
        public int SchemaVersion { get; init; }

        public int SharedHubModuleFanInThreshold { get; init; }

        public List<ProjectPolicyDto> Projects { get; init; } = [];
    }

    private sealed class ProjectPolicyDto
    {
        public string Path { get; init; } = string.Empty;

        public string AssemblyName { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        public string? Module { get; init; }

        public List<string> AllowedProjectReferences { get; init; } = [];

        public List<string> AllowedPublicApiReferences { get; init; } = [];

        public List<string> AllowedInternalsVisibleTo { get; init; } = [];

        public SharedHubReviewDto? ResponsibilityReview { get; init; }
    }

    private sealed class SharedHubReviewDto
    {
        public bool Approved { get; init; }

        public string Reason { get; init; } = string.Empty;
    }
}
