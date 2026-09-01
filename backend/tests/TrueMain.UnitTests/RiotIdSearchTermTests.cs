using AwesomeAssertions;
using Core.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// The ops search boxes advertise "Riot ID" (#1340), but the queue and candidate
/// rows store the name and the tag in separate fields, so a term is only usable
/// once it is split. What matters here is that a half-typed term never collapses
/// to "match nothing": the operator is typing, and every intermediate state has
/// to keep returning the rows the previous keystroke returned.
/// </summary>
public sealed class RiotIdSearchTermTests
{
    [Theory]
    [InlineData("Aileri#KR4", "Aileri", "KR4")]
    [InlineData("  Aileri # KR4 ", "Aileri", "KR4")]     // trimmed on both halves
    [InlineData("Aileri", "Aileri", null)]               // no '#': one fragment
    [InlineData("Aileri#", "Aileri", null)]              // mid-typing: name only
    [InlineData("#KR4", null, "KR4")]                    // tag only
    [InlineData("KC next aileri#king", "KC next aileri", "king")]  // spaces are part of the name
    [InlineData("a#b#c", "a", "b#c")]                    // split on the first '#' only
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData(null, null, null)]
    [InlineData("#", null, null)]
    public void Split_SeparatesTheTwoHalves(string? term, string? expectedName, string? expectedTag)
    {
        var (name, tag) = RiotIdSearchTerm.Split(term);

        name.Should().Be(expectedName);
        tag.Should().Be(expectedTag);
    }
}
