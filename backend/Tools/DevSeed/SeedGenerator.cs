using Core.Lol.Lane;
using Core.Lol.Map;
using Data.Entities;
using EloBracket = Core.Lol.Ranking.EloBracket;

namespace DevSeed;

/// <summary>
/// Generates one deterministic dataset per <see cref="ChampionSeed"/>, covering
/// every read path the champion page exercises:
///
/// - Raw <c>matches</c> / <c>match_participants</c> / timeline snapshots / kill
///   positions, for the live-computed reads (Roam, Scaling, the live matchup /
///   powerspikes fallback).
/// - <c>champion_matchup_stats</c> (#606 pre-aggregation), accumulated from the
///   very same synthetic games so the live and pre-aggregated numbers agree, and
///   split per (patch, elo bracket) like the real fold — the matchups panel is
///   patch-scoped (#1087) and rank-scoped, so a single collapsed row would make
///   it read empty on every patch but the current one.
/// - <c>champion_aggregate_scopes</c> / <c>champion_aggregate_patterns</c> +
///   dimension rows (build aggregation), for the build tab / trend chart /
///   tier list.
///
/// One instance is not reused across champions — call <see cref="Generate"/>
/// once per <see cref="ChampionSeed"/> and persist its <see cref="GenerationResult"/>.
/// </summary>
public sealed class SeedGenerator(
    RiotAccount devSeedAccount,
    DimCache dimCache,
    string currentPatch,
    int patchCount,
    int gamesPerPatch)
{
    private const int QueueId = (int)LolQueueId.RankedSoloDuo;
    private const string PlatformId = "EUW1";
    private const int BlueTeamId = 100;
    private const int RedTeamId = 200;
    private static readonly int[] IntervalMinutes = [5, 10, 15, 20, 30];

    // The minute the lane outcome is judged at, matching
    // ChampionLaneOutcomeAggregationProcess. Must stay one of IntervalMinutes, or
    // no snapshot pair exists to judge from and every lane counter stays 0.
    private const int LaneOutcomeMinute = 15;

    // A ChampionAggregateScope row is persisted per elo bracket (see
    // ChampionAggregateScopeConfiguration's unique index), and the build tab's
    // rank filter (?eloBracket=GOLD_PLUS etc.) reads scoped to specific brackets
    // — a single ALL-only scope would make every rank-scoped query return
    // nothing. Split each patch's games across a handful of brackets instead so
    // that read path is actually exercisable against seeded data.
    private static readonly (string Bracket, double Share)[] EloBrackets =
    [
        (EloBracket.Gold, 0.32),
        (EloBracket.Platinum, 0.30),
        (EloBracket.Emerald, 0.22),
        (EloBracket.Diamond, 0.16),
    ];

    // Shared across every SeedGenerator instance in the process (one is created
    // per champion, sequentially, in Program.cs) so match ids never collide.
    private static int _matchCounter;

    public sealed record GenerationResult(
        List<Match> Matches,
        List<MatchParticipant> Participants,
        List<MatchParticipantTimelineSnapshot> Snapshots,
        List<MatchParticipantKillPosition> KillPositions,
        List<ChampionAggregateScope> Scopes,
        List<ChampionAggregatePattern> Patterns,
        List<ChampionMatchupStat> MatchupStats);

    public GenerationResult Generate(ChampionSeed self, IReadOnlyList<ChampionSeed> laneOpponentPool, DateTime nowUtc)
    {
        var archetype = ChampionArchetypes.Archetypes[self.ArchetypeKey];
        var rng = new Rng((uint)(self.Id * 7919 + self.Position.Length));

        var matches = new List<Match>();
        var participants = new List<MatchParticipant>();
        var snapshots = new List<MatchParticipantTimelineSnapshot>();
        var killPositions = new List<MatchParticipantKillPosition>();
        var scopes = new List<ChampionAggregateScope>();
        var patterns = new List<ChampionAggregatePattern>();

        var matchupTotals = new Dictionary<MatchupKey, MatchupTotals>();

        // One set of dimension rows per champion, reused across every patch's
        // scope — a champion's archetype build doesn't reinvent itself patch to
        // patch in this model, and reusing the same FK targets keeps the
        // champion_dim_* tables from filling up with identical duplicate rows.
        var dims = BuildDims(self, archetype, rng);

        var patches = TrendPatches(currentPatch, patchCount);
        foreach (var patch in patches)
        {
            // Pass A: the build aggregate for this patch — share-based,
            // mirroring the web mock's makeBuild(variant, totalGames) rather than
            // per-match accumulation (there are only two known variants here).
            var (patchScopes, patternRows) = BuildAggregateForPatch(self, dims, patch, nowUtc);
            scopes.AddRange(patchScopes);
            patterns.AddRange(patternRows);

            // Pass B: real per-match rows for the live-computed reads, plus the
            // matchup accumulator derived from the very same games.
            for (var i = 0; i < gamesPerPatch; i++)
            {
                var opponent = laneOpponentPool[rng.NextInt(0, laneOpponentPool.Count)];
                GenerateMatch(self, opponent, archetype, patch, rng, nowUtc, matches, participants, snapshots, killPositions,
                    matchupTotals);
            }
        }

        // One row per (patch, elo bracket, opponent), the same grain the real fold
        // writes and the unique index enforces. Collapsing them onto the current
        // patch would leave the patch-scoped matchups panel (#1087) empty on every
        // trend patch, and the rank filter empty everywhere.
        var matchupStats = matchupTotals.Select(kv => new ChampionMatchupStat
        {
            Id = Guid.NewGuid(),
            ChampionId = self.Id,
            TeamPosition = self.Position,
            OpponentChampionId = kv.Key.OpponentChampionId,
            Patch = kv.Key.Patch,
            EloBracket = kv.Key.EloBracket,
            Games = kv.Value.Games,
            Wins = kv.Value.Wins,
            LaneGames = kv.Value.LaneGames,
            LaneWins = kv.Value.LaneWins,
            LaneLosses = kv.Value.LaneLosses,
            LaneGoldDiffSum = kv.Value.LaneGoldDiffSum,
            LaneGoldDiffGames = kv.Value.LaneGoldDiffGames,
            LaneXpDiffSum = kv.Value.LaneXpDiffSum,
            LaneXpDiffGames = kv.Value.LaneXpDiffGames,
            AggregatedAtUtc = nowUtc,
        }).ToList();

        return new GenerationResult(matches, participants, snapshots, killPositions, scopes, patterns, matchupStats);
    }

    private (List<ChampionAggregateScope> Scopes, List<ChampionAggregatePattern> Patterns) BuildAggregateForPatch(
        ChampionSeed self, Dims dim, string patch, DateTime nowUtc)
    {
        var totalGames = Math.Max(20, gamesPerPatch);
        var scopes = new List<ChampionAggregateScope>();
        var patterns = new List<ChampionAggregatePattern>();

        foreach (var (bracket, share) in EloBrackets)
        {
            var bracketGames = Math.Max(4, (int)Math.Round(totalGames * share));

            var scope = new ChampionAggregateScope
            {
                Id = Guid.NewGuid(),
                RiotAccountId = devSeedAccount.Id,
                ChampionId = self.Id,
                GameVersion = patch,
                PlatformId = PlatformId,
                QueueId = QueueId,
                Position = self.Position,
                EloBracket = bracket,
                Games = bracketGames,
                Wins = (int)Math.Round(bracketGames * self.WinRate),
                LastGameStartTimeUtc = nowUtc.AddDays(-1),
                AggregatedAtUtc = nowUtc,
            };
            scopes.Add(scope);

            // Two build variants, matching the web mock's makeBuild(0|1): the
            // dominant build owns ~2/3 of the sample, the alternate the rest.
            var dominantGames = (int)Math.Round(bracketGames * 0.64);
            var altGames = (int)Math.Round(bracketGames * 0.24);

            patterns.Add(new ChampionAggregatePattern
            {
                Id = Guid.NewGuid(),
                ScopeId = scope.Id,
                BuildId = dim.DominantBuildId,
                RunePageId = dim.RunePageId,
                SkillOrderId = dim.DominantSkillOrderId,
                SpellPairId = dim.SpellPairId,
                StarterItemsId = dim.StarterItemsId,
                Games = dominantGames,
                Wins = (int)Math.Round(dominantGames * self.WinRate),
            });
            patterns.Add(new ChampionAggregatePattern
            {
                Id = Guid.NewGuid(),
                ScopeId = scope.Id,
                BuildId = dim.AltBuildId,
                RunePageId = dim.RunePageId,
                SkillOrderId = dim.AltSkillOrderId,
                SpellPairId = dim.SpellPairId,
                StarterItemsId = dim.StarterItemsId,
                Games = altGames,
                Wins = (int)Math.Round(altGames * (self.WinRate - 0.015)),
            });
        }

        return (scopes, patterns);
    }

    /// <summary>Grain of a <c>champion_matchup_stats</c> row, minus the champion side.</summary>
    private readonly record struct MatchupKey(string Patch, string EloBracket, int OpponentChampionId);

    /// <summary>
    /// Additive counters for one <see cref="MatchupKey"/>, mirroring what the matchup
    /// fold (#606/#811) and the lane-outcome fold (#919/#976/#1111) accumulate.
    /// </summary>
    private sealed class MatchupTotals
    {
        public int Games { get; set; }

        public int Wins { get; set; }

        public int LaneGames { get; set; }

        public int LaneWins { get; set; }

        public int LaneLosses { get; set; }

        public long LaneGoldDiffSum { get; set; }

        public int LaneGoldDiffGames { get; set; }

        public long LaneXpDiffSum { get; set; }

        public int LaneXpDiffGames { get; set; }
    }

    private sealed record Dims(
        Guid DominantBuildId,
        Guid AltBuildId,
        Guid RunePageId,
        Guid DominantSkillOrderId,
        Guid AltSkillOrderId,
        Guid SpellPairId,
        Guid StarterItemsId);

    private Dims BuildDims(ChampionSeed self, Archetype archetype, Rng rng)
    {
        var items = archetype.Items;
        var dominantBuildId = dimCache.GetOrAddBuild(archetype.Boots[0], [items[0], items[1], items[2], items[3], items.ElementAtOrDefault(4), items.ElementAtOrDefault(5), 0]);
        var altBootsId = archetype.Boots.ElementAtOrDefault(1) is var b and not 0 ? b : archetype.Boots[0];
        var altBuildId = dimCache.GetOrAddBuild(altBootsId, [items[1], items[0], items.ElementAtOrDefault(2), items.ElementAtOrDefault(3), items.ElementAtOrDefault(4), items.ElementAtOrDefault(5), 0]);

        var primary = ChampionArchetypes.StylePerks[self.PrimaryStyle];
        var secondary = ChampionArchetypes.StylePerks[self.SecondaryStyle];
        var runePageId = dimCache.GetOrAddRunePage(
            self.PrimaryStyle, self.Keystone,
            Pick(primary[1], rng), Pick(primary[2], rng), Pick(primary[3], rng),
            self.SecondaryStyle, Pick(secondary[1], rng), Pick(secondary[2], rng),
            Pick(ChampionArchetypes.StatOffense, rng), Pick(ChampionArchetypes.StatFlex, rng), Pick(ChampionArchetypes.StatDefense, rng));

        var dominantSkillOrderId = dimCache.GetOrAddSkillOrder(archetype.SkillOrders[0]);
        var altSkillOrderId = dimCache.GetOrAddSkillOrder(archetype.SkillOrders[1]);
        var spellPairId = dimCache.GetOrAddSpellPair(archetype.Spells.Spell1, archetype.Spells.Spell2);
        var starterItemsId = dimCache.GetOrAddStarterItems(archetype.StarterItems);

        return new Dims(dominantBuildId, altBuildId, runePageId, dominantSkillOrderId, altSkillOrderId, spellPairId, starterItemsId);
    }

    private static int Pick(int[] row, Rng rng) => row[rng.NextInt(0, row.Length)];

    private void GenerateMatch(
        ChampionSeed self,
        ChampionSeed opponent,
        Archetype archetype,
        string patch,
        Rng rng,
        DateTime nowUtc,
        List<Match> matches,
        List<MatchParticipant> participants,
        List<MatchParticipantTimelineSnapshot> snapshots,
        List<MatchParticipantKillPosition> killPositions,
        Dictionary<MatchupKey, MatchupTotals> matchupTotals)
    {
        var matchId = $"DEVSEED_{_matchCounter++:D8}";
        var eloBracket = WeightedEloBracket(rng);

        // Bucket the game length the same way ChampionScalingQueryService does,
        // weighted toward the middle buckets, and adjust the win probability by
        // the archetype's scaling slope so Scaling has a real signal to read.
        var bucket = WeightedBucket(rng);
        var durationSeconds = bucket switch
        {
            0 => rng.NextInt(900, 1200), // <20m
            1 => rng.NextInt(1200, 1500), // 20-25m
            2 => rng.NextInt(1500, 1800), // 25-30m
            3 => rng.NextInt(1800, 2100), // 30-35m
            _ => rng.NextInt(2100, 2400), // 35m+
        };
        var slope = ChampionArchetypes.ScalingSlope[self.ArchetypeKey];
        var winProbability = Math.Clamp(self.WinRate + slope * (bucket - 2) + rng.NextDouble(-0.02, 0.02), 0.05, 0.95);
        var win = rng.NextDouble() < winProbability;

        var selfIsBlue = rng.NextDouble() < 0.5;
        var selfTeam = selfIsBlue ? BlueTeamId : RedTeamId;
        var opponentTeam = selfIsBlue ? RedTeamId : BlueTeamId;

        matches.Add(new Match
        {
            Id = matchId,
            PlatformId = PlatformId,
            QueueId = QueueId,
            MapId = 11,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = nowUtc.AddMinutes(-rng.NextInt(0, 60 * 24 * 60)),
            GameDurationSeconds = durationSeconds,
            GameVersion = $"{patch}.{rng.NextInt(1, 30)}.{rng.NextInt(100, 999)}",
            CreatedAtUtc = nowUtc,
            TimelineIngested = true,
        });

        var selfParticipantId = 1;
        var opponentParticipantId = 2;

        // Bias so the position's roam tendency shows up in the Roam metric:
        // supports/mids read as roamers, side lanes stay lane-bound.
        var roamShare = ChampionArchetypes.RoamSharePerPosition.GetValueOrDefault(self.Position, 0.25);
        var itemEvents = BuildItemEvents(archetype, rng);

        var selfKills = rng.NextInt(0, 12);
        var selfDeaths = rng.NextInt(0, 8);
        var selfAssists = rng.NextInt(0, 14);

        participants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = selfParticipantId,
            Puuid = devSeedAccount.Puuid,
            RiotAccountId = devSeedAccount.Id,
            SummonerName = devSeedAccount.GameName,
            SummonerLevel = devSeedAccount.SummonerLevel,
            ChampionId = self.Id,
            TeamId = selfTeam,
            TeamPosition = self.Position,
            IndividualPosition = self.Position,
            Lane = self.Position == "UTILITY" ? "BOTTOM" : self.Position,
            Role = self.Position == "UTILITY" ? "SUPPORT" : "SOLO",
            // Stamped here rather than left empty: EloBracketEnrichment does it in
            // production, and the rank-filtered live reads seek this column.
            EloBracket = eloBracket,
            Win = win,
            Kills = selfKills,
            Deaths = selfDeaths,
            Assists = selfAssists,
            TotalDamageDealtToChampions = rng.NextInt(8000, 32000),
            VisionScore = rng.NextInt(10, 60),
            GoldEarned = rng.NextInt(8000, 18000),
            TotalMinionsKilled = rng.NextInt(80, 260),
            NeutralMinionsKilled = self.Position == "JUNGLE" ? rng.NextInt(80, 180) : rng.NextInt(0, 20),
            ChampLevel = rng.NextInt(14, 19),
            Item0 = archetype.Boots[0],
            Item1 = archetype.Items.ElementAtOrDefault(0),
            Item2 = archetype.Items.ElementAtOrDefault(1),
            Item3 = archetype.Items.ElementAtOrDefault(2),
            Item4 = archetype.Items.ElementAtOrDefault(3),
            Item5 = archetype.Items.ElementAtOrDefault(4),
            Item6 = 0,
            TrinketItemId = 3364,
            PerksDefense = ChampionArchetypes.StatDefense[0],
            PerksFlex = ChampionArchetypes.StatFlex[0],
            PerksOffense = ChampionArchetypes.StatOffense[0],
            PrimaryStyleId = self.PrimaryStyle,
            SubStyleId = self.SecondaryStyle,
            Summoner1Id = archetype.Spells.Spell1,
            Summoner2Id = archetype.Spells.Spell2,
            ItemEvents = itemEvents,
        });

        participants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = opponentParticipantId,
            Puuid = $"devseed-opponent-{matchId}",
            SummonerName = "DevSeedOpponent",
            SummonerLevel = 200,
            ChampionId = opponent.Id,
            TeamId = opponentTeam,
            TeamPosition = self.Position,
            IndividualPosition = self.Position,
            Lane = self.Position == "UTILITY" ? "BOTTOM" : self.Position,
            Role = self.Position == "UTILITY" ? "SUPPORT" : "SOLO",
            Win = !win,
            Kills = rng.NextInt(0, 12),
            Deaths = rng.NextInt(0, 8),
            Assists = rng.NextInt(0, 14),
            TotalDamageDealtToChampions = rng.NextInt(8000, 32000),
            VisionScore = rng.NextInt(10, 60),
            GoldEarned = rng.NextInt(8000, 18000),
            TotalMinionsKilled = rng.NextInt(80, 260),
            ChampLevel = rng.NextInt(14, 19),
            TrinketItemId = 3364,
            PrimaryStyleId = opponent.PrimaryStyle,
            SubStyleId = opponent.SecondaryStyle,
            Summoner1Id = Spells.Flash,
            Summoner2Id = Spells.Teleport,
        });

        // Timeline snapshots for both sides, and the lead accumulator. Gold/xp
        // grow roughly linearly with a per-archetype late-game tilt; the diff
        // (self - opponent) is what "lead vs lane opponent" reads.
        var bias = (self.WinRate - 0.5) * 20;
        int? laneGoldDiff = null;
        int? laneXpDiff = null;
        foreach (var minute in IntervalMinutes)
        {
            if (minute * 60 > durationSeconds)
            {
                break;
            }

            var driftIndex = Array.IndexOf(IntervalMinutes, minute) + 1;
            var drift = driftIndex * (bias + rng.NextDouble(-1.6, 1.6));

            var selfGold = 500 + minute * 380 + rng.NextInt(-150, 150);
            var selfXp = 500 + minute * 420;
            var selfCs = minute * 7;
            var selfLevel = Math.Min(18, 1 + minute / 2);
            var selfDamage = minute * 950 + rng.NextInt(-200, 200);

            var goldDiff = (int)Math.Round(drift * 55);
            var csDiff = drift * 0.55;
            var killsDiff = drift * 0.045;
            var levelDiff = drift * 0.02;
            var xpDiff = (int)Math.Round(drift * 38);
            var damageDiff = (int)Math.Round(drift * 140);

            if (minute == LaneOutcomeMinute)
            {
                laneGoldDiff = goldDiff;
                laneXpDiff = xpDiff;
            }

            snapshots.Add(new MatchParticipantTimelineSnapshot
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                ParticipantId = selfParticipantId,
                IntervalMinute = minute,
                TimestampMs = minute * 60_000,
                TotalGold = Math.Max(0, selfGold),
                MinionsKilled = Math.Max(0, selfCs),
                JungleMinionsKilled = self.Position == "JUNGLE" ? minute * 5 : 0,
                Level = selfLevel,
                Xp = Math.Max(0, selfXp),
                Kills = Math.Min(selfKills, minute / 3),
                DamageToChampions = Math.Max(0, selfDamage),
                WardsPlaced = minute / 3,
                WardsKilled = minute / 6,
            });

            snapshots.Add(new MatchParticipantTimelineSnapshot
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                ParticipantId = opponentParticipantId,
                IntervalMinute = minute,
                TimestampMs = minute * 60_000,
                TotalGold = Math.Max(0, selfGold - goldDiff),
                MinionsKilled = Math.Max(0, selfCs - (int)Math.Round(csDiff)),
                JungleMinionsKilled = 0,
                Level = Math.Clamp(selfLevel - (int)Math.Round(levelDiff), 1, 18),
                Xp = Math.Max(0, selfXp - xpDiff),
                Kills = Math.Max(0, (int)Math.Round((selfKills) - killsDiff)),
                DamageToChampions = Math.Max(0, selfDamage - damageDiff),
                WardsPlaced = minute / 3,
                WardsKilled = minute / 6,
            });
        }

        // Kill positions before the 15-minute cutoff: a mix of in-lane and
        // out-of-lane (roam) participations, biased by the position's roam share.
        var killCount = selfKills + selfAssists > 0 ? rng.NextInt(1, 6) : 0;
        for (var i = 0; i < killCount; i++)
        {
            var timestampMs = rng.NextInt(30_000, 900_000);
            var isRoam = rng.NextDouble() < roamShare;
            var (x, y) = isRoam ? MapPoints.EnemyJungle(selfIsBlue) : MapPoints.OwnLane(self.Position);
            killPositions.Add(new MatchParticipantKillPosition
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                ParticipantId = selfParticipantId,
                TimestampMs = timestampMs,
                X = x,
                Y = y,
            });
        }

        var matchupKey = new MatchupKey(patch, eloBracket, opponent.Id);
        if (!matchupTotals.TryGetValue(matchupKey, out var totals))
        {
            totals = new MatchupTotals();
            matchupTotals[matchupKey] = totals;
        }

        totals.Games++;
        totals.Wins += win ? 1 : 0;

        // Lane counters only when both sides have a 15-minute snapshot — the same
        // condition the real fold applies (#919), which is why LaneGames is a
        // separate denominator from Games. A game shorter than 15 minutes has no
        // snapshot pair here either, so the two stay honestly apart.
        if (laneGoldDiff is { } goldDiffAt15)
        {
            totals.LaneGames++;
            switch (LaneOutcomeRules.Judge(goldDiffAt15, LaneOutcomeRules.DefaultGoldLeadThreshold))
            {
                case LaneStanding.Won:
                    totals.LaneWins++;
                    break;
                case LaneStanding.Lost:
                    totals.LaneLosses++;
                    break;
                case LaneStanding.Even:
                default:
                    break;
            }

            totals.LaneGoldDiffSum += goldDiffAt15;
            totals.LaneGoldDiffGames++;
            totals.LaneXpDiffSum += laneXpDiff ?? 0;
            totals.LaneXpDiffGames++;
        }
    }

    /// <summary>
    /// Draws the match's elo band from the same share table the build scopes use, so
    /// a rank-filtered read finds seeded games in every band the build tab offers.
    /// </summary>
    private static string WeightedEloBracket(Rng rng)
    {
        var roll = rng.NextDouble();
        double cumulative = 0;
        foreach (var (bracket, share) in EloBrackets)
        {
            cumulative += share;
            if (roll < cumulative)
            {
                return bracket;
            }
        }

        return EloBrackets[^1].Bracket;
    }

    private static int WeightedBucket(Rng rng)
    {
        double[] weights = [0.22, 0.28, 0.24, 0.16, 0.10];
        var roll = rng.NextDouble();
        double cumulative = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }

    private static List<ItemEvent> BuildItemEvents(Archetype archetype, Rng rng)
    {
        var events = new List<ItemEvent>();
        var timestampMs = 90_000;

        void Add(int itemId)
        {
            events.Add(new ItemEvent { TimestampMs = timestampMs, EventType = "ITEM_PURCHASED", ItemId = itemId });
            timestampMs += 400_000 + rng.NextInt(-60_000, 60_000);
        }

        foreach (var starter in archetype.StarterItems)
        {
            Add(starter);
        }

        Add(archetype.Boots[0]);
        foreach (var item in archetype.Items.Take(6))
        {
            Add(item);
        }

        return events;
    }

    /// <summary>Previous short patches, newest last — mirrors the web mock's trendPatches helper.</summary>
    private static IReadOnlyList<string> TrendPatches(string latest, int count)
    {
        var parts = latest.Split('.');
        var major = int.Parse(parts[0]);
        var minor = int.Parse(parts[1]);
        var patches = new List<string>();
        for (var i = count - 1; i >= 0; i--)
        {
            var m = minor - i;
            patches.Add(m >= 1 ? $"{major}.{m}" : $"{major - 1}.{24 + m}");
        }

        return patches;
    }
}
