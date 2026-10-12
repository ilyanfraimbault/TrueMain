namespace Data.Entities;

/// <summary>
/// Lifecycle of a seed request (<see cref="Data.Ops.Mongo.SeedRequestDocument"/>). <see cref="Pending"/> and
/// <see cref="Resolving"/> are the unprocessed states the idempotency check and
/// the Ingestor's claim scan look at; <see cref="Ingested"/> and
/// <see cref="Failed"/> are terminal. Stored as the enum name (a string) so the
/// document is human-readable in the shell.
/// </summary>
public enum SeedRequestStatus
{
    Pending = 0,
    Resolving = 1,
    Ingested = 2,
    Failed = 3
}
