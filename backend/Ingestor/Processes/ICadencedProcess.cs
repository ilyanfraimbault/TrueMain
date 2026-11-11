namespace Ingestor.Processes;

/// <summary>
/// A step that runs at most once per <see cref="MinRunInterval"/>, however often its lane
/// passes (#487, #1149). The <see cref="RecordedProcess{TInner}"/> around it applies the guard:
/// it measures from the start of the last run that did its work — never from a skip, so a skip
/// cannot re-arm itself — and records the declined pass as Skipped without calling the step.
/// </summary>
public interface ICadencedProcess : IIngestorProcess
{
    /// <summary>The least time between two runs that do their work; zero runs every pass.</summary>
    TimeSpan MinRunInterval { get; }
}
