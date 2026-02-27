using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Options;

public class EasyAuthAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public EasyAuthAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    [Obsolete("ISystemClock is obsolete, use TimeProvider on AuthenticationSchemeOptions instead.")]
    public EasyAuthAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            // Get user info directly from Easy Auth headers
            var nameHeader = Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].FirstOrDefault();
            var idHeader = Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"].FirstOrDefault();

            if (string.IsNullOrEmpty(nameHeader))
            {
                Logger.LogWarning("No X-MS-CLIENT-PRINCIPAL-NAME header found");
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            // Start with email as default
            var displayName = nameHeader;

            // Try to parse the full principal to get the actual name
            var principalHeader = Request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();
            if (!string.IsNullOrEmpty(principalHeader))
            {
                try
                {
                    var principalBytes = Convert.FromBase64String(principalHeader);
                    var principalJson = System.Text.Encoding.UTF8.GetString(principalBytes);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var principal = JsonSerializer.Deserialize<EasyAuthPrincipal>(principalJson, options);

                    if (principal?.Claims != null)
                    {
                        // Look for the "name" claim which contains full name like "Jon Ormond"
                        var nameClaim = principal.Claims.FirstOrDefault(c => c.Type == "name");
                        if (nameClaim != null && !string.IsNullOrEmpty(nameClaim.Value))
                        {
                            // Extract just the first name
                            var fullName = nameClaim.Value;
                            var firstName = fullName.Split(' ')[0];
                            displayName = firstName;
                            Logger.LogInformation("Found full name '{FullName}', using first name '{FirstName}'", fullName, firstName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not parse name from principal, using email");
                }
            }

            // Create claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, displayName),
                new Claim(ClaimTypes.NameIdentifier, idHeader ?? "unknown"),
                new Claim(ClaimTypes.Email, nameHeader)
            };

            var identity = new ClaimsIdentity(claims, "EasyAuth", ClaimTypes.Name, ClaimTypes.Role);
            var claimsPrincipal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(claimsPrincipal, "EasyAuth");

            Logger.LogInformation("Authenticated user: {Name}", displayName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in Easy Auth handler");
            return Task.FromResult(AuthenticateResult.Fail($"Authentication error: {ex.Message}"));
        }
    }

    private class EasyAuthPrincipal
    {
        [System.Text.Json.Serialization.JsonPropertyName("auth_typ")]
        public string? AuthenticationType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name_typ")]
        public string? NameType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("role_typ")]
        public string? RoleType { get; set; }

        public string? UserId { get; set; }
        public string? UserName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("claims")]
        public List<EasyAuthClaim> Claims { get; set; } = new();
    }

    private class EasyAuthClaim
    {
        [System.Text.Json.Serialization.JsonPropertyName("typ")]
        public string Type { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("val")]
        public string Value { get; set; } = string.Empty;
    }
}
