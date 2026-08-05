namespace Data.Metrics.Mongo;

/// <summary>
/// The storage engines the database panel measures (#1023). Both live on the same
/// volume in every environment, so the disk figure is their sum — the discriminator
/// exists to keep the two series apart, not to suggest they are separate disks.
/// </summary>
public static class StorageEngines
{
    public const string Postgres = "postgres";

    public const string Mongo = "mongo";
}
