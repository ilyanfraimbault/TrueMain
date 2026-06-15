namespace TrueMain.Options;

/// <summary>
/// Cross-origin policy for the public API, bound from <c>Cors:*</c>. The single
/// <see cref="Origins"/> list drives the <c>FrontendCors</c> policy: the browser
/// receives <c>Access-Control-Allow-Origin</c> only for the hosts listed here.
/// </summary>
/// <remarks>
/// An empty list is a silent-failure trap — the policy still builds, but with no
/// allowed origins the frontend's cross-origin requests are rejected by the
/// browser even though the API itself answers. It reads as "works locally,
/// broken in prod" because Development ships real origins while
/// <c>appsettings.json</c> ships an empty array. Startup therefore validates the
/// list: empty fails the boot in every non-Development environment (see
/// <c>Program.cs</c>) and only logs a warning under Development.
/// </remarks>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Exact origins (scheme + host + port) allowed to make cross-origin browser
    /// requests, e.g. <c>https://truemain.app</c>. Must be non-empty outside
    /// Development.
    /// </summary>
    public string[] Origins { get; set; } = [];
}
