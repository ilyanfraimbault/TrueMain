namespace Core.Truemains;

/// <summary>
/// Splits an operator's free-text search term into the two halves of a Riot ID.
/// The ops lists store the game name and the tag line in separate columns, so a
/// term matched as one string can never find a row typed the way players write
/// it (<c>Name#TAG</c>) — the search boxes invite exactly that form.
/// <para>
/// Deliberately not <c>NameTagParser.TryParseRiotId</c> (Api side): that one
/// validates a *complete* Riot ID and fails when a half is missing, while this
/// one describes a term someone is still typing —
/// a bare name, or a name with a partial tag — and never fails. It also lives in
/// Core rather than Api because both the Mongo seed-request store and the EF
/// candidate query need it, and Data cannot reference Api.
/// </para>
/// </summary>
public static class RiotIdSearchTerm
{
    /// <summary>
    /// Returns the name and tag fragments to match. Either can be null, and
    /// both are when the term carries nothing to search on — blank, or a lone
    /// <c>#</c>, which callers read as "no search filter" rather than as a
    /// filter that matches nothing:
    /// <list type="bullet">
    /// <item>no <c>#</c> — the whole term is the name fragment, and callers keep
    /// matching it against the tag too (searching "KR1" still works);</item>
    /// <item><c>Name#TAG</c> — both fragments, and callers require both to
    /// match;</item>
    /// <item><c>Name#</c> — the name alone, so results don't vanish between the
    /// keystroke that types the '#' and the one that types the first tag
    /// character;</item>
    /// <item><c>#TAG</c> — the tag alone.</item>
    /// </list>
    /// A second <c>#</c> stays in the tag fragment: Riot game names cannot
    /// contain one, so the split is on the first.
    /// </summary>
    public static (string? Name, string? Tag) Split(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return (null, null);
        }

        var trimmed = term.Trim();
        var hash = trimmed.IndexOf('#');
        if (hash < 0)
        {
            return (trimmed, null);
        }

        var name = trimmed[..hash].Trim();
        var tag = trimmed[(hash + 1)..].Trim();

        return (name.Length == 0 ? null : name, tag.Length == 0 ? null : tag);
    }
}
