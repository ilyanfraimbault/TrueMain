namespace Data.Repositories;

/// <summary>One participant of an ingested match: this account played this champion at this time (#1475).</summary>
public sealed record MatchActivityObservation(string Puuid, int ChampionId, DateTime PlayedAtUtc);

/// <summary>What <see cref="IMainChampionStatRepository.RecordMatchActivityAsync"/> changed.</summary>
public sealed record MatchActivityRecordResult(int AccountsStamped, int MainsReactivated);
