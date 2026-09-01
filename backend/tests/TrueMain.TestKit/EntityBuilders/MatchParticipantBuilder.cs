using Data.Entities;

namespace TrueMain.TestKit.EntityBuilders;

/// <summary>
/// Fluent <see cref="MatchParticipant"/> builder with sane defaults for every
/// non-nullable column (a mid-lane winner on a full item set), so a test only
/// states the fields its assertion depends on.
/// <para>
/// It exists so that adding a mandatory column to <c>match_participants</c> breaks
/// one file instead of the twenty that used to hand-roll the same ~25 assignments.
/// Do not re-declare a local <c>BuildParticipant</c>: extend this builder instead.
/// </para>
/// </summary>
public sealed class MatchParticipantBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _matchId = string.Empty;
    private int _participantId = 1;
    private string _puuid = "puuid-" + Guid.NewGuid().ToString("N")[..8];
    private string? _summonerName;
    private Guid? _riotAccountId;
    private int _summonerLevel = 100;
    private int _championId = 1;
    private int _teamId = 100;
    private string _teamPosition = "MIDDLE";
    private string _individualPosition = "MIDDLE";
    private string _lane = "MIDDLE";
    private string _role = "SOLO";
    private string _eloBracket = string.Empty;
    private bool _win = true;
    private int _kills = 1;
    private int _deaths = 1;
    private int _assists = 1;
    private int _totalDamageDealtToChampions;
    private int _visionScore;
    private int _goldEarned = 10_000;
    private int _totalMinionsKilled = 100;
    private int _neutralMinionsKilled;
    private int _champLevel = 14;
    private int _item0 = 6672;
    private int _item1 = 3006;
    private int _item2;
    private int _item3;
    private int _item4;
    private int _item5;
    private int _item6 = 3363;
    private int _trinketItemId = 3363;
    private int _perksDefense = 5002;
    private int _perksFlex = 5008;
    private int _perksOffense = 5005;
    private int _primaryStyleId = 8000;
    private int _subStyleId = 8200;
    private int _summoner1Id = 4;
    private int _summoner2Id = 7;
    private List<ItemEvent> _itemEvents = [];
    private List<SkillEvent> _skillEvents = [];

    public MatchParticipantBuilder WithId(Guid id) { _id = id; return this; }
    public MatchParticipantBuilder WithMatchId(string matchId) { _matchId = matchId; return this; }
    public MatchParticipantBuilder WithParticipantId(int participantId) { _participantId = participantId; return this; }
    public MatchParticipantBuilder WithPuuid(string puuid) { _puuid = puuid; return this; }
    public MatchParticipantBuilder WithSummonerName(string summonerName) { _summonerName = summonerName; return this; }
    public MatchParticipantBuilder WithRiotAccountId(Guid? riotAccountId) { _riotAccountId = riotAccountId; return this; }
    public MatchParticipantBuilder WithSummonerLevel(int level) { _summonerLevel = level; return this; }
    public MatchParticipantBuilder WithChampionId(int championId) { _championId = championId; return this; }
    public MatchParticipantBuilder WithTeamId(int teamId) { _teamId = teamId; return this; }

    /// <summary>
    /// Sets <see cref="MatchParticipant.TeamPosition"/>, <see cref="MatchParticipant.IndividualPosition"/>
    /// and <see cref="MatchParticipant.Lane"/> to the same value — the shape Riot returns for a
    /// game that went as drafted, and what almost every test means by "this row is a mid laner".
    /// Use the single-field overrides to build a row where the three disagree.
    /// </summary>
    public MatchParticipantBuilder WithPosition(string position)
    {
        _teamPosition = position;
        _individualPosition = position;
        _lane = position;
        return this;
    }

    public MatchParticipantBuilder WithTeamPosition(string teamPosition) { _teamPosition = teamPosition; return this; }
    public MatchParticipantBuilder WithIndividualPosition(string position) { _individualPosition = position; return this; }
    public MatchParticipantBuilder WithLane(string lane) { _lane = lane; return this; }
    public MatchParticipantBuilder WithRole(string role) { _role = role; return this; }
    public MatchParticipantBuilder WithEloBracket(string eloBracket) { _eloBracket = eloBracket; return this; }
    public MatchParticipantBuilder WithWin(bool win = true) { _win = win; return this; }

    public MatchParticipantBuilder WithKda(int kills, int deaths, int assists)
    {
        _kills = kills;
        _deaths = deaths;
        _assists = assists;
        return this;
    }

    public MatchParticipantBuilder WithTotalDamageDealtToChampions(int damage) { _totalDamageDealtToChampions = damage; return this; }
    public MatchParticipantBuilder WithVisionScore(int visionScore) { _visionScore = visionScore; return this; }
    public MatchParticipantBuilder WithGoldEarned(int gold) { _goldEarned = gold; return this; }
    public MatchParticipantBuilder WithTotalMinionsKilled(int minions) { _totalMinionsKilled = minions; return this; }
    public MatchParticipantBuilder WithNeutralMinionsKilled(int minions) { _neutralMinionsKilled = minions; return this; }
    public MatchParticipantBuilder WithChampLevel(int champLevel) { _champLevel = champLevel; return this; }

    /// <summary>
    /// Overrides the six item slots at once. Slots beyond <paramref name="items"/> keep
    /// their default, so pass the full set when a test asserts on the final build.
    /// </summary>
    public MatchParticipantBuilder WithItems(int item0, int item1 = 0, int item2 = 0, int item3 = 0, int item4 = 0, int item5 = 0)
    {
        _item0 = item0;
        _item1 = item1;
        _item2 = item2;
        _item3 = item3;
        _item4 = item4;
        _item5 = item5;
        return this;
    }

    public MatchParticipantBuilder WithItem6(int item6) { _item6 = item6; return this; }

    public MatchParticipantBuilder WithTrinket(int trinketItemId)
    {
        _item6 = trinketItemId;
        _trinketItemId = trinketItemId;
        return this;
    }

    public MatchParticipantBuilder WithStatPerks(int defense, int flex, int offense)
    {
        _perksDefense = defense;
        _perksFlex = flex;
        _perksOffense = offense;
        return this;
    }

    public MatchParticipantBuilder WithPerkStyles(int primaryStyleId, int subStyleId)
    {
        _primaryStyleId = primaryStyleId;
        _subStyleId = subStyleId;
        return this;
    }

    public MatchParticipantBuilder WithSummonerSpells(int summoner1Id, int summoner2Id)
    {
        _summoner1Id = summoner1Id;
        _summoner2Id = summoner2Id;
        return this;
    }

    public MatchParticipantBuilder WithItemEvents(IEnumerable<ItemEvent> events) { _itemEvents = [.. events]; return this; }
    public MatchParticipantBuilder WithSkillEvents(IEnumerable<SkillEvent> events) { _skillEvents = [.. events]; return this; }

    public MatchParticipant Build() => new()
    {
        Id = _id,
        MatchId = _matchId,
        ParticipantId = _participantId,
        Puuid = _puuid,
        RiotAccountId = _riotAccountId,
        // Riot sends the display name, and every seed so far reused the puuid for it;
        // keeping that default means a test that asserts on the name still gets a
        // value it can correlate with the row it seeded.
        SummonerName = _summonerName ?? _puuid,
        SummonerLevel = _summonerLevel,
        ChampionId = _championId,
        TeamId = _teamId,
        TeamPosition = _teamPosition,
        IndividualPosition = _individualPosition,
        Lane = _lane,
        Role = _role,
        EloBracket = _eloBracket,
        Win = _win,
        Kills = _kills,
        Deaths = _deaths,
        Assists = _assists,
        TotalDamageDealtToChampions = _totalDamageDealtToChampions,
        VisionScore = _visionScore,
        GoldEarned = _goldEarned,
        TotalMinionsKilled = _totalMinionsKilled,
        NeutralMinionsKilled = _neutralMinionsKilled,
        ChampLevel = _champLevel,
        Item0 = _item0,
        Item1 = _item1,
        Item2 = _item2,
        Item3 = _item3,
        Item4 = _item4,
        Item5 = _item5,
        Item6 = _item6,
        TrinketItemId = _trinketItemId,
        PerksDefense = _perksDefense,
        PerksFlex = _perksFlex,
        PerksOffense = _perksOffense,
        PrimaryStyleId = _primaryStyleId,
        SubStyleId = _subStyleId,
        Summoner1Id = _summoner1Id,
        Summoner2Id = _summoner2Id,
        ItemEvents = _itemEvents,
        SkillEvents = _skillEvents
    };
}
