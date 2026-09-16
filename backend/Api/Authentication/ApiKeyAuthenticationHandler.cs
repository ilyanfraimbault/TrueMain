using System.Security.Claims;
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
        if (string.IsNullOrEmpty(configured))
        {
            // Defence-in-depth: Ops:ApiKey is validated at startup
            // (ValidateDataAnnotations + ValidateOnStart), so an empty key
            // normally fails the host boot. Guard here too so the handler can
            // never authenticate against an empty configured key if that
            // startup validation is ever weakened or bypassed.
            Logger.LogError(
                "Ops API key is not configured; rejecting request from {RemoteIp}.",
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Ops API key is not configured."));
        }

        if (!ApiKeyComparison.Matches(providedApiKey[0]!, configured))
        {
            Logger.LogWarning("Ops API key rejected for {RemoteIp}.", Context.Connection.RemoteIpAddress);
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
