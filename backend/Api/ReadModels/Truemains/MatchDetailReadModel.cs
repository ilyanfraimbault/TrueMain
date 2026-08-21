namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// Full single-match detail payload backing
/// <c>GET /truemains/{nameTag}/matches/{matchId}</c>. Everything the three
/// detail tabs (General scoreboard / Details build+lane / Runes) need in one
/// round trip: the match header, all 10 participants with their build order,
/// skill order, rune page and timeline-derived laning stats.
///
/// Per the story's "Out of scope" list this carries no team-objective counters
/// and no ward counts — only data already in the DB. Derived per-minute rates,
/// laning diffs and the performance score / placement / MVP / ACE accolades
/// (#639) are computed server-side so the frontend renders them directly.
/// </summary>
public sealed record MatchDetailReadModel
{
    public string MatchId { get; init; } = string.Empty;

    public int QueueId { get; init; }

    public string GameMode { get; init; } = string.Empty;

    public DateTime GameStartTimeUtc { get; init; }

    public int GameDurationSeconds { get; init; }

    public string GameVersion { get; init; } = string.Empty;

    public IReadOnlyList<MatchDetailParticipantReadModel> Participants { get; init; }
        = Array.Empty<MatchDetailParticipantReadModel>();
}

/// <summary>
/// One participant's full slice: identity, scoreboard line, build order, skill
/// order, rune page and the derived per-minute / laning stats.
/// </summary>
public sealed record MatchDetailParticipantReadModel
{
    public int ParticipantId { get; init; }

    public int ChampionId { get; init; }

    public int ChampLevel { get; init; }

    public string SummonerName { get; init; } = string.Empty;

    /// <summary>Riot game name when the participant is a tracked account, else null.</summary>
    public string? GameName { get; init; }

    /// <summary>Riot tag line when the participant is a tracked account, else null.</summary>
    public string? TagLine { get; init; }

    /// <summary>100 = blue side, 200 = red side.</summary>
    public int TeamId { get; init; }

    /// <summary>Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY); empty when unknown.</summary>
    public string TeamPosition { get; init; } = string.Empty;

    public bool Win { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public int Assists { get; init; }

    /// <summary>Inventory slots 0..6 (length 7). The trinket is in <see cref="TrinketItemId"/>.</summary>
    public IReadOnlyList<int> Items { get; init; } = Array.Empty<int>();

    public int TrinketItemId { get; init; }

    public int Summoner1Id { get; init; }

    public int Summoner2Id { get; init; }

    public int PrimaryStyleId { get; init; }

    public int SubStyleId { get; init; }

    /// <summary>The keystone perk id (slot 0 of the primary tree); 0 when the page failed to ingest.</summary>
    public int KeystoneId { get; init; }

    public int TotalDamageDealtToChampions { get; init; }

    public int VisionScore { get; init; }

    public int GoldEarned { get; init; }

    /// <summary>Sum of lane minions + neutral monsters.</summary>
    public int Cs { get; init; }

    /// <summary>Approximate rank tier at game time (closest snapshot). Null when no snapshot exists.</summary>
    public MatchDetailRankReadModel? Rank { get; init; }

    // ── Derived (computed server-side) ──────────────────────────────────────

    /// <summary>
    /// Kill participation — <c>(kills + assists) / teamKills</c>, 0 when the team
    /// scored no kills. Usually 0..1 but can exceed 1 when a player's kills plus
    /// assists outnumber the team's total kills (shared assists are double-counted).
    /// </summary>
    public double KillParticipation { get; init; }

    /// <summary>CS per minute.</summary>
    public double CsPerMin { get; init; }

    /// <summary>Damage to champions per minute.</summary>
    public double DamagePerMin { get; init; }

    /// <summary>Gold per minute.</summary>
    public double GoldPerMin { get; init; }

    /// <summary>Vision score per minute.</summary>
    public double VisionPerMin { get; init; }

    /// <summary>
    /// TrueMain performance score, 0–100 — a weighted, role-aware blend of KDA,
    /// kill participation, damage share, gold share, CS/min, vision/min and the
    /// @15 laning leads. See <c>Core.Lol.Performance.PerformanceScore</c> for the
    /// full model and its weights.
    /// </summary>
    public int PerformanceScore { get; init; }

    /// <summary>
    /// 1-based rank of this participant's <see cref="PerformanceScore"/> within the
    /// match (1 = best of all 10). Ties break on takedowns, then deaths, then
    /// participant id, so the placement is always a strict 1..N.
    /// </summary>
    public int Placement { get; init; }

    /// <summary>True for the top-scoring participant of the winning side.</summary>
    public bool IsMvp { get; init; }

    /// <summary>True for the top-scoring participant of the losing side.</summary>
    public bool IsAce { get; init; }

    /// <summary>Laning diffs @15 vs the opposing <c>TeamPosition</c>. Null when either side lacks a @15 snapshot.</summary>
    public MatchDetailLaning15ReadModel? Laning15 { get; init; }

    /// <summary>
    /// True when this participant reached level 2 (their 2nd skill point)
    /// strictly before their lane opponent. Null when there is no opponent in
    /// the same <c>TeamPosition</c> or either skill timeline is missing.
    /// </summary>
    public bool? FirstToLevelTwo { get; init; }

    /// <summary>Full rune page (6 selections): the primary tree (keystone first) then the secondary tree.</summary>
    public IReadOnlyList<MatchDetailRuneReadModel> Runes { get; init; }
        = Array.Empty<MatchDetailRuneReadModel>();

    /// <summary>Stat-shard ids — offense / flex / defense.</summary>
    public int StatPerkOffense { get; init; }

    public int StatPerkFlex { get; init; }

    public int StatPerkDefense { get; init; }

    /// <summary>Build order (purchases / sells / undos) in chronological order.</summary>
    public IReadOnlyList<MatchDetailItemEventReadModel> ItemEvents { get; init; }
        = Array.Empty<MatchDetailItemEventReadModel>();

    /// <summary>Skill order (Q/W/E/R level-ups) in chronological order.</summary>
    public IReadOnlyList<MatchDetailSkillEventReadModel> SkillEvents { get; init; }
        = Array.Empty<MatchDetailSkillEventReadModel>();

    /// <summary>
    /// The participant's measured jungle first clear (#1188). Null for the eight
    /// non-junglers and for matches ingested without timeline coverage.
    /// </summary>
    public MatchDetailJungleClearReadModel? JungleClear { get; init; }
}

/// <summary>
/// One jungler's first clear as stored in <c>jungle_first_clears</c>. Carries no
/// camp order on purpose: Riot samples positions once a minute while a clear
/// takes ~1:45, so the camp sequence is not reconstructable (#1188).
/// </summary>
public sealed record MatchDetailJungleClearReadModel
{
    /// <summary>The camp the jungler opened on (<c>JungleCamp</c> enum name); null when unknown.</summary>
    public string? StartCamp { get; init; }

    /// <summary>Per-minute clear-speed samples, ascending by timestamp.</summary>
    public IReadOnlyList<MatchDetailJungleClearSampleReadModel> Samples { get; init; }
        = Array.Empty<MatchDetailJungleClearSampleReadModel>();

    /// <summary>First frame (ms) where jungle CS reached a full clear's worth; null if never.</summary>
    public int? FullClearTimeMs { get; init; }

    /// <summary>Jungle monsters that make up a full six-camp clear — the yardstick for the samples.</summary>
    public int FullClearJungleCs { get; init; }
}

public sealed record MatchDetailJungleClearSampleReadModel
{
    public int TimestampMs { get; init; }

    public int JungleCs { get; init; }

    /// <summary>Sampled map position — where the jungler was, not a camp claim.</summary>
    public int X { get; init; }

    public int Y { get; init; }
}

public sealed record MatchDetailRankReadModel
{
    public string Tier { get; init; } = string.Empty;

    public string Division { get; init; } = string.Empty;

    public int LeaguePoints { get; init; }
}

/// <summary>Laning-phase diffs @15 (this participant minus their lane opponent).</summary>
public sealed record MatchDetailLaning15ReadModel
{
    public int CsDiff { get; init; }

    public int GoldDiff { get; init; }

    public int XpDiff { get; init; }
}

public sealed record MatchDetailRuneReadModel
{
    /// <summary>Owning style (rune tree) id.</summary>
    public int StyleId { get; init; }

    /// <summary>Slot index within the style (0 = keystone of the primary tree).</summary>
    public int SelectionIndex { get; init; }

    public int PerkId { get; init; }
}

public sealed record MatchDetailItemEventReadModel
{
    public int TimestampMs { get; init; }

    /// <summary>Riot event type: ITEM_PURCHASED / ITEM_SOLD / ITEM_DESTROYED / ITEM_UNDO.</summary>
    public string EventType { get; init; } = string.Empty;

    public int ItemId { get; init; }

    /// <summary>On an ITEM_UNDO, the item id that existed before the undo (Riot <c>beforeId</c>).</summary>
    public int? BeforeId { get; init; }

    /// <summary>On an ITEM_UNDO, the item id that exists after the undo (Riot <c>afterId</c>).</summary>
    public int? AfterId { get; init; }
}

public sealed record MatchDetailSkillEventReadModel
{
    public int TimestampMs { get; init; }

    /// <summary>1 = Q, 2 = W, 3 = E, 4 = R.</summary>
    public int SkillSlot { get; init; }
}
