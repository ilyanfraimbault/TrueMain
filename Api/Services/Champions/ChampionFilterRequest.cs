namespace TrueMain.Services.Champions;

/// <summary>
/// Bag of query-string filters for the champions endpoints. The constructor
/// normalises empty / whitespace strings to null so downstream services can
/// rely on <c>!string.IsNullOrWhiteSpace</c>-free branching.
/// </summary>
public readonly record struct ChampionFilterRequest(
    Guid? RiotAccountId,
    string? Patch,
    string? PlatformId,
    string? Position)
{
    public static ChampionFilterRequest Create(
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position)
        => new(
            riotAccountId,
            NullIfEmpty(patch),
            NullIfEmpty(platformId),
            NullIfEmpty(position));

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
