namespace KnownFirst.Services.DataSafety.Merge;

internal sealed record MergeWriterExecutionMaps(
    IReadOnlyDictionary<string, int> WordIds,
    IReadOnlyDictionary<string, int> SenseIds,
    IReadOnlyDictionary<string, int> CardIds,
    IReadOnlyDictionary<string, int>? MeaningIds = null,
    IReadOnlyDictionary<string, int>? LearningSessionIds = null);
