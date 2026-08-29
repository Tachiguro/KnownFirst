using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Data.Schema13;

public sealed record FsrsPersistedReviewHistoryEntry(
    int Id,
    string StableId,
    int CardId,
    int SequenceNumber,
    Fsrs6ReviewEvent Event);
