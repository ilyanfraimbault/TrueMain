using System.Text.Json;
using AwesomeAssertions;
using Core.Lol.Identifiers;

namespace TrueMain.UnitTests;

public sealed class PuuidTests
{
    // 78 base64url characters — the shape of a real Riot encrypted PUUID.
    private static string ValidPuuid => new('a', 78);

    [Fact]
    public void Parse_accepts_a_valid_puuid()
    {
        var puuid = Puuid.Parse(ValidPuuid);

        puuid.Value.Should().Be(ValidPuuid);
        puuid.ToString().Should().Be(ValidPuuid);
        ((string)puuid).Should().Be(ValidPuuid);
    }

    [Fact]
    public void TryParse_accepts_valid_and_rejects_malformed()
    {
        Puuid.TryParse(ValidPuuid, out var ok).Should().BeTrue();
        ok.Value.Should().Be(ValidPuuid);

        Puuid.TryParse(null, out _).Should().BeFalse();
        Puuid.TryParse("", out _).Should().BeFalse();
        Puuid.TryParse(new string('a', 77), out _).Should().BeFalse();          // too short
        Puuid.TryParse(new string('a', 79), out _).Should().BeFalse();          // too long
        Puuid.TryParse(new string('a', 77) + "!", out _).Should().BeFalse();    // bad charset
    }

    [Fact]
    public void Parse_throws_on_malformed_input()
    {
        var act = () => Puuid.Parse("too-short");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Default_is_invalid_on_both_Value_and_ToString()
    {
        var puuid = default(Puuid);

        var readValue = () => { _ = puuid.Value; };
        var readString = () => { _ = puuid.ToString(); };

        readValue.Should().Throw<InvalidOperationException>();
        readString.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CompareTo_orders_ordinally()
    {
        var a = Puuid.Parse(new string('a', 78));
        var b = Puuid.Parse(new string('b', 78));

        a.CompareTo(b).Should().BeNegative();
        b.CompareTo(a).Should().BePositive();
        a.CompareTo(a).Should().Be(0);
    }

    [Fact]
    public void Json_round_trips_as_a_bare_string()
    {
        var puuid = Puuid.Parse(ValidPuuid);

        var json = JsonSerializer.Serialize(puuid);

        json.Should().Be($"\"{ValidPuuid}\"");
        JsonSerializer.Deserialize<Puuid>(json).Should().Be(puuid);
    }

    [Fact]
    public void Json_rejects_null_rather_than_yielding_the_invalid_default()
    {
        // A null would otherwise deserialize to default(Puuid), whose Value and
        // ToString both throw — the converter rejects it at the boundary.
        var act = () => JsonSerializer.Deserialize<Puuid>("null");

        act.Should().Throw<JsonException>();
    }
}
