namespace Data.Repositories;

/// <summary>The profile icon and summoner level one participant of an ingested match had when it started (#2002).</summary>
public sealed record MatchSummonerObservation(string Puuid, int ProfileIconId, int SummonerLevel, DateTime PlayedAtUtc);
