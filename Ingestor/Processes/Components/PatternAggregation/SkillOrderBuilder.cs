using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

public static class SkillOrderBuilder
{
    public static string Build(IEnumerable<SkillEvent> skillEvents)
    {
        var basicSkillStates = new Dictionary<int, (int Rank, int LastRankUpAtMs)>
        {
            [1] = (0, int.MaxValue),
            [2] = (0, int.MaxValue),
            [3] = (0, int.MaxValue)
        };
        var sequence = new List<int>(3);

        foreach (var skill in skillEvents
                     .Where(skill => skill.LevelUpType.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(skill => skill.TimestampMs))
        {
            if (!basicSkillStates.TryGetValue(skill.SkillSlot, out var state))
            {
                continue;
            }

            var updatedRank = state.Rank + 1;
            basicSkillStates[skill.SkillSlot] = (updatedRank, skill.TimestampMs);

            if (updatedRank == 2)
            {
                sequence.Add(skill.SkillSlot);
            }
        }

        if (basicSkillStates.Values.All(state => state.Rank == 0))
        {
            return string.Empty;
        }

        var remainingSlots = basicSkillStates.Keys
            .Except(sequence)
            .OrderByDescending(slot => basicSkillStates[slot].Rank)
            .ThenBy(slot => basicSkillStates[slot].LastRankUpAtMs)
            .ThenBy(slot => slot);

        return string.Join("-", sequence
            .Concat(remainingSlots)
            .Select(slot => slot switch
            {
                1 => "Q",
                2 => "W",
                3 => "E",
                _ => slot.ToString()
            }));
    }
}
