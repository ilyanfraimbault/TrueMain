using System.Text.Json.Serialization;

namespace Data.ItemContext;

/// <summary>
/// One situation that measurably moves an item's pick rate, as stored on a verdict and
/// served to the page (#1450). Everything a sentence needs is here, counts included:
/// "built in 62% of games against a magic-damage team, 21% otherwise — 1 430 games".
/// </summary>
/// <remarks>
/// The <see cref="JsonPropertyNameAttribute"/> on every member pins the on-disk key
/// names of the <c>jsonb</c> column, exactly like <c>ItemEvent</c> does: a property
/// rename must not silently orphan every row already written. The two enums are
/// written as strings for the same reason the columns beside them are: this is a row an
/// operator reads in psql while asking why a card says what it says.
/// </remarks>
public sealed class ItemContextAxisFinding
{
    /// <summary>The situation.</summary>
    [JsonPropertyName("Axis")]
    [JsonConverter(typeof(JsonStringEnumConverter<ItemContextAxis>))]
    public ItemContextAxis Axis { get; set; }

    /// <summary>
    /// The end of the axis where the item is picked <em>more</em>. Always the higher of
    /// the two rates, so a sentence never has to invert itself: an item built against
    /// melee teams reads as High on <see cref="ItemContextAxis.EnemyMelee"/>, one built
    /// against ranged teams reads as Low on the same axis.
    /// </summary>
    [JsonPropertyName("Bucket")]
    [JsonConverter(typeof(JsonStringEnumConverter<ItemContextBucket>))]
    public ItemContextBucket Bucket { get; set; }

    /// <summary>Games in that bucket where the item was built.</summary>
    [JsonPropertyName("GamesIn")]
    public int GamesIn { get; set; }

    /// <summary>Games in that bucket, built or not — the denominator of <see cref="RateIn"/>.</summary>
    [JsonPropertyName("TotalIn")]
    public int TotalIn { get; set; }

    /// <summary>Games at the opposite end where the item was built.</summary>
    [JsonPropertyName("GamesOut")]
    public int GamesOut { get; set; }

    /// <summary>Games at the opposite end, built or not.</summary>
    [JsonPropertyName("TotalOut")]
    public int TotalOut { get; set; }

    /// <summary>
    /// <see cref="GamesIn"/>/<see cref="TotalIn"/> minus <see cref="GamesOut"/>/<see cref="TotalOut"/>,
    /// always positive by construction of <see cref="Bucket"/>.
    /// </summary>
    [JsonPropertyName("Lift")]
    public double Lift { get; set; }

    /// <summary>The |z| of the two-proportion test the finding cleared. Kept so a reader can see how hard it cleared it.</summary>
    [JsonPropertyName("Z")]
    public double Z { get; set; }

    /// <summary>
    /// How many patches this finding was folded over — 1 when the served patch was deep
    /// enough on its own. Both ends always share the same window, so the two rates stay
    /// comparable; the sentence prints it, because "over the last three patches" is a
    /// different claim from "this patch".
    /// </summary>
    [JsonPropertyName("PatchWindow")]
    public int PatchWindow { get; set; } = 1;

    /// <summary>The pick rate inside the bucket.</summary>
    [JsonIgnore]
    public double RateIn => TotalIn > 0 ? GamesIn / (double)TotalIn : 0d;

    /// <summary>The pick rate at the opposite end.</summary>
    [JsonIgnore]
    public double RateOut => TotalOut > 0 ? GamesOut / (double)TotalOut : 0d;
}
