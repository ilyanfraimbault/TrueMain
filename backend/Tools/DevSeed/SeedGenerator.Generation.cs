using Data.Entities;

namespace DevSeed;

/// <summary>
/// The per-champion pass: the dimension rows, one build aggregate per patch and elo bracket,
/// the patch's synthetic games, and the matchup rows folded from those games.
/// </summary>
public sealed partial class SeedGenerator
{
    public GenerationResult Generate(ChampionSeed self, IReadOnlyList<ChampionSeed> laneOpponentPool, DateTime nowUtc)
    {
        var archetype = ChampionArchetypes.Archetypes[self.ArchetypeKey];
        var rng = new Rng((uint)(self.Id * 7919 + self.Position.Length));

        var matches = new List<Match>();
        var participants = new List<MatchParticipant>();
        var snapshots = new List<MatchParticipantTimelineSnapshot>();
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
                GenerateMatch(self, opponent, archetype, patch, rng, nowUtc, matches, participants, snapshots,
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

        return new GenerationResult(matches, participants, snapshots, scopes, patterns, matchupStats);
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
