using Data.Entities;

namespace Ingestor.Persistence;

public interface IMatchBanRepository
{
    void AddRange(IEnumerable<MatchBan> bans);
}
