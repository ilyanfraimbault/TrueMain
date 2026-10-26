namespace TrueMain.Services.Personas;

/// <summary>
/// What makes two personas homonyms (#2271): the same name once lowercased, spaces, dots, dashes and
/// underscores dropped — <c>Rek'Sai King</c> is not <c>RekSaiKing</c>, but <c>Auxxence</c> is
/// <c>auxxence</c> and <c>ZDR</c> is <c>Z.D.R</c>. Computed in SQL, where every reader of it runs.
/// </summary>
public static partial class PersonaHomonyms
{
    /// <summary>The name key of <c>{alias}."DisplayName"</c>, as a Postgres expression.</summary>
    public static string NameKeySql(string alias) =>
        $"regexp_replace(lower({alias}.\"DisplayName\"), '[[:space:]._-]+', '', 'g')";

    /// <summary>The same name key in C#, for the personas already loaded (#2286).</summary>
    public static string NameKey(string displayName) =>
        NameKeyPattern().Replace(displayName.ToLowerInvariant(), string.Empty);

    /// <summary>
    /// The key two links of the same channel share (#2273): lowercased, without the scheme, a
    /// <c>www.</c> or a trailing slash, as a Postgres expression over <c>{alias}."Url"</c>.
    /// </summary>
    public static string LinkKeySql(string alias) =>
        $"regexp_replace(lower({alias}.\"Url\"), '^https?://(www\\.)?|/+$', '', 'g')";

    /// <summary>The same key in C#, for the links already loaded.</summary>
    public static string LinkKey(string url) =>
        LinkKeyPattern().Replace(url.ToLowerInvariant(), string.Empty);

    /// <summary>
    /// The pair in the order <c>persona_distinct_pairs</c> stores it: by the uuids' canonical text,
    /// which is how Postgres orders uuids (<see cref="Guid.CompareTo(Guid)"/> is not).
    /// </summary>
    public static (Guid Low, Guid High) Ordered(Guid a, Guid b) =>
        string.CompareOrdinal(a.ToString("D"), b.ToString("D")) < 0 ? (a, b) : (b, a);

    [System.Text.RegularExpressions.GeneratedRegex(@"[\s._-]+")]
    private static partial System.Text.RegularExpressions.Regex NameKeyPattern();

    [System.Text.RegularExpressions.GeneratedRegex("^https?://(www\\.)?|/+$")]
    private static partial System.Text.RegularExpressions.Regex LinkKeyPattern();
}
