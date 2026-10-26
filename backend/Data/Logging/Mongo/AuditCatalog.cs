namespace Data.Logging.Mongo;

/// <summary>
/// Every action name an <see cref="IAuditLog"/> writer records, with the plain words the admin
/// Audit page shows for it (#2288). One table for the Api and the Ingestor: a writer passes one of
/// these constants, so a new action without its wording fails <c>AuditCatalogTests</c>.
/// </summary>
public static class AuditActions
{
    public const string CreatePersona = "create_persona";
    public const string RenamePersona = "rename_persona";
    public const string AddPersonaLink = "add_persona_link";
    public const string RemovePersonaLink = "remove_persona_link";
    public const string AddPersonaAccount = "add_persona_account";
    public const string UnlinkPersonaAccount = "unlink_persona_account";
    public const string RemovePersona = "remove_persona";
    public const string ResolvePersonaConflict = "resolve_persona_conflict";
    public const string MergePersonas = "merge_personas";
    public const string MarkPersonasDistinct = "mark_personas_distinct";
    public const string SeedAccount = "seed_account";
    public const string SeedAccountIngested = "seed_account_ingested";

    /// <summary>The wording of each action, in the order the Audit page's filter lists them.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Labels =
    [
        new(CreatePersona, "Added a known player"),
        new(RenamePersona, "Renamed a known player"),
        new(AddPersonaAccount, "Linked an account"),
        new(UnlinkPersonaAccount, "Unlinked an account"),
        new(AddPersonaLink, "Added a link"),
        new(RemovePersonaLink, "Removed a link"),
        new(RemovePersona, "Removed a known player"),
        new(ResolvePersonaConflict, "Resolved a conflict"),
        new(MergePersonas, "Merged known players"),
        new(MarkPersonasDistinct, "Marked as different people"),
        new(SeedAccount, "Asked to seed a main"),
        new(SeedAccountIngested, "Seeded a main"),
    ];

    /// <summary>The plain words of <paramref name="action"/>, or the name itself when it has none.</summary>
    public static string Label(string action) =>
        Labels.FirstOrDefault(pair => pair.Key == action).Value ?? action;
}

/// <summary>Who an audit event says acted: an operator, an automatic sweep, or the ingestor.</summary>
public static class AuditActors
{
    public const string Operator = "operator";
    public const string Auto = "auto";
    public const string Ingestor = "ingestor";

    public static readonly IReadOnlyList<string> All = [Operator, Auto, Ingestor];
}

/// <summary>The kinds of entity an audit event targets.</summary>
public static class AuditTargets
{
    public const string Persona = "Persona";
    public const string PersonaAccountConflict = "PersonaAccountConflict";
    public const string SeedRequest = "SeedRequest";

    public static readonly IReadOnlyList<string> All = [Persona, PersonaAccountConflict, SeedRequest];
}
