using AwesomeAssertions;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Composition;

namespace TrueMain.UnitTests;

public sealed class CompositionVoteWeightTests
{
    private static readonly CompositionSearchOptions Options = new();

    private static CompositionMatchRef Match(
        bool isCurrentPatch,
        bool isTruemain,
        int score = 0)
        => new()
        {
            MatchId = "EUW1_1",
            ParticipantId = 1,
            Score = score,
            Win = true,
            GameStartTimeUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Puuid = "puuid",
            IsTruemain = isTruemain,
            IsCurrentPatch = isCurrentPatch,
        };

    // The four combinations the product is built from, with similarity left out
    // (maxPossibleScore = 0): a main's game on the current patch votes twelve times as
    // loudly as a stranger's on the previous one, and a main's game on the previous patch
    // still outvotes a stranger's on the current one.
    [Theory]
    [InlineData(true, true, 12d)]
    [InlineData(false, true, 8d)]
    [InlineData(true, false, 3d)]
    [InlineData(false, false, 2d)]
    public void For_multiplies_patch_and_pilot_weights(
        bool isCurrentPatch,
        bool isTruemain,
        double expected)
    {
        var weight = CompositionVoteWeight.For(
            Match(isCurrentPatch, isTruemain), maxPossibleScore: 0, Options);

        weight.Should().Be(expected);
    }

    [Fact]
    public void For_scales_the_product_by_draft_similarity()
    {
        // A perfect draft reproduction is worth 1 + boost (4) on the similarity factor,
        // on top of current patch (3) and main (4).
        var perfect = CompositionVoteWeight.For(
            Match(isCurrentPatch: true, isTruemain: true, score: 20), maxPossibleScore: 20, Options);

        perfect.Should().Be(48d);
    }

    [Fact]
    public void For_leaves_similarity_out_when_no_slot_was_requested()
    {
        // maxPossibleScore = 0 means a slotless draft: every game is equally (un)similar,
        // so patch and pilot decide alone rather than the whole product collapsing.
        var slotless = CompositionVoteWeight.For(
            Match(isCurrentPatch: true, isTruemain: false), maxPossibleScore: 0, Options);

        slotless.Should().Be(Options.CurrentPatchWeight * Options.NonMainGameWeight);
    }

    [Fact]
    public void For_never_silences_a_game()
    {
        // The weakest possible vote — previous patch, non-main, nothing of the draft
        // matched — still counts. A zero would drop the game from the aggregation
        // entirely, which is not what down-weighting means.
        var weakest = CompositionVoteWeight.For(
            Match(isCurrentPatch: false, isTruemain: false), maxPossibleScore: 20, Options);

        weakest.Should().BePositive();
    }
}
