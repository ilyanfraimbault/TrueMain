using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains.Matches;

/// <summary>
/// A player's games past match retention, as match rows (#2358): what a truncated seat keeps
/// and nothing it does not. No CS, kill participation, LP or performance grade, and no detail
/// to open, which <see cref="MatchSummaryReadModel.IsTruncated"/> tells the row. Past the first
/// tier a match only keeps its tracked seats, so its teams can be partial.
/// </summary>
public static class TruncatedMatchSummaries
{
    /// <summary>The only game mode a truncated seat comes from: ranked solo/duo.</summary>
    private const string RankedGameMode = "CLASSIC";

    /// <summary>The rows of <paramref name="seats"/>, in their order, with the other seats of each match.</summary>
    public static async Task<IReadOnlyList<MatchSummaryReadModel>> ReadAsync(
        TrueMainDbContext db,
        IReadOnlyList<TruncatedMatchSeat> seats,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(seats);

        if (seats.Count == 0)
        {
            return [];
        }

        var matchIds = seats.Select(seat => seat.MatchId).Distinct().ToList();
        var everyone = await db.TruncatedMatchSeats
            .AsNoTracking()
            .Where(seat => matchIds.Contains(seat.MatchId))
            .ToListAsync(ct);

        var accountIds = everyone.Where(seat => seat.RiotAccountId != null).Select(seat => seat.RiotAccountId!.Value).Distinct().ToList();
        var accounts = await db.RiotAccounts
            .AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .Select(account => new { account.Id, account.GameName, account.TagLine })
            .ToDictionaryAsync(account => account.Id, account => (account.GameName, account.TagLine), ct);

        var byMatch = everyone.ToLookup(seat => seat.MatchId, StringComparer.Ordinal);
        return [.. seats.Select(seat => ToSummary(seat, byMatch[seat.MatchId], accounts))];
    }

    /// <summary>One match row, from the player's seat and the seats its match kept.</summary>
    public static MatchSummaryReadModel ToSummary(
        TruncatedMatchSeat self,
        IEnumerable<TruncatedMatchSeat> matchSeats,
        IReadOnlyDictionary<Guid, (string GameName, string? TagLine)> accounts)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(matchSeats);
        ArgumentNullException.ThrowIfNull(accounts);

        return new MatchSummaryReadModel
        {
            MatchId = self.MatchId,
            QueueId = self.QueueId,
            GameMode = RankedGameMode,
            GameStartTimeUtc = self.GameStartTimeUtc,
            GameDurationSeconds = self.GameDurationSeconds,
            IsTruncated = true,
            Self = new MatchSummarySelfReadModel
            {
                ChampionId = self.ChampionId,
                Summoner1Id = self.Summoner1Id,
                Summoner2Id = self.Summoner2Id,
                PrimaryStyleId = self.PrimaryStyleId,
                SubStyleId = self.SubStyleId,
                KeystoneId = self.RunePerkIds.Length > 0 ? self.RunePerkIds[0] : 0,
                Kills = self.Kills,
                Deaths = self.Deaths,
                Assists = self.Assists,
                Items = self.BootsItemId > 0 ? [.. self.BuildItemIds, self.BootsItemId] : self.BuildItemIds,
                TeamId = self.TeamId,
                Position = string.IsNullOrEmpty(self.TeamPosition) ? null : self.TeamPosition,
                Win = self.Win,
            },
            Participants = [.. matchSeats
                .OrderBy(seat => seat.ParticipantId)
                .Select(seat =>
                {
                    var known = seat.RiotAccountId is { } id && accounts.TryGetValue(id, out var account) && account.GameName.Length > 0;
                    var identity = known ? accounts[seat.RiotAccountId!.Value] : default;
                    return new MatchSummaryParticipantReadModel
                    {
                        ChampionId = seat.ChampionId,
                        TeamId = seat.TeamId,
                        Position = string.IsNullOrEmpty(seat.TeamPosition) ? null : seat.TeamPosition,
                        GameName = known ? identity.GameName : null,
                        TagLine = known ? identity.TagLine : null,
                    };
                })],
        };
    }
}
