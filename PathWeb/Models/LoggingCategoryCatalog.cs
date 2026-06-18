namespace PathWeb.Models;

/// <summary>
/// Lightweight catalog of categories that can appear in the SQL application log,
/// surfaced in the Settings UI so administrators can author logging overrides
/// against the exact category names the runtime will use.
/// </summary>
public class LoggingCategoryCatalog
{
    /// <summary>
    /// Concrete categories discovered by reflection (full type names of classes
    /// that inject <c>ILogger&lt;T&gt;</c>), grouped by their parent namespace.
    /// </summary>
    public List<LoggingCategoryGroup> Groups { get; set; } = [];

    /// <summary>
    /// Convenience prefix overrides (e.g., <c>PathWeb</c>, <c>PathWeb.Controllers</c>)
    /// that match every category beneath them.
    /// </summary>
    public List<string> PrefixOverrides { get; set; } = [];

    /// <summary>
    /// Category prefixes that are unconditionally suppressed by <c>DbLoggerProvider</c>
    /// and therefore cannot be enabled via an override.
    /// </summary>
    public List<string> ExcludedPrefixes { get; set; } = [];
}

public class LoggingCategoryGroup
{
    public string GroupName { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
}
