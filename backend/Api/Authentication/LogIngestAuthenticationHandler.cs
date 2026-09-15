using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.Authentication;

/// <summary>
/// Authenticates the frontends' log forwarders on <c>POST /internal/logs</c> (#1556)
/// with <see cref="LogIngestAuthenticationDefaults.HeaderName"/>. Same shape as the ops
/// scheme, a different key: holding this one lets a caller write Web/Admin log rows and
/// nothing else.
/// </summary>
public sealed class LogIngestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<LogIngestOptions> ingestOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(LogIngestAuthenticationDefaults.HeaderName, out var providedKey)
            || providedKey.Count != 1
            || string.IsNullOrWhiteSpace(providedKey[0]))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var configured = ingestOptions.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            // Ingestion is off in this environment (no key configured): refuse
            // rather than accept anything, whatever the caller sends.
            return Task.FromResult(AuthenticateResult.Fail("Log ingestion is disabled."));
        }

        if (!ApiKeyComparison.Matches(providedKey[0]!, configured))
        {
            Logger.LogWarning("Log ingest key rejected for {RemoteIp}.", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid log ingest key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "log-ingest")],
            LogIngestAuthenticationDefaults.Scheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            LogIngestAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
