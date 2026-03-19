using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PathWeb.Data;

namespace PathWeb.Services;

public class SettingsService
{
    private const string AutoDeleteRunbookSettingName = "AutoDeleteRunbook";
    private const string AutoDeleteRunbooksCacheKey = "Settings:AutoDeleteRunbook";
    private const string AutomationRunbookTypeSettingName = "AutomationRunbookType";
    private const string AutomationRunbookTypeCacheKey = "Settings:AutomationRunbookType";
    private const string DefaultAutomationRunbookType = "PowerShell72";
    private const string LoggingDefaultSettingName = "Logging:Default";
    private const string LoggingSettingsCacheKey = "Settings:Logging";
    private const string LoggingPrefix = "Logging:";
    private const string DefaultLoggingLevel = "Information";

    private readonly LabConfigContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(LabConfigContext context, IMemoryCache cache, ILogger<SettingsService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> GetAutoDeleteRunbooksAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue<bool>(AutoDeleteRunbooksCacheKey, out var cachedValue))
            return cachedValue;

        var setting = await _context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingName == AutoDeleteRunbookSettingName, cancellationToken);

        var enabled = ParseBool(setting?.ProdVersion, defaultValue: true);
        _cache.Set(AutoDeleteRunbooksCacheKey, enabled, TimeSpan.FromMinutes(5));
        return enabled;
    }

    public async Task SaveAutoDeleteRunbooksAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var storedValue = enabled ? "true" : "false";
        await SaveSettingAsync(AutoDeleteRunbookSettingName, storedValue, cancellationToken);

        _cache.Set(AutoDeleteRunbooksCacheKey, enabled, TimeSpan.FromMinutes(5));
        _logger.LogInformation("Setting {SettingName} saved as {SettingValue}", AutoDeleteRunbookSettingName, storedValue);
    }

    public async Task<string> GetAutomationRunbookTypeAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue<string>(AutomationRunbookTypeCacheKey, out var cachedValue) && !string.IsNullOrWhiteSpace(cachedValue))
            return cachedValue;

        var setting = await _context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingName == AutomationRunbookTypeSettingName, cancellationToken);

        var runbookType = string.IsNullOrWhiteSpace(setting?.ProdVersion)
            ? DefaultAutomationRunbookType
            : setting.ProdVersion.Trim();

        _cache.Set(AutomationRunbookTypeCacheKey, runbookType, TimeSpan.FromMinutes(5));
        return runbookType;
    }

    public async Task SaveAutomationRunbookTypeAsync(string runbookType, CancellationToken cancellationToken = default)
    {
        var storedValue = string.IsNullOrWhiteSpace(runbookType)
            ? DefaultAutomationRunbookType
            : runbookType.Trim();

        await SaveSettingAsync(AutomationRunbookTypeSettingName, storedValue, cancellationToken);

        _cache.Set(AutomationRunbookTypeCacheKey, storedValue, TimeSpan.FromMinutes(5));
        _logger.LogInformation("Setting {SettingName} saved as {SettingValue}", AutomationRunbookTypeSettingName, storedValue);
    }

    public async Task<LoggingSettingsSnapshot> GetLoggingSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue<LoggingSettingsSnapshot>(LoggingSettingsCacheKey, out var cachedValue) && cachedValue != null)
            return cachedValue;

        var settings = await _context.Settings
            .AsNoTracking()
            .Where(s => s.SettingName != null && s.SettingName.StartsWith(LoggingPrefix))
            .ToListAsync(cancellationToken);

        var snapshot = new LoggingSettingsSnapshot
        {
            DefaultLevel = NormalizeLogLevel(settings.FirstOrDefault(s => s.SettingName == LoggingDefaultSettingName)?.ProdVersion, DefaultLoggingLevel),
            CategoryLevels = settings
                .Where(s => !string.Equals(s.SettingName, LoggingDefaultSettingName, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(s.SettingName) &&
                            !string.IsNullOrWhiteSpace(s.ProdVersion))
                .ToDictionary(
                    s => s.SettingName![LoggingPrefix.Length..],
                    s => NormalizeLogLevel(s.ProdVersion, DefaultLoggingLevel),
                    StringComparer.OrdinalIgnoreCase)
        };

        _cache.Set(LoggingSettingsCacheKey, snapshot, TimeSpan.FromMinutes(1));
        return snapshot;
    }

    public async Task SaveLoggingSettingsAsync(string defaultLevel, IEnumerable<KeyValuePair<string, string>> overrides, CancellationToken cancellationToken = default)
    {
        var normalizedDefault = NormalizeLogLevel(defaultLevel, DefaultLoggingLevel);
        var normalizedOverrides = overrides
            .Where(o => !string.IsNullOrWhiteSpace(o.Key))
            .GroupBy(o => o.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => NormalizeLogLevel(g.Last().Value, normalizedDefault),
                StringComparer.OrdinalIgnoreCase);

        await SaveSettingAsync(LoggingDefaultSettingName, normalizedDefault, cancellationToken);

        var existingOverrideNames = await _context.Settings
            .AsNoTracking()
            .Where(s => s.SettingName != null && s.SettingName.StartsWith(LoggingPrefix) && s.SettingName != LoggingDefaultSettingName)
            .Select(s => s.SettingName!)
            .ToListAsync(cancellationToken);

        foreach (var existingName in existingOverrideNames.Where(name => !normalizedOverrides.ContainsKey(name[LoggingPrefix.Length..])))
            await DeleteSettingAsync(existingName, cancellationToken);

        foreach (var overrideEntry in normalizedOverrides)
            await SaveSettingAsync($"{LoggingPrefix}{overrideEntry.Key}", overrideEntry.Value, cancellationToken);

        var snapshot = new LoggingSettingsSnapshot
        {
            DefaultLevel = normalizedDefault,
            CategoryLevels = new Dictionary<string, string>(normalizedOverrides, StringComparer.OrdinalIgnoreCase)
        };

        _cache.Set(LoggingSettingsCacheKey, snapshot, TimeSpan.FromMinutes(1));
        _logger.LogInformation("Logging settings saved. Default={DefaultLevel}, Overrides={OverrideCount}", normalizedDefault, normalizedOverrides.Count);
    }

    private async Task SaveSettingAsync(string settingName, string value, CancellationToken cancellationToken)
    {
        var exists = await _context.Settings
            .AsNoTracking()
            .AnyAsync(s => s.SettingName == settingName, cancellationToken);

        if (exists)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE Settings
SET ProdVersion = {value}
WHERE SettingName = {settingName}", cancellationToken);
        }
        else
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO Settings (SettingName, ProdVersion, BetaVersion)
VALUES ({settingName}, {value}, NULL)", cancellationToken);
        }
    }

    private async Task DeleteSettingAsync(string settingName, CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM Settings
WHERE SettingName = {settingName}", cancellationToken);
    }

    private static string NormalizeLogLevel(string? value, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<LogLevel>(value.Trim(), true, out var parsed))
            return parsed.ToString();

        return fallback;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (bool.TryParse(value, out var parsed))
            return parsed;

        return defaultValue;
    }
}
