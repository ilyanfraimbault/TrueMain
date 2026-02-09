using Data.Repositories;
using FluentAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

public sealed class MatchSnapshotWriterPerkMappingTests
{
    [Fact]
    public void BuildPerkSelectionRows_ShouldReturnSixRowsForSingleParticipant()
    {
        var match = new RiotMatchDto
        {
            Info = new RiotMatchInfoDto
            {
                Participants =
                [
                    new RiotParticipantDto
                    {
                        ParticipantId = 1,
                        Perks = new RiotPerksDto
                        {
                            Styles =
                            [
                                new RiotPerkStyleDto
                                {
                                    Style = 8000,
                                    Description = "primaryStyle",
                                    Selections =
                                    [
                                        new RiotPerkSelectionDto { Perk = 8005 },
                                        new RiotPerkSelectionDto { Perk = 9111 },
                                        new RiotPerkSelectionDto { Perk = 9104 },
                                        new RiotPerkSelectionDto { Perk = 8014 }
                                    ]
                                },
                                new RiotPerkStyleDto
                                {
                                    Style = 8400,
                                    Description = "subStyle",
                                    Selections =
                                    [
                                        new RiotPerkSelectionDto { Perk = 8429 },
                                        new RiotPerkSelectionDto { Perk = 8451 }
                                    ]
                                }
                            ]
                        }
                    }
                ]
            }
        };

        var rows = MatchSnapshotWriter.BuildPerkSelectionRows(match);

        rows.Should().HaveCount(6);
        rows.Select(row => row.ParticipantId).Should().OnlyContain(id => id == 1);
        rows.Select(row => row.Key).Should().BeEquivalentTo(
        [
            new PerkCatalogKey(8000, 0, 8005, "primaryStyle"),
            new PerkCatalogKey(8000, 1, 9111, "primaryStyle"),
            new PerkCatalogKey(8000, 2, 9104, "primaryStyle"),
            new PerkCatalogKey(8000, 3, 8014, "primaryStyle"),
            new PerkCatalogKey(8400, 0, 8429, "subStyle"),
            new PerkCatalogKey(8400, 1, 8451, "subStyle")
        ]);
    }
}
