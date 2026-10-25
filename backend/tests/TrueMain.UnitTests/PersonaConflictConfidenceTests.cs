using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Personas;

namespace TrueMain.UnitTests;

/// <summary>
/// How sure an open conflict's answer is (#2277): a shared social channel weighs most, a shared
/// reference page less, a social kind listed with different channels counts against.
/// </summary>
public sealed class PersonaConflictConfidenceTests
{
    [Fact]
    public void Evaluate_WeighsSharedSocialChannelsAboveReferencePages()
    {
        (PersonaLinkKind, string)[] claimant =
        [
            (PersonaLinkKind.Twitch, "https://www.twitch.tv/KageErGudd"),
            (PersonaLinkKind.X, "https://x.com/MarkusRosn2"),
            (PersonaLinkKind.DeepLol, "https://www.deeplol.gg/strm/Ros%C3%A9n"),
            (PersonaLinkKind.Leaguepedia, "https://lol.fandom.com/wiki/Rosen_(Markus_Rosen)"),
        ];
        (PersonaLinkKind, string)[] owner =
        [
            (PersonaLinkKind.Twitch, "https://twitch.tv/kageergudd/"),
            (PersonaLinkKind.X, "https://x.com/markusrosn2?lang=en"),
            (PersonaLinkKind.DeepLol, "https://www.deeplol.gg/strm/Rosén"),
            (PersonaLinkKind.Leaguepedia, "https://lol.fandom.com/wiki/Rosen"),
        ];

        var evidence = PersonaConflictConfidence.Evaluate(claimant, owner);

        evidence.SharedSocial.Should().Equal(PersonaLinkKind.Twitch, PersonaLinkKind.X);
        evidence.SharedReferences.Should().Be(1);
        evidence.DifferingSocial.Should().BeEmpty();
        evidence.Score.Should().Be(23);
    }

    [Fact]
    public void Evaluate_CountsASocialKindWithDifferentChannelsAgainst()
    {
        var evidence = PersonaConflictConfidence.Evaluate(
            [(PersonaLinkKind.YouTube, "https://youtube.com/@one"), (PersonaLinkKind.LoLPros, "https://lolpros.gg/player/a")],
            [(PersonaLinkKind.YouTube, "https://youtube.com/@two"), (PersonaLinkKind.LoLPros, "https://lolpros.gg/player/a")]);

        evidence.SharedSocial.Should().BeEmpty();
        evidence.DifferingSocial.Should().Equal(PersonaLinkKind.YouTube);
        evidence.Score.Should().Be(3 - 5);
    }

    [Fact]
    public void Evaluate_ScoresNothingInCommonAsZero()
    {
        PersonaConflictConfidence.Evaluate([(PersonaLinkKind.Kick, "https://kick.com/a")], []).Score.Should().Be(0);
        PersonaConflictConfidence.IsSocial(PersonaLinkKind.Soop).Should().BeTrue();
        PersonaConflictConfidence.IsSocial(PersonaLinkKind.Website).Should().BeFalse();
    }
}
