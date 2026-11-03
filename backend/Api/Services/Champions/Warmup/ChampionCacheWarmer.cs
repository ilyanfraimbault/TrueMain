using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using TrueMain.Services.Champions.Directory;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Warmup;

/// <summary>
/// Fills the champion read cache before anyone asks (#2425): at boot, behind the maintenance
/// page, and again after each aggregation run, behind the version visitors are still reading
/// (see <see cref="ChampionReadVersion"/>).
///
/// <para>
/// <b>It replays the site's own requests over loopback</b> (see
/// <see cref="ChampionWarmCatalog"/>), so what it warms is what is read. Each pass reads the
/// data as it is now under a version of its own, so an entry computed by a pass is never mixed
/// with one computed before it. A request that fails only counts: it leaves that slice to be
/// computed on demand, which is what would have happened without the warm-up.
/// </para>
/// </summary>
public sealed class ChampionCacheWarmer(
    IServiceScopeFactory scopeFactory,
    IServer server,
    IHostApplicationLifetime lifetime,
    IHttpClientFactory httpClientFactory,
    ChampionReadVersion version,
    IOptions<ChampionWarmupOptions> options,
    TimeProvider clock,
    ILogger<ChampionCacheWarmer> logger) : BackgroundService
{
    internal const string ClientName = nameof(ChampionCacheWarmer);

    // A slice can take seconds, and it queues behind the ones the same pass is computing.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    // A pass with more than a tenth of its requests failed is not published.
    private const int MaxFailureShareDivisor = 10;

    private long _passNumber;
    private string? _lastStamp;
    private DateTimeOffset? _lastPassEndedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await WaitForServerAsync(stoppingToken))
        {
            return;
        }

        var poll = TimeSpan.FromSeconds(options.Value.StampPollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stamp = await ReadStampAsync(stoppingToken);
                if (IsPassDue(stamp, _lastStamp, _lastPassEndedAt, clock.GetUtcNow(), options.Value))
                {
                    await RunPassAsync(stamp, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Postgres restarting, typically: readers keep the published version.
                logger.LogError(ex, "Champion warm pass failed; readers keep the published version");
            }

            await Task.Delay(poll, clock, stoppingToken);
        }
    }

    /// <summary>
    /// Whether a pass should start now. The first one always does; after that, a new stamp or
    /// the age limit asks for one, and the minimum interval can only hold it back.
    /// </summary>
    internal static bool IsPassDue(
        string stamp,
        string? lastStamp,
        DateTimeOffset? lastPassEndedAt,
        DateTimeOffset now,
        ChampionWarmupOptions options)
    {
        if (lastPassEndedAt is not { } endedAt)
        {
            return true;
        }

        var age = now - endedAt;
        if (age < TimeSpan.FromSeconds(options.MinPassIntervalSeconds))
        {
            return false;
        }

        return !string.Equals(stamp, lastStamp, StringComparison.Ordinal)
            || age >= TimeSpan.FromMinutes(options.MaxPassAgeMinutes);
    }

    private async Task<bool> WaitForServerAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        await using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
        try
        {
            await started.Task;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<string> ReadStampAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var stamp = await scope.ServiceProvider.GetRequiredService<IChampionAggregationStamp>().GetLatestAsync(ct);
        return stamp is { } aggregatedAt
            ? aggregatedAt.Ticks.ToString(CultureInfo.InvariantCulture)
            : ChampionReadCache.EmptyVersion;
    }

    private async Task RunPassAsync(string stamp, CancellationToken ct)
    {
        var pass = $"{stamp}.{++_passNumber}";
        var sw = Stopwatch.StartNew();

        // The in-process reads below and every request of this pass share one version.
        version.EnterPass(pass);

        using var client = httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = LoopbackAddress();
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.Add(ChampionReadVersion.HeaderName, $"{version.RequestKey}:{pass}");

        var patches = await ReadPatchesAsync(ct);
        var rank = options.Value.DefaultEloBracket;
        var failures = new ConcurrentBag<string>();

        var directory = ChampionWarmCatalog.Directory(patches);
        await RequestAllAsync(client, directory, failures, ct);
        var directoryElapsed = sw.Elapsed;

        var leaderboard = ChampionWarmCatalog.Leaderboard();
        await RequestAllAsync(client, leaderboard, failures, ct);

        // The lines of a page's default rank, and of all ranks for a champion too thin to
        // make that rank's directory but whose page is still linked from everywhere.
        var lines = (await client.GetFromJsonAsync<List<ChampionLineDto>>($"/champions?eloBracket={rank}", ct) ?? [])
            .Concat(await client.GetFromJsonAsync<List<ChampionLineDto>>("/champions", ct) ?? [])
            .Select(line => new ChampionWarmLine(line.ChampionId, line.Position))
            .Distinct()
            .ToList();
        var pages = patches.Count == 0
            ? []
            : ChampionWarmCatalog.ChampionPages(lines, patches[0], rank);
        await RequestAllAsync(client, pages, failures, ct);

        var total = directory.Count + leaderboard.Count + pages.Count;
        if (!IsWorthPublishing(failures.Count, total))
        {
            // Postgres went away, typically. Publishing would move every reader to a version
            // nothing is cached under, where keeping them put costs them nothing. A site that
            // has never published retries at the next poll; otherwise the interval applies.
            _lastPassEndedAt = version.HasPublished ? clock.GetUtcNow() : null;
            logger.LogError(
                "Champion warm pass {Pass} not published: {Failures} of {Total} requests failed (first: {FirstFailure})",
                pass, failures.Count, total, failures.FirstOrDefault());
            return;
        }

        version.Publish(pass);
        _lastStamp = stamp;
        _lastPassEndedAt = clock.GetUtcNow();

        logger.LogInformation(
            "Champion warm pass {Pass} published: {DirectoryRequests} directory requests in {DirectorySeconds}s, "
            + "{PageRequests} champion page requests over {Lines} lines, {Failures} failed (first: {FirstFailure}), "
            + "{TotalSeconds}s in all",
            pass, directory.Count, (long)directoryElapsed.TotalSeconds, pages.Count, lines.Count,
            failures.Count, failures.FirstOrDefault(), (long)sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Whether a pass filled enough to move readers onto it. A failed request only leaves that
    /// slice to be read on demand, so a few are not worth holding a version back for.
    /// </summary>
    internal static bool IsWorthPublishing(int failures, int total)
        => failures <= total / MaxFailureShareDivisor;

    private async Task<IReadOnlyList<string>> ReadPatchesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var summaries = scope.ServiceProvider.GetRequiredService<IChampionSummariesQueryService>();
        var patches = await summaries.GetPatchesNewestFirstAsync(ct);
        return [.. patches.Take(options.Value.PatchDepth)];
    }

    private async Task RequestAllAsync(
        HttpClient client, IReadOnlyList<string> urls, ConcurrentBag<string> failures, CancellationToken ct)
    {
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Value.Concurrency,
            CancellationToken = ct,
        };
        await Parallel.ForEachAsync(urls, parallel, async (url, token) =>
        {
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, token);

                // A 404 is an answer, and is cached like one: a lane with no data on that rank.
                if ((int)response.StatusCode >= 500)
                {
                    failures.Add($"{(int)response.StatusCode} {url}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
            {
                failures.Add($"{ex.GetType().Name} {url}");
            }
        });
    }

    private Uri LoopbackAddress()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(entry => entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The API listens on no plain HTTP address to warm itself through.");

        // The configured form may be a wildcard ("http://+:8080"), which is no host to dial.
        var port = address[(address.LastIndexOf(':') + 1)..].TrimEnd('/');
        return new Uri($"http://127.0.0.1:{port}");
    }

    private sealed record ChampionLineDto(int ChampionId, string Position);
}
