namespace KnownFirst.Models;

public enum PreparationTimingPhase
{
    Validation = 0,
    DatabaseTransaction = 1,
    PreparedMeaningSave = 2,
    LearningCardCreation = 3,
    SessionUpdate = 4,
    NextCandidateQuery = 5,
    ContextLoading = 6,
    UiTransition = 7,
    NetworkWork = 8
}

public sealed record PreparationTimingMeasurement(
    long Sequence,
    int? CandidateId,
    string Operation,
    PreparationTimingPhase Phase,
    double DurationMilliseconds,
    DateTime RecordedAtUtc);
