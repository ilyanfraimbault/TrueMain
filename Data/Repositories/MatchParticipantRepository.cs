using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchParticipantRepository(TrueMainDbContext db) : IMatchParticipantRepository
{
    public Task<List<MatchParticipant>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchParticipants.Where(p => p.MatchId == matchId).ToListAsync(ct);

    public Task<List<ParticipantRow>> GetRecentParticipantsAsync(string platformId, string puuid, int queueId, int take, CancellationToken ct)
    {
        return (
                from participant in db.MatchParticipants
                join match in db.Matches on participant.MatchId equals match.Id
                where participant.Puuid == puuid &&
                      match.PlatformId == platformId &&
                      match.QueueId == queueId
                orderby match.GameStartTimeUtc descending
                select new ParticipantRow(participant.ChampionId, participant.TeamPosition)
            )
            .Take(Math.Max(1, take))
            .ToListAsync(ct);
    }

    public void AddRange(IEnumerable<MatchParticipant> participants)
        => db.MatchParticipants.AddRange(participants);

    public void AddPerkSelections(IEnumerable<ParticipantPerkSelection> selections)
        => db.ParticipantPerkSelections.AddRange(selections);
}
