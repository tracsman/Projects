using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;

namespace PathWeb.Services;

/// <summary>
/// Custom logger provider that writes log entries to the AppLog table in SQL.
/// Buffers entries and flushes in batches to avoid a DB round-trip per log line.
/// </summary>
public sealed class DbLoggerProvider : ILoggerProvider, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentQueue<AppLog> _queue = new();
    private readonly Timer _flushTimer;
    private readonly HashSet<string> _excludedPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Hosting",
        "Microsoft.Extensions",
        "System.Net.Http"
    ];

    public DbLoggerProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public ILogger CreateLogger(string categoryName) => new DbLogger(this, categoryName);

    public void Enqueue(AppLog entry) => _queue.Enqueue(entry);

    public bool IsExcluded(string categoryName) =>
        _excludedPrefixes.Any(p => categoryName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

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

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Information && !_provider.IsExcluded(_category);

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
            Message = message
        });
    }
}
