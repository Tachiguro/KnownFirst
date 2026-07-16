using KnownFirst.Models;

namespace KnownFirst.Services;

public interface ITextReviewService
{
    Task<ImportAnalysisResult> ImportAsync(ImportTextRequest request);

    Task<ActiveReviewSummary?> GetActiveReviewAsync();

    Task<ReviewCandidateDetails?> GetCurrentCandidateAsync();

    Task<ReviewDecisionResult> DecideAsync(int wordId, WordStatus status);

    Task<bool> UndoPreviousDecisionAsync();

    Task DiscardActiveImportAsync();

    Task<CompletedReviewSummary?> GetLatestCompletedReviewAsync();

    Task<ReviewDiagnosticsSnapshot> GetDiagnosticsAsync();
}
