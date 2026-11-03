using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Personas;

/// <summary>
/// Channel conflicts (#2286): two live known players listing the same social channel — the strongest
/// evidence of one person (#2277), nobody else streams on it or posts from it — that no operator
/// marked as different people. Derived from the links rather than recorded, so merging the two,
/// marking them different or removing the link settles one without a write of its own. Keyed in
/// memory with <see cref="PersonaConflictConfidence.ChannelKey"/>, which decodes the URL (one source
/// escapes a handle, another does not) where SQL cannot; the live social links are a few tens of
/// thousands of short rows.
/// </summary>
public sealed class PersonaChannelConflictFinder(TrueMainDbContext db)
{
    public async Task<IReadOnlyList<PersonaChannelConflict>> FindAsync(CancellationToken ct)
    {
        var kinds = PersonaConflictConfidence.SocialLinkKinds.ToList();
        var links = await db.PersonaLinks
            .AsNoTracking()
            .Where(l => kinds.Contains(l.Kind)
                        && l.Persona!.MergedIntoPersonaId == null && l.Persona.RemovedAtUtc == null)
            .Select(l => new { l.PersonaId, l.Kind, l.Url })
            .ToListAsync(ct);

        var channels = links
            .GroupBy(l => (l.Kind, Key: PersonaConflictConfidence.ChannelKey(l.Url)))
            .Select(g => (g.Key.Kind, g.First().Url, PersonaIds: g.Select(l => l.PersonaId).Distinct().ToList()))
            .Where(c => c.PersonaIds.Count > 1)
            .ToList();
        if (channels.Count == 0)
        {
            return [];
        }

        var ids = channels.SelectMany(c => c.PersonaIds).Distinct().ToList();
        var personas = await db.Personas
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Slug, p.DisplayName, p.CreatedAtUtc })
            .ToDictionaryAsync(p => p.Id, ct);
        var distinct = (await db.PersonaDistinctPairs
                .AsNoTracking()
                .Where(d => ids.Contains(d.PersonaId) && ids.Contains(d.OtherPersonaId))
                .Select(d => new { d.PersonaId, d.OtherPersonaId })
                .ToListAsync(ct))
            .Select(d => (d.PersonaId, d.OtherPersonaId))
            .ToHashSet();

        var shared = new Dictionary<(Guid, Guid), List<PersonaSharedChannel>>();
        foreach (var channel in channels)
        {
            for (var i = 0; i < channel.PersonaIds.Count; i++)
            {
                for (var j = i + 1; j < channel.PersonaIds.Count; j++)
                {
                    var pair = PersonaHomonyms.Ordered(channel.PersonaIds[i], channel.PersonaIds[j]);
                    if (distinct.Contains(pair))
                    {
                        continue;
                    }

                    if (!shared.TryGetValue(pair, out var list))
                    {
                        shared[pair] = list = [];
                    }

                    list.Add(new PersonaSharedChannel(channel.Kind, channel.Url));
                }
            }
        }

        return shared
            .Select(entry =>
            {
                var (a, b) = (personas[entry.Key.Item1], personas[entry.Key.Item2]);
                var older = a.CreatedAtUtc != b.CreatedAtUtc
                    ? a.CreatedAtUtc < b.CreatedAtUtc
                    : string.CompareOrdinal(a.Id.ToString("D"), b.Id.ToString("D")) < 0;
                var (first, second) = older ? (a, b) : (b, a);
                return new PersonaChannelConflict(
                    new PersonaChannelSide(first.Id, first.Slug, first.DisplayName, first.CreatedAtUtc),
                    new PersonaChannelSide(second.Id, second.Slug, second.DisplayName, second.CreatedAtUtc),
                    entry.Value.OrderBy(c => c.Kind).ThenBy(c => c.Url, StringComparer.Ordinal).ToList());
            })
            .ToList();
    }
}

/// <summary>Two live known players listing the same social channel(s).</summary>
/// <param name="Older">The one created first: the one a merge keeps by default.</param>
/// <param name="Newer">The other one.</param>
/// <param name="Channels">The channels both list.</param>
public sealed record PersonaChannelConflict(
    PersonaChannelSide Older, PersonaChannelSide Newer, IReadOnlyList<PersonaSharedChannel> Channels)
{
    /// <summary>Homonyms too (<see cref="PersonaHomonyms"/>): one person, merged without the operator.</summary>
    public bool SameName
    {
        get
        {
            var key = PersonaHomonyms.NameKey(Older.DisplayName);
            return key.Length > 0 && key == PersonaHomonyms.NameKey(Newer.DisplayName);
        }
    }

    public bool Involves(Guid personaId) => Older.Id == personaId || Newer.Id == personaId;
}

public sealed record PersonaChannelSide(Guid Id, string Slug, string DisplayName, DateTime CreatedAtUtc);

/// <summary>A channel both personas list, as one of their links spells it.</summary>
public sealed record PersonaSharedChannel(PersonaLinkKind Kind, string Url);
