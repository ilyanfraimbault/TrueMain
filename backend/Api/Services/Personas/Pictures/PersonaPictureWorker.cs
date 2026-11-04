using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>
/// Keeps known players' pictures current (#2446): one person at a time, at a request rate the
/// platforms never notice, only those with a tracked account (the others have no page to show it on).
/// A person falls due when never read, when their channels change, and every
/// <see cref="RefreshInterval"/>. Stateless between persons, so a restart costs nothing.
/// </summary>
public sealed class PersonaPictureWorker : BackgroundService
{
    /// <summary>Under the 30 days the YouTube API policy allows its data to be kept without a refresh.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(25);

    /// <summary>How soon a person is read again after a channel that outranks their picture could not be.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromDays(1);

    private const int BatchSize = 50;

    private static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultPersonDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersonaPictureResolver _resolver;
    private readonly IOptions<PersonaPictureOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PersonaPictureWorker> _logger;
    private readonly TimeSpan _startupDelay;
    private readonly TimeSpan _personDelay;

    public PersonaPictureWorker(
        IServiceScopeFactory scopeFactory,
        PersonaPictureResolver resolver,
        IOptions<PersonaPictureOptions> options,
        TimeProvider timeProvider,
        ILogger<PersonaPictureWorker> logger)
        : this(scopeFactory, resolver, options, timeProvider, logger, DefaultStartupDelay, DefaultPersonDelay)
    {
    }

    internal PersonaPictureWorker(
        IServiceScopeFactory scopeFactory,
        PersonaPictureResolver resolver,
        IOptions<PersonaPictureOptions> options,
        TimeProvider timeProvider,
        ILogger<PersonaPictureWorker> logger,
        TimeSpan startupDelay,
        TimeSpan personDelay)
    {
        _scopeFactory = scopeFactory;
        _resolver = resolver;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _startupDelay = startupDelay;
        _personDelay = personDelay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Known players' pictures are off: the YouTube and Twitch credentials are not set.");
            return;
        }

        try
        {
            await Task.Delay(_startupDelay, _timeProvider, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var read = await RunPassAsync(stoppingToken);
                if (read == 0)
                {
                    await Task.Delay(IdleDelay, _timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping: the next start carries on with whoever is still due.
        }
    }

    /// <summary>Reads one batch of the people due; how many it read. A database failure counts as none: the loop waits, then retries.</summary>
    internal async Task<int> RunPassAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TrueMainDbContext>();
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var due = await db.Personas
                .AsNoTracking()
                .Where(p => p.MergedIntoPersonaId == null && p.RemovedAtUtc == null)
                .Where(p => p.Accounts.Any(a => a.RiotAccountId != null))
                .Where(p => p.PictureRefreshAtUtc == null || p.PictureRefreshAtUtc <= now)
                .OrderBy(p => p.PictureRefreshAtUtc == null ? 0 : 1)
                .ThenBy(p => p.PictureRefreshAtUtc)
                .Take(BatchSize)
                .Select(p => new { p.Id, p.PictureRefreshAtUtc })
                .ToListAsync(ct);

            foreach (var person in due)
            {
                await ReadPersonAsync(db, person.Id, person.PictureRefreshAtUtc, ct);
                await Task.Delay(_personDelay, _timeProvider, ct);
            }

            return due.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "A pass over known players' pictures failed; the next one retries.");
            return 0;
        }
    }

    private async Task ReadPersonAsync(TrueMainDbContext db, Guid id, DateTime? seenRefreshAt, CancellationToken ct)
    {
        // The page's own order within a kind: newest first, and a channel's `@handle` form first within one batch.
        var channels = await db.PersonaLinks
            .AsNoTracking()
            .Where(link => link.PersonaId == id && PersonaPictureResolver.Priority.Contains(link.Kind))
            .OrderByDescending(link => link.CreatedAtUtc)
            .ThenByDescending(link => link.Url.Contains("/@"))
            .ThenBy(link => link.Url)
            .Select(link => new PersonaChannel(link.Kind, link.Url))
            .ToListAsync(ct);

        var resolution = await _resolver.ResolveAsync(channels, ct);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var refreshAt = now + (resolution.Unsettled ? RetryInterval : RefreshInterval);

        // Only if nobody touched the person meanwhile: a channel added during the reads must not have
        // its refresh erased, and a person removed meanwhile keeps no picture.
        var update = db.Personas.Where(p =>
            p.Id == id && p.PictureRefreshAtUtc == seenRefreshAt && p.MergedIntoPersonaId == null && p.RemovedAtUtc == null);

        // No answer for the channels that outrank the current picture and nothing better: the picture
        // stays. Not `UpdatedAtUtc`: it is the sitemap's lastmod, and a crawler does not read this image.
        if (resolution is { Url: null, Unsettled: true })
        {
            await update.ExecuteUpdateAsync(set => set.SetProperty(p => p.PictureRefreshAtUtc, refreshAt), ct);
            return;
        }

        await update.ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.PictureUrl, resolution.Url)
                .SetProperty(p => p.PictureRefreshAtUtc, refreshAt),
            ct);
    }
}
