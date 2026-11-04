using Data.Entities;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>A channel of a known player: what it is and where.</summary>
public sealed record PersonaChannel(PersonaLinkKind Kind, string Url);

/// <summary>
/// What a known player's channels gave. <see cref="Unsettled"/> says a channel ranked above the
/// answer could not be read: the answer holds for now, and the worker asks again sooner.
/// </summary>
public sealed record PersonaPictureResolution(string? Url, bool Unsettled);

/// <summary>
/// Picks a known player's picture (#2446): the first channel with a picture, in
/// <see cref="Priority"/> order. X, TikTok and Instagram have no official access to read one with;
/// wikis and stat sites are not the person's own.
/// </summary>
public sealed class PersonaPictureResolver(IEnumerable<IPersonaPictureReader> readers)
{
    public static readonly IReadOnlyList<PersonaLinkKind> Priority =
    [
        PersonaLinkKind.YouTube,
        PersonaLinkKind.Twitch,
        PersonaLinkKind.Kick,
        PersonaLinkKind.Soop,
        PersonaLinkKind.Chzzk,
    ];

    private readonly Dictionary<PersonaLinkKind, IPersonaPictureReader> _readers = readers.ToDictionary(reader => reader.Kind);

    /// <summary>The picture of the first channel that has one, walking the kinds in priority order.</summary>
    /// <param name="channels">The person's links, the preferred one first within a kind.</param>
    /// <param name="ct">Stops the walk.</param>
    public async Task<PersonaPictureResolution> ResolveAsync(IReadOnlyList<PersonaChannel> channels, CancellationToken ct)
    {
        var unsettled = false;
        foreach (var kind in Priority)
        {
            foreach (var channel in channels.Where(channel => channel.Kind == kind))
            {
                var lookup = await _readers[kind].ReadAsync(channel.Url, ct);
                if (lookup.Outcome == PersonaPictureOutcome.Found)
                {
                    return new PersonaPictureResolution(lookup.Url, unsettled);
                }

                unsettled |= lookup.Outcome == PersonaPictureOutcome.Unavailable;
            }
        }

        return new PersonaPictureResolution(null, unsettled);
    }
}
