using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class TimelineAggregateRepository(TrueMainDbContext db) : ITimelineAggregateRepository
{
    public async Task ApplyAsync(
        TimelineAggregateContribution contribution,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        await ApplyLeadsAsync(contribution.Leads, aggregatedAtUtc, ct);
        await ApplySigmasAsync(contribution.Sigmas, aggregatedAtUtc, ct);
        await ApplyEventsAsync(contribution.Events, aggregatedAtUtc, ct);
    }

    // Each aggregate is folded with a single set-based INSERT ... SELECT FROM
    // unnest(arrays) ... ON CONFLICT DO UPDATE, so one match is one round trip per
    // table regardless of row count, and concurrent matches merge additively.
    private async Task ApplyLeadsAsync(
        IReadOnlyList<TimelineLeadContribution> leads,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (leads.Count == 0)
        {
            return;
        }

        var championId = leads.Select(l => l.ChampionId).ToArray();
        var teamPosition = leads.Select(l => l.TeamPosition).ToArray();
        var patch = leads.Select(l => l.Patch).ToArray();
        var minute = leads.Select(l => l.IntervalMinute).ToArray();
        var games = leads.Select(l => l.Games).ToArray();
        var gold = leads.Select(l => l.GoldDiff).ToArray();
        var cs = leads.Select(l => l.CsDiff).ToArray();
        var kills = leads.Select(l => l.KillsDiff).ToArray();
        var level = leads.Select(l => l.LevelDiff).ToArray();
        var xp = leads.Select(l => l.XpDiff).ToArray();
        var damage = leads.Select(l => l.DamageDiff).ToArray();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO champion_timeline_lead_stats
                (""Id"", ""ChampionId"", ""TeamPosition"", ""Patch"", ""IntervalMinute"", ""Games"",
                 ""TotalGoldDiff"", ""TotalCsDiff"", ""TotalKillsDiff"", ""TotalLevelDiff"", ""TotalXpDiff"", ""TotalDamageDiff"", ""AggregatedAtUtc"")
            SELECT gen_random_uuid(), c, tp, p, m, g, gd, cd, kd, ld, xd, dd, {aggregatedAtUtc}
            FROM unnest({championId}, {teamPosition}, {patch}, {minute}, {games}, {gold}, {cs}, {kills}, {level}, {xp}, {damage})
                AS x(c, tp, p, m, g, gd, cd, kd, ld, xd, dd)
            ON CONFLICT (""ChampionId"", ""TeamPosition"", ""Patch"", ""IntervalMinute"")
            DO UPDATE SET
                ""Games"" = champion_timeline_lead_stats.""Games"" + EXCLUDED.""Games"",
                ""TotalGoldDiff"" = champion_timeline_lead_stats.""TotalGoldDiff"" + EXCLUDED.""TotalGoldDiff"",
                ""TotalCsDiff"" = champion_timeline_lead_stats.""TotalCsDiff"" + EXCLUDED.""TotalCsDiff"",
                ""TotalKillsDiff"" = champion_timeline_lead_stats.""TotalKillsDiff"" + EXCLUDED.""TotalKillsDiff"",
                ""TotalLevelDiff"" = champion_timeline_lead_stats.""TotalLevelDiff"" + EXCLUDED.""TotalLevelDiff"",
                ""TotalXpDiff"" = champion_timeline_lead_stats.""TotalXpDiff"" + EXCLUDED.""TotalXpDiff"",
                ""TotalDamageDiff"" = champion_timeline_lead_stats.""TotalDamageDiff"" + EXCLUDED.""TotalDamageDiff"",
                ""AggregatedAtUtc"" = EXCLUDED.""AggregatedAtUtc""", ct);
    }

    private async Task ApplySigmasAsync(
        IReadOnlyList<TimelineSigmaContribution> sigmas,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (sigmas.Count == 0)
        {
            return;
        }

        var queueId = sigmas.Select(s => s.QueueId).ToArray();
        var patch = sigmas.Select(s => s.Patch).ToArray();
        var minute = sigmas.Select(s => s.IntervalMinute).ToArray();
        var n = sigmas.Select(s => s.N).ToArray();
        var sumGold = sigmas.Select(s => s.SumGold).ToArray();
        var sumSqGold = sigmas.Select(s => s.SumSqGold).ToArray();
        var sumDmg = sigmas.Select(s => s.SumDmg).ToArray();
        var sumSqDmg = sigmas.Select(s => s.SumSqDmg).ToArray();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO timeline_lead_sigma_moments
                (""Id"", ""QueueId"", ""Patch"", ""IntervalMinute"", ""N"", ""SumGold"", ""SumSqGold"", ""SumDmg"", ""SumSqDmg"", ""AggregatedAtUtc"")
            SELECT gen_random_uuid(), q, p, m, n, sg, ssg, sd, ssd, {aggregatedAtUtc}
            FROM unnest({queueId}, {patch}, {minute}, {n}, {sumGold}, {sumSqGold}, {sumDmg}, {sumSqDmg})
                AS x(q, p, m, n, sg, ssg, sd, ssd)
            ON CONFLICT (""QueueId"", ""Patch"", ""IntervalMinute"")
            DO UPDATE SET
                ""N"" = timeline_lead_sigma_moments.""N"" + EXCLUDED.""N"",
                ""SumGold"" = timeline_lead_sigma_moments.""SumGold"" + EXCLUDED.""SumGold"",
                ""SumSqGold"" = timeline_lead_sigma_moments.""SumSqGold"" + EXCLUDED.""SumSqGold"",
                ""SumDmg"" = timeline_lead_sigma_moments.""SumDmg"" + EXCLUDED.""SumDmg"",
                ""SumSqDmg"" = timeline_lead_sigma_moments.""SumSqDmg"" + EXCLUDED.""SumSqDmg"",
                ""AggregatedAtUtc"" = EXCLUDED.""AggregatedAtUtc""", ct);
    }

    private async Task ApplyEventsAsync(
        IReadOnlyList<TimelinePowerspikeEventContribution> events,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (events.Count == 0)
        {
            return;
        }

        var championId = events.Select(e => e.ChampionId).ToArray();
        var teamPosition = events.Select(e => e.TeamPosition).ToArray();
        var patch = events.Select(e => e.Patch).ToArray();
        var eventType = events.Select(e => e.EventType).ToArray();
        var refId = events.Select(e => e.RefId).ToArray();
        var games = events.Select(e => e.Games).ToArray();
        var sumMinute = events.Select(e => e.SumEventMinute).ToArray();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO champion_powerspike_event_stats
                (""Id"", ""ChampionId"", ""TeamPosition"", ""Patch"", ""EventType"", ""RefId"", ""Games"", ""SumEventMinute"", ""AggregatedAtUtc"")
            SELECT gen_random_uuid(), c, tp, p, et, r, g, sm, {aggregatedAtUtc}
            FROM unnest({championId}, {teamPosition}, {patch}, {eventType}, {refId}, {games}, {sumMinute})
                AS x(c, tp, p, et, r, g, sm)
            ON CONFLICT (""ChampionId"", ""TeamPosition"", ""Patch"", ""EventType"", ""RefId"")
            DO UPDATE SET
                ""Games"" = champion_powerspike_event_stats.""Games"" + EXCLUDED.""Games"",
                ""SumEventMinute"" = champion_powerspike_event_stats.""SumEventMinute"" + EXCLUDED.""SumEventMinute"",
                ""AggregatedAtUtc"" = EXCLUDED.""AggregatedAtUtc""", ct);
    }
}
