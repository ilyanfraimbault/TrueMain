namespace Data;

/// <summary>
/// Shared database configuration. Bound from the <c>Database</c> configuration
/// section so the API and the Ingestor read the same key. Older compose files
/// already set <c>Database__ApplyMigrationsOnStartup</c>; the previous
/// <c>Migrations:ApplyOnStartup</c> binding silently ignored that variable.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool ApplyMigrationsOnStartup { get; set; }
}
