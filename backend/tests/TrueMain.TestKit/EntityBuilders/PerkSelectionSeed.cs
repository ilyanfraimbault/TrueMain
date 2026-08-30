using Data;
using Data.Entities;

namespace TrueMain.TestKit.EntityBuilders;

/// <summary>
/// Seeds one complete rune page (four primary perks + two secondary) for a participant.
/// <para>
/// Shared on purpose: the three suites that cover the consumers of
/// <c>ParticipantBuildFactsLoader</c> only prove "a game means the same build to every
/// consumer" if they seed the *same* page. Three private copies could drift apart and
/// hide exactly the bug the shared loader exists to catch.
/// </para>
/// </summary>
public static class PerkSelectionSeed
{
    private const int PrimaryStyleId = 8000;   // Precision
    private const int SubStyleId = 8100;       // Domination

    private const string PrimaryStyleDescription = "primaryStyle";
    private const string SubStyleDescription = "subStyle";

    /// <summary>A plausible Precision/Domination page: keystone plus three minors, then two shards.</summary>
    private static readonly (string Style, int Index, int PerkId)[] DefaultPage =
    [
        (PrimaryStyleDescription, 0, 8010),
        (PrimaryStyleDescription, 1, 9111),
        (PrimaryStyleDescription, 2, 9104),
        (PrimaryStyleDescription, 3, 8014),
        (SubStyleDescription, 0, 8139),
        (SubStyleDescription, 1, 8135),
    ];

    /// <summary>
    /// Adds the six <see cref="ParticipantPerkSelection"/> rows of the default page for
    /// <paramref name="matchId"/> / <paramref name="participantId"/>, reusing the catalog
    /// entries already tracked or persisted so repeated calls share one catalog row per perk
    /// (the catalog is deduplicated in production too). Saves before returning.
    /// </summary>
    public static async Task SeedRunePageAsync(TrueMainDbContext db, string matchId, int participantId = 1)
    {
        foreach ((string style, int index, int perkId) in DefaultPage)
        {
            int styleId = style == PrimaryStyleDescription ? PrimaryStyleId : SubStyleId;

            PerkSelectionCatalog? catalog = db.PerkSelectionCatalogs.Local
                    .FirstOrDefault(entry =>
                        entry.StyleId == styleId && entry.SelectionIndex == index
                        && entry.PerkId == perkId && entry.StyleDescription == style)
                ?? db.PerkSelectionCatalogs
                    .FirstOrDefault(entry =>
                        entry.StyleId == styleId && entry.SelectionIndex == index
                        && entry.PerkId == perkId && entry.StyleDescription == style);

            if (catalog is null)
            {
                catalog = new PerkSelectionCatalog
                {
                    StyleId = styleId,
                    SelectionIndex = index,
                    PerkId = perkId,
                    StyleDescription = style,
                };
                db.PerkSelectionCatalogs.Add(catalog);
            }

            db.ParticipantPerkSelections.Add(new ParticipantPerkSelection
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                ParticipantId = participantId,
                // Navigation rather than the FK: the catalog id is an identity column and
                // is still 0 for a freshly added row, so EF has to fix it up on save.
                Catalog = catalog,
            });
        }

        await db.SaveChangesAsync();
    }
}
