using Data.Entities;

namespace DevSeed;

/// <summary>
/// Generates one deterministic dataset per <see cref="ChampionSeed"/>, covering
/// every read path the champion page exercises:
///
/// - Raw <c>matches</c> / <c>match_participants</c> / timeline snapshots / kill
///   positions, for the live-computed reads (Roam, Scaling, the live matchup /
///   powerspikes fallback).
/// - <c>champion_matchup_stats</c> / <c>champion_timeline_lead_stats</c> (#606
///   pre-aggregation), accumulated from the very same synthetic games so the
///   live and pre-aggregated numbers agree.
/// - <c>champion_aggregate_scopes</c> / <c>champion_aggregate_patterns</c> +
///   dimension rows (Phase 6 build aggregation), for the build tab / trend
///   chart / tier list.
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
    private const int QueueId = 420;
    private const string PlatformId = "EUW1";
    private const int BlueTeamId = 100;
    private const int RedTeamId = 200;
    private static readonly int[] IntervalMinutes = [5, 10, 15, 20, 30];

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
        List<ChampionMatchupStat> MatchupStats,
        List<ChampionTimelineLeadStat> LeadStats);

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

        var matchupTotals = new Dictionary<int, (int Games, int Wins)>();
        var leadTotals = new Dictionary<(string Patch, int Minute), (int Games, long Gold, long Cs, long Kills, long Level, long Xp, long Damage)>();

        // One set of dimension rows per champion, reused across every patch's
        // scope — a champion's archetype build doesn't reinvent itself patch to
        // patch in this model, and reusing the same FK targets keeps the
        // champion_dim_* tables from filling up with identical duplicate rows.
        var dims = BuildDims(self, archetype, rng);

        var patches = TrendPatches(currentPatch, patchCount);
        foreach (var patch in patches)
        {
            // Pass A: the Phase 6 build aggregate for this patch — share-based,
            // mirroring the web mock's makeBuild(variant, totalGames) rather than
            // per-match accumulation (there are only two known variants here).
            var (scope, patternRows) = BuildAggregateForPatch(self, dims, patch, nowUtc);
            scopes.Add(scope);
            patterns.AddRange(patternRows);

            // Pass B: real per-match rows for the live-computed reads, plus the
            // matchup / lead accumulators derived from the very same games.
            for (var i = 0; i < gamesPerPatch; i++)
            {
                var opponent = laneOpponentPool[rng.NextInt(0, laneOpponentPool.Count)];
                GenerateMatch(self, opponent, archetype, patch, rng, nowUtc, matches, participants, snapshots, killPositions,
                    matchupTotals, leadTotals);
            }
        }

        var matchupStats = matchupTotals.Select(kv => new ChampionMatchupStat
        {
            Id = Guid.NewGuid(),
            ChampionId = self.Id,
            TeamPosition = self.Position,
            OpponentChampionId = kv.Key,
            Patch = currentPatch, // folded across the run; per-patch split isn't needed for the matchup leaderboard read
            Games = kv.Value.Games,
            Wins = kv.Value.Wins,
            AggregatedAtUtc = nowUtc,
        }).ToList();

        var leadStats = leadTotals.Select(kv => new ChampionTimelineLeadStat
        {
            Id = Guid.NewGuid(),
            ChampionId = self.Id,
            TeamPosition = self.Position,
            Patch = kv.Key.Patch,
            IntervalMinute = kv.Key.Minute,
            Games = kv.Value.Games,
            TotalGoldDiff = kv.Value.Gold,
            TotalCsDiff = kv.Value.Cs,
            TotalKillsDiff = kv.Value.Kills,
            TotalLevelDiff = kv.Value.Level,
            TotalXpDiff = kv.Value.Xp,
            TotalDamageDiff = kv.Value.Damage,
            AggregatedAtUtc = nowUtc,
        }).ToList();

        return new GenerationResult(matches, participants, snapshots, killPositions, scopes, patterns, matchupStats, leadStats);
    }

    private (ChampionAggregateScope Scope, List<ChampionAggregatePattern> Patterns) BuildAggregateForPatch(
        ChampionSeed self, Dims dim, string patch, DateTime nowUtc)
    {
        var totalGames = Math.Max(20, gamesPerPatch);
        var scopeWins = (int)Math.Round(totalGames * self.WinRate);

        var scope = new ChampionAggregateScope
        {
            Id = Guid.NewGuid(),
            RiotAccountId = devSeedAccount.Id,
            ChampionId = self.Id,
            GameVersion = patch,
            PlatformId = PlatformId,
            QueueId = QueueId,
            Position = self.Position,
            Games = totalGames,
            Wins = scopeWins,
            LastGameStartTimeUtc = nowUtc.AddDays(-1),
            AggregatedAtUtc = nowUtc,
        };

        // Two build variants, matching the web mock's makeBuild(0|1): the
        // dominant build owns ~2/3 of the sample, the alternate the rest.
        var dominantGames = (int)Math.Round(totalGames * 0.64);
        var altGames = (int)Math.Round(totalGames * 0.24);

        var patterns = new List<ChampionAggregatePattern>
        {
            new()
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
            },
            new()
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
            },
        };

        return (scope, patterns);
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
        Dictionary<int, (int Games, int Wins)> matchupTotals,
        Dictionary<(string Patch, int Minute), (int Games, long Gold, long Cs, long Kills, long Level, long Xp, long Damage)> leadTotals)
    {
        var matchId = $"DEVSEED_{_matchCounter++:D8}";

        // Bucket the game length the same way ChampionScalingQueryService does,
        // weighted toward the middle buckets, and adjust the win probability by
        // the archetype's scaling slope so Scaling has a real signal to read.
        var bucket = WeightedBucket(rng);
        var durationSeconds = bucket switch
        {
            0 => rng.NextInt(1500, 1800), // Riot's actual floor differs; only relative bucketing matters here.
            1 => rng.NextInt(1200, 1500),
            2 => rng.NextInt(1500, 1800),
            3 => rng.NextInt(1800, 2100),
            _ => rng.NextInt(2100, 2400),
        } - (bucket == 0 ? 300 : 0); // bucket 0 is "<20m": pull it under 1200s.
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

            var leadKey = (patch, minute);
            leadTotals.TryGetValue(leadKey, out var leadAcc);
            leadTotals[leadKey] = (
                leadAcc.Games + 1,
                leadAcc.Gold + goldDiff,
                leadAcc.Cs + (long)Math.Round(csDiff),
                leadAcc.Kills + (long)Math.Round(killsDiff),
                leadAcc.Level + (long)Math.Round(levelDiff),
                leadAcc.Xp + xpDiff,
                leadAcc.Damage + damageDiff);
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

        matchupTotals.TryGetValue(opponent.Id, out var matchupAcc);
        matchupTotals[opponent.Id] = (matchupAcc.Games + 1, matchupAcc.Wins + (win ? 1 : 0));
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
