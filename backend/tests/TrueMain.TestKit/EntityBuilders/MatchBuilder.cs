using Core.Lol.Map;
using Data.Entities;

namespace TrueMain.TestKit.EntityBuilders;

/// <summary>
/// Fluent <see cref="Match"/> builder with sane defaults (Ranked Solo Duo,
/// Summoner's Rift, start = <c>DateTime.UtcNow - 1 hour</c>). Override only
/// the fields that matter to the assertion.
/// </summary>
public sealed class MatchBuilder
{
    private string _id = "MATCH_" + Guid.NewGuid().ToString("N")[..8];
    private string _platformId = "KR";
    private int _queueId = (int)LolQueueId.RankedSoloDuo;
    private int _mapId = (int)LolMapId.SummonersRift;
    private string _gameMode = "CLASSIC";
    private string _gameType = "MATCHED_GAME";
    private DateTime _gameStartTimeUtc = DateTime.UtcNow.AddHours(-1);
    private int _gameDurationSeconds = 1_800;
    private string _gameVersion = "16.4.521.123";
    private DateTime _createdAtUtc = DateTime.UtcNow;
    private bool _timelineIngested;

    public MatchBuilder WithId(string id) { _id = id; return this; }
    public MatchBuilder WithPlatformId(string platformId) { _platformId = platformId; return this; }
    public MatchBuilder WithQueueId(int queueId) { _queueId = queueId; return this; }
    public MatchBuilder WithMapId(int mapId) { _mapId = mapId; return this; }
    public MatchBuilder WithGameMode(string gameMode) { _gameMode = gameMode; return this; }
    public MatchBuilder WithGameType(string gameType) { _gameType = gameType; return this; }
    public MatchBuilder WithGameStartTimeUtc(DateTime value) { _gameStartTimeUtc = value; return this; }
    public MatchBuilder WithGameDurationSeconds(int seconds) { _gameDurationSeconds = seconds; return this; }
    public MatchBuilder WithGameVersion(string gameVersion) { _gameVersion = gameVersion; return this; }
    public MatchBuilder WithCreatedAtUtc(DateTime value) { _createdAtUtc = value; return this; }
    public MatchBuilder WithTimelineIngested(bool ingested = true) { _timelineIngested = ingested; return this; }

    public Match Build() => new()
    {
        Id = _id,
        PlatformId = _platformId,
        QueueId = _queueId,
        MapId = _mapId,
        GameMode = _gameMode,
        GameType = _gameType,
        GameStartTimeUtc = _gameStartTimeUtc,
        GameDurationSeconds = _gameDurationSeconds,
        GameVersion = _gameVersion,
        CreatedAtUtc = _createdAtUtc,
        TimelineIngested = _timelineIngested
    };
}
