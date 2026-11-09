namespace TrueMain.ReadModels.Ops;

/// <summary>Which ingestion arm selects a tracked account.</summary>
public enum AccountTrackingArm
{
    EstablishedMain,
    QueuedCandidate,
    Both
}
