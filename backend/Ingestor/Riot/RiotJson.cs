using System.Text.Json;
using System.Text.Json.Serialization;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

/// <summary>
/// Source-generated metadata for every payload the ingestor reads from Riot.
/// Only the root types the clients ask for need registering — the generator walks
/// each root's graph and emits metadata for the nested DTOs too.
/// </summary>
/// <remarks>
/// The resolver is source-gen only, so a root type that is not registered fails
/// with <see cref="NotSupportedException"/> at deserialization time rather than
/// silently falling back to reflection. <c>RiotJsonContextTests</c> asserts every
/// root the clients use is present.
/// </remarks>
[JsonSerializable(typeof(RiotAccountDto))]
[JsonSerializable(typeof(RiotSummonerDto))]
[JsonSerializable(typeof(RiotLeagueListDto))]
[JsonSerializable(typeof(List<RiotLeagueEntryByPuuidDto>))]
[JsonSerializable(typeof(List<RiotChampionMasteryDto>))]
[JsonSerializable(typeof(RiotMatchDto))]
[JsonSerializable(typeof(RiotTimelineDto))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class RiotJsonContext : JsonSerializerContext;

internal static class RiotJson
{
    /// <summary>
    /// Riot deserialization options: <see cref="JsonSerializerDefaults.Web"/>
    /// (camelCase, case-insensitive, numbers readable from strings) exactly as
    /// before, with the reflection-based resolver swapped for the generated one.
    /// </summary>
    /// <remarks>
    /// A fresh instance is required — <see cref="JsonSerializerOptions.Web"/> is a
    /// frozen singleton and rejects a <see cref="JsonSerializerOptions.TypeInfoResolver"/>
    /// assignment. Keeping the Web defaults keeps the casing contract identical:
    /// every Riot DTO already carries an explicit <c>[JsonPropertyName]</c> matching
    /// Riot's wire casing, so nothing relies on the case-insensitive fallback (#254),
    /// but dropping it is a behaviour change and stays out of this PR.
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = RiotJsonContext.Default
    };
}
