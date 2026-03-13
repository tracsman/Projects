using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PathWeb.Data;

namespace PathWeb.Services;

/// <summary>
/// Service that looks up a user's AuthLevel from the Users table in the database.
/// Results are cached for 5 minutes to avoid a DB query on every request.
/// </summary>
public class AuthLevelService
{
    private readonly LabConfigContext _context;
    private readonly ILogger<AuthLevelService> _logger;
    private readonly IMemoryCache _cache;

    public AuthLevelService(LabConfigContext context, ILogger<AuthLevelService> logger, IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Gets the AuthLevel for a given user email/username.
    /// Returns AuthLevels.Reject (0) if user is not found.
    /// </summary>
    public async Task<byte> GetAuthLevelAsync(string? userName)
    {
        if (string.IsNullOrEmpty(userName))
        {
            _logger.LogWarning("GetAuthLevelAsync called with null/empty userName");
            return (byte)AuthLevels.Reject;
        }

        var cacheKey = $"AuthLevel:{userName.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out byte cachedLevel))
            return cachedLevel;

        try
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserName == userName);

            if (user == null)
            {
                _logger.LogWarning("User '{UserName}' not found in database, defaulting to AuthLevel 0 (Reject)", userName);
                return (byte)AuthLevels.Reject;
            }

            _logger.LogDebug("User '{UserName}' has AuthLevel {AuthLevel}", userName, user.AuthLevel);
            _cache.Set(cacheKey, user.AuthLevel, TimeSpan.FromMinutes(5));
            return user.AuthLevel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up AuthLevel for '{UserName}', defaulting to 0", userName);
            return (byte)AuthLevels.Reject;
        }
    }

    /// <summary>
    /// Checks if the user meets the required authorization level.
    /// </summary>
    public async Task<bool> IsAuthorizedAsync(string? userName, AuthLevels requiredLevel)
    {
        var authLevel = await GetAuthLevelAsync(userName);
        return authLevel >= (byte)requiredLevel;
    }
}
