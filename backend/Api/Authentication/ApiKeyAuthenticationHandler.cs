using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.Authentication;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<OpsOptions> opsOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var providedApiKey)
            || providedApiKey.Count != 1
            || string.IsNullOrWhiteSpace(providedApiKey[0]))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var configured = opsOptions.CurrentValue.ApiKey;
        // Hash both sides so FixedTimeEquals gets same-length spans even when
        // the provided key differs in length from the configured one —
        // otherwise the early-out on span length re-opens the timing channel.
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedApiKey[0]!));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        if (!CryptographicOperations.FixedTimeEquals(providedHash, configuredHash))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "ops")],
            ApiKeyAuthenticationDefaults.Scheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            ApiKeyAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
