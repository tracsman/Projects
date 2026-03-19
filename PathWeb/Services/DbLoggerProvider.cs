using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using PathWeb.Data;
using PathWeb.Models;

namespace PathWeb.Services;

/// <summary>
/// Custom logger provider that writes log entries to the AppLog table in SQL.
/// Buffers entries and flushes in batches to avoid a DB round-trip per log line.
/// </summary>
public sealed class DbLoggerProvider : ILoggerProvider, IDisposable
{
    private static readonly TimeSpan SettingsRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ConcurrentQueue<AppLog> _queue = new();
    private readonly Timer _flushTimer;
    private readonly object _settingsLock = new();
    private readonly HashSet<string> _excludedPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Hosting",
        "Microsoft.Extensions",
        "System.Net.Http"
    ];
    private LoggingSettingsSnapshot _loggingSettings = new();
    private DateTime _loggingSettingsLoadedUtc = DateTime.MinValue;

    public DbLoggerProvider(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public ILogger CreateLogger(string categoryName) => new DbLogger(this, categoryName);

    public void Enqueue(AppLog entry) => _queue.Enqueue(entry);

    public string? GetCurrentUserName()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var signedInUser = user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.Identity?.Name
                ?? user.FindFirst(ClaimTypes.Upn)?.Value
                ?? user.FindFirst("preferred_username")?.Value;

            if (!string.IsNullOrWhiteSpace(signedInUser))
                return TrimToLength(signedInUser, 128);
        }

        if (OperatingSystem.IsWindows())
        {
            var processIdentity = WindowsIdentity.GetCurrent()?.Name;
            if (!string.IsNullOrWhiteSpace(processIdentity))
                return TrimToLength(processIdentity, 128);
        }

        var runningOnManagedIdentity =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSI_ENDPOINT"));

        if (runningOnManagedIdentity)
        {
            var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "AppService";
            return TrimToLength($"MI:{siteName}", 128);
        }

        var environmentUser = Environment.UserName;
        return string.IsNullOrWhiteSpace(environmentUser) ? null : TrimToLength(environmentUser, 128);
    }

    public bool IsExcluded(string categoryName) =>
        _excludedPrefixes.Any(p => categoryName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public bool IsEnabled(string categoryName, LogLevel logLevel)
    {
        if (logLevel == LogLevel.None || IsExcluded(categoryName))
            return false;

        var settings = GetLoggingSettings();
        var minimumLevel = ResolveMinimumLevel(settings, categoryName);
        return minimumLevel != LogLevel.None && logLevel >= minimumLevel;
    }

    private static string TrimToLength(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    private LoggingSettingsSnapshot GetLoggingSettings()
    {
        if (DateTime.UtcNow - _loggingSettingsLoadedUtc < SettingsRefreshInterval)
            return _loggingSettings;

        lock (_settingsLock)
        {
            if (DateTime.UtcNow - _loggingSettingsLoadedUtc < SettingsRefreshInterval)
                return _loggingSettings;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                _loggingSettings = settingsService.GetLoggingSettingsAsync().GetAwaiter().GetResult();
                _loggingSettingsLoadedUtc = DateTime.UtcNow;
            }
            catch
            {
                if (_loggingSettingsLoadedUtc == DateTime.MinValue)
                {
                    _loggingSettings = new LoggingSettingsSnapshot();
                    _loggingSettingsLoadedUtc = DateTime.UtcNow;
                }
            }
        }

        return _loggingSettings;
    }

    private static LogLevel ResolveMinimumLevel(LoggingSettingsSnapshot settings, string categoryName)
    {
        var candidate = categoryName;
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (settings.CategoryLevels.TryGetValue(candidate, out var configuredLevel) &&
                Enum.TryParse<LogLevel>(configuredLevel, true, out var parsedLevel))
            {
                return parsedLevel;
            }

            var lastDot = candidate.LastIndexOf('.');
            if (lastDot < 0)
                break;

            candidate = candidate[..lastDot];
        }

        return Enum.TryParse<LogLevel>(settings.DefaultLevel, true, out var defaultLevel)
            ? defaultLevel
            : LogLevel.Information;
    }

    private void Flush()
    {
        if (_queue.IsEmpty) return;

        var batch = new List<AppLog>();
        while (_queue.TryDequeue(out var entry))
        {
            batch.Add(entry);
            if (batch.Count >= 100) break;
        }

        if (batch.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LabConfigContext>();
            context.AppLogs.AddRange(batch);
            context.SaveChanges();
        }
        catch
        {
            // Swallow DB errors to avoid crashing the app if logging fails
        }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
    }
}

/// <summary>
/// Individual logger instance created per category. Enqueues entries to the provider's batch queue.
/// </summary>
public sealed class DbLogger : ILogger
{
    private readonly DbLoggerProvider _provider;
    private readonly string _category;

    public DbLogger(DbLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(_category, logLevel);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception != null)
            message += $"\n{exception}";

        _provider.Enqueue(new AppLog
        {
            Timestamp = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Category = _category.Length > 256 ? _category[..256] : _category,
            Message = message,
            UserName = _provider.GetCurrentUserName()
        });
    }
}
