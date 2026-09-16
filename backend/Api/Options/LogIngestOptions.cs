namespace TrueMain.Options;

/// <summary>
/// The key the frontends present to <c>POST /internal/logs</c> (#1556), bound from
/// <c>LogIngest:*</c>.
/// </summary>
/// <remarks>
/// <para>
/// A key of its own rather than the ops key: the public site's server is the most
/// exposed process in the stack, and what it needs is to <em>write</em> its own
/// errors, not to read or operate anything behind <c>/ops</c>.
/// </para>
/// <para>
/// Optional on purpose. Unset turns ingestion off — every call is refused and the
/// frontends' forwarders stay silent — so a deploy whose environment has not been
/// given a key yet boots and serves as before instead of failing on a logging
/// feature. When it is set, it must be long enough to be a secret.
/// </para>
/// </remarks>
public sealed class LogIngestOptions
{
    public const string SectionName = "LogIngest";

    public const int MinimumKeyLength = 32;

    public string? ApiKey { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);
}
