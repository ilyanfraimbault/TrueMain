using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Personas;

namespace TrueMain.Services.Personas;

/// <summary>
/// The persona each of a set of tracked accounts belongs to — what the leaderboard row and the
/// profile show in place of a bare Riot ID (#2016). One indexed lookup on
/// <c>persona_accounts."RiotAccountId"</c>; an account with no persona is simply absent.
/// </summary>
internal static class PersonaTags
{
    public static async Task<Dictionary<Guid, PersonaTagReadModel>> FetchAsync(
        TrueMainDbContext ctx,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, PersonaTagReadModel>();
        }

        var rows = await ctx.PersonaAccounts
            .AsNoTracking()
            .Where(link => link.RiotAccountId != null && accountIds.Contains(link.RiotAccountId.Value))
            .Join(
                ctx.Personas.AsNoTracking(),
                link => link.PersonaId,
                persona => persona.Id,
                (link, persona) => new { AccountId = link.RiotAccountId!.Value, persona.Slug, persona.DisplayName })
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => row.AccountId,
            row => new PersonaTagReadModel { Slug = row.Slug, DisplayName = row.DisplayName });
    }
}
