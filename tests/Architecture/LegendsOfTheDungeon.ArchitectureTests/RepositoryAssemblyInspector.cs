using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace LegendsOfTheDungeon.ArchitectureTests;

internal static class RepositoryAssemblyInspector
{
    public static IReadOnlyDictionary<string, Assembly> LoadProductionAssemblies(
        string repositoryRoot,
        ProjectGraph graph)
    {
        var result = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        foreach (var project in graph.Projects.Values)
        {
            var existing = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    project.AssemblyName,
                    StringComparison.Ordinal));

            if (existing is not null)
            {
                result[project.Path] = existing;
                continue;
            }

            var projectDirectory = System.IO.Path.GetDirectoryName(
                System.IO.Path.Combine(
                    repositoryRoot,
                    project.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)))
                ?? throw new InvalidDataException($"无法确定项目目录: {project.Path}。");

            var assemblyPath = System.IO.Path.Combine(
                projectDirectory,
                "bin",
                "Release",
                "net10.0",
                project.AssemblyName + ".dll");

            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    $"找不到已构建的生产 Assembly: {assemblyPath}。",
                    assemblyPath);
            }

            result[project.Path] = AssemblyLoadContext.Default.LoadFromAssemblyPath(
                System.IO.Path.GetFullPath(assemblyPath));
        }

        return result;
    }

    public static IReadOnlyList<ApiExposure> ScanPublicApi(
        ProjectGraph graph,
        IReadOnlyDictionary<string, Assembly> assemblies)
    {
        var projectByAssemblyName = graph.Projects.Values.ToDictionary(
            project => project.AssemblyName,
            project => project.Path,
            StringComparer.Ordinal);

        var exposures = new List<ApiExposure>();

        foreach (var (sourcePath, assembly) in assemblies)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                AddTypeExposure(
                    sourcePath,
                    $"type {type.FullName}",
                    type.BaseType,
                    projectByAssemblyName,
                    exposures);

                foreach (var implementedInterface in type.GetInterfaces())
                {
                    AddTypeExposure(
                        sourcePath,
                        $"interface on {type.FullName}",
                        implementedInterface,
                        projectByAssemblyName,
                        exposures);
                }

                foreach (var constructor in type.GetConstructors(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    foreach (var parameter in constructor.GetParameters())
                    {
                        AddTypeExposure(
                            sourcePath,
                            $"constructor {type.FullName}",
                            parameter.ParameterType,
                            projectByAssemblyName,
                            exposures);
                    }
                }

                foreach (var method in type.GetMethods(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                    .Where(IsExternallyVisible))
                {
                    AddTypeExposure(
                        sourcePath,
                        $"method {type.FullName}.{method.Name} return",
                        method.ReturnType,
                        projectByAssemblyName,
                        exposures);

                    foreach (var parameter in method.GetParameters())
                    {
                        AddTypeExposure(
                            sourcePath,
                            $"method {type.FullName}.{method.Name} parameter {parameter.Name}",
                            parameter.ParameterType,
                            projectByAssemblyName,
                            exposures);
                    }

                    foreach (var genericArgument in method.GetGenericArguments())
                    {
                        foreach (var constraint in genericArgument.GetGenericParameterConstraints())
                        {
                            AddTypeExposure(
                                sourcePath,
                                $"method {type.FullName}.{method.Name} generic constraint",
                                constraint,
                                projectByAssemblyName,
                                exposures);
                        }
                    }
                }

                foreach (var property in type.GetProperties(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                    .Where(property => property
                        .GetAccessors(nonPublic: true)
                        .Any(IsExternallyVisible)))
                {
                    AddTypeExposure(
                        sourcePath,
                        $"property {type.FullName}.{property.Name}",
                        property.PropertyType,
                        projectByAssemblyName,
                        exposures);
                }

                foreach (var field in type.GetFields(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                    .Where(IsExternallyVisible))
                {
                    AddTypeExposure(
                        sourcePath,
                        $"field {type.FullName}.{field.Name}",
                        field.FieldType,
                        projectByAssemblyName,
                        exposures);
                }

                foreach (var eventInfo in type.GetEvents(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                    .Where(eventInfo => eventInfo
                        .GetAddMethod(nonPublic: true) is { } addMethod
                        && IsExternallyVisible(addMethod)))
                {
                    AddTypeExposure(
                        sourcePath,
                        $"event {type.FullName}.{eventInfo.Name}",
                        eventInfo.EventHandlerType,
                        projectByAssemblyName,
                        exposures);
                }
            }
        }

        return exposures
            .Distinct()
            .ToArray();
    }

    public static IReadOnlyList<FriendAccess> ScanInternalsVisibleTo(
        IReadOnlyDictionary<string, Assembly> assemblies)
    {
        return assemblies
            .SelectMany(pair => pair.Value
                .GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => new FriendAccess(
                    pair.Key,
                    ParseFriendAssemblyName(attribute.AssemblyName))))
            .ToArray();
    }

    private static bool IsExternallyVisible(MethodBase method)
    {
        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

    private static bool IsExternallyVisible(FieldInfo field)
    {
        return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
    }

    private static void AddTypeExposure(
        string sourcePath,
        string member,
        Type? type,
        IReadOnlyDictionary<string, string> projectByAssemblyName,
        ICollection<ApiExposure> exposures)
    {
        if (type is null)
        {
            return;
        }

        var visited = new HashSet<Type>();
        var stack = new Stack<Type>();
        stack.Push(type);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (!visited.Add(current))
            {
                continue;
            }

            if (current.IsGenericParameter)
            {
                foreach (var constraint in current.GetGenericParameterConstraints())
                {
                    stack.Push(constraint);
                }

                continue;
            }

            if (current.HasElementType && current.GetElementType() is { } elementType)
            {
                stack.Push(elementType);
            }

            if (current.IsGenericType)
            {
                stack.Push(current.GetGenericTypeDefinition());

                foreach (var argument in current.GetGenericArguments())
                {
                    stack.Push(argument);
                }
            }

            var assemblyName = current.Assembly.GetName().Name;

            if (assemblyName is not null
                && projectByAssemblyName.TryGetValue(assemblyName, out var targetPath)
                && !string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
            {
                exposures.Add(new ApiExposure(sourcePath, targetPath, member));
            }
        }
    }

    private static string ParseFriendAssemblyName(string value)
    {
        var separator = value.IndexOf(',', StringComparison.Ordinal);
        return separator < 0 ? value.Trim() : value[..separator].Trim();
    }
}
