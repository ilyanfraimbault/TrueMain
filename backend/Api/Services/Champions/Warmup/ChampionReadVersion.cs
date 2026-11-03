using Microsoft.Extensions.Options;

namespace TrueMain.Services.Champions.Warmup;

/// <summary>
/// The version the champion read cache answers from once the warm-up runs (#2425).
///
/// <para>
/// <b>Readers stay on the published version.</b> A new aggregation run used to retire every
/// entry the moment the stamp moved, and the next visitor of each slice paid the cold read.
/// <see cref="ChampionCacheWarmer"/> now fills the next version first, on its own requests,
/// and <see cref="Publish"/> moves every reader to it at once. A visitor therefore never waits
/// for a version change: they read the previous one until the new one is complete.
/// </para>
///
/// <para>
/// <see cref="Current"/> is null until a first pass publishes, and always with the warm-up
/// off: <see cref="Scopes.ChampionReadCache"/> then follows the aggregation stamp, as it
/// did before.
/// </para>
/// </summary>
public sealed class ChampionReadVersion(TimeProvider clock, IOptions<ChampionWarmupOptions> options)
{
    // An ambient value because a warm pass goes through the whole request pipeline, and every
    // read behind it asks the same cache: threading a version down through every query service
    // for the one caller that needs it would change all of them.
    private static readonly AsyncLocal<string?> Warming = new();

    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();
    private volatile string? _published;

    /// <summary>
    /// The secret a warm request carries in <see cref="HeaderName"/>, drawn per process: the
    /// header reaches the API through web's proxy like any other, and only the warmer knows this.
    /// </summary>
    public string RequestKey { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The header marking a warm pass's own requests: the key, a colon, the version.</summary>
    public const string HeaderName = "X-Champion-Warm-Pass";

    /// <summary>The version this request reads: its warm pass's, else the published one.</summary>
    public string? Current => Warming.Value ?? _published;

    /// <summary>True on a warm pass's own requests.</summary>
    public bool IsWarming => Warming.Value is not null;

    /// <summary>True once a first pass has completed.</summary>
    public bool HasPublished => _published is not null;

    /// <summary>When this process started waiting for its first pass.</summary>
    public DateTimeOffset StartedAt => _startedAt;

    /// <summary>
    /// How long this request's answer may be cached: a pass's entries have to outlive the wait
    /// for the next pass, which replaces them all, or they would go cold in the middle of the
    /// version. Anything else keeps <paramref name="otherwise"/>.
    /// </summary>
    public TimeSpan EntryLifetime(TimeSpan otherwise)
        => IsWarming ? options.Value.WarmedEntryLifetime : otherwise;

    /// <summary>Puts the current async flow on <paramref name="version"/>, for its duration.</summary>
    public void EnterPass(string version) => Warming.Value = version;

    /// <summary>
    /// Takes the value of <see cref="HeaderName"/>: enters the pass it names if the key matches,
    /// and leaves the request an ordinary one otherwise.
    /// </summary>
    public bool TryEnterPass(string? headerValue)
    {
        if (headerValue is null || !headerValue.StartsWith(RequestKey + ":", StringComparison.Ordinal))
        {
            return false;
        }

        EnterPass(headerValue[(RequestKey.Length + 1)..]);
        return true;
    }

    /// <summary>Moves every reader to <paramref name="version"/>.</summary>
    public void Publish(string version) => _published = version;
}
