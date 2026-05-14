using Data.Entities;

namespace TrueMain.TestKit.EntityBuilders;

public sealed class RiotAccountBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _platformId = "KR";
    private string _puuid = "puuid-" + Guid.NewGuid().ToString("N")[..8];
    private string _gameName = "TestSummoner";
    private string? _tagLine = "KR1";
    private DateTime _createdAtUtc = DateTime.UtcNow;
    private DateTime _updatedAtUtc = DateTime.UtcNow;
    private DateTime? _lastProfileSyncAtUtc;
    private MatchIngestStatus _matchIngestStatus = MatchIngestStatus.Idle;

    public RiotAccountBuilder WithId(Guid id) { _id = id; return this; }
    public RiotAccountBuilder WithPlatformId(string platformId) { _platformId = platformId; return this; }
    public RiotAccountBuilder WithPuuid(string puuid) { _puuid = puuid; return this; }
    public RiotAccountBuilder WithGameName(string gameName) { _gameName = gameName; return this; }
    public RiotAccountBuilder WithTagLine(string? tagLine) { _tagLine = tagLine; return this; }
    public RiotAccountBuilder WithCreatedAtUtc(DateTime value) { _createdAtUtc = value; return this; }
    public RiotAccountBuilder WithUpdatedAtUtc(DateTime value) { _updatedAtUtc = value; return this; }
    public RiotAccountBuilder WithLastProfileSyncAtUtc(DateTime? value) { _lastProfileSyncAtUtc = value; return this; }
    public RiotAccountBuilder WithMatchIngestStatus(MatchIngestStatus status) { _matchIngestStatus = status; return this; }

    public RiotAccount Build() => new()
    {
        Id = _id,
        PlatformId = _platformId,
        Puuid = _puuid,
        GameName = _gameName,
        TagLine = _tagLine,
        CreatedAtUtc = _createdAtUtc,
        UpdatedAtUtc = _updatedAtUtc,
        LastProfileSyncAtUtc = _lastProfileSyncAtUtc,
        MatchIngestStatus = _matchIngestStatus
    };
}
