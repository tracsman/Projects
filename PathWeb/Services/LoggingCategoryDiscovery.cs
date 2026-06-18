using System.Reflection;
using PathWeb.Models;

namespace PathWeb.Services;

/// <summary>
/// Discovers the set of logging categories produced by PathWeb at runtime by
/// reflecting over the application assembly for types that inject
/// <c>ILogger&lt;T&gt;</c>. The catalog is computed lazily on first request and
/// cached for the lifetime of the process; reflection cost is paid once (~5 ms)
/// the first time the Settings page is opened.
/// </summary>
public sealed class LoggingCategoryDiscovery
{
    private readonly Lazy<LoggingCategoryCatalog> _catalog;

    public LoggingCategoryDiscovery()
    {
        _catalog = new Lazy<LoggingCategoryCatalog>(BuildCatalog, isThreadSafe: true);
    }

    public LoggingCategoryCatalog GetCatalog() => _catalog.Value;

    private static LoggingCategoryCatalog BuildCatalog()
    {
        var assembly = typeof(LoggingCategoryDiscovery).Assembly;
        var rootNamespace = typeof(LoggingCategoryDiscovery).Namespace?.Split('.')[0] ?? "PathWeb";

        var categories = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in SafeGetTypes(assembly))
        {
            if (type is null || !type.IsClass || type.IsAbstract || string.IsNullOrEmpty(type.FullName))
                continue;

            if (!type.FullName.StartsWith(rootNamespace + ".", StringComparison.Ordinal))
                continue;

            if (!InjectsILogger(type))
                continue;

            categories.Add(type.FullName);
        }

        var groups = categories
            .GroupBy(GetGroupName)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new LoggingCategoryGroup
            {
                GroupName = g.Key,
                Categories = g.OrderBy(c => c, StringComparer.Ordinal).ToList()
            })
            .ToList();

        var prefixOverrides = new List<string> { rootNamespace };
        prefixOverrides.AddRange(groups
            .Select(g => g.GroupName)
            .Where(n => !string.Equals(n, rootNamespace, StringComparison.Ordinal) &&
                        !string.Equals(n, "(Other)", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal));

        return new LoggingCategoryCatalog
        {
            Groups = groups,
            PrefixOverrides = prefixOverrides,
            ExcludedPrefixes = DbLoggerProvider.ExcludedPrefixes.OrderBy(p => p, StringComparer.Ordinal).ToList()
        };
    }

    private static IEnumerable<Type?> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
    }

    private static bool InjectsILogger(Type type)
    {
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in ctor.GetParameters())
            {
                var pt = parameter.ParameterType;
                if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(ILogger<>))
                    return true;
            }
        }

        return false;
    }

    private static string GetGroupName(string fullTypeName)
    {
        // Group on the second namespace segment (e.g., PathWeb.Controllers.X -> "PathWeb.Controllers").
        var firstDot = fullTypeName.IndexOf('.');
        if (firstDot < 0) return "(Other)";

        var secondDot = fullTypeName.IndexOf('.', firstDot + 1);
        return secondDot < 0
            ? fullTypeName[..firstDot]
            : fullTypeName[..secondDot];
    }
}
