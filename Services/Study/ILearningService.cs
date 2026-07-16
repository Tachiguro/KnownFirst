using KnownFirst.Core.Learning;
using KnownFirst.Models;

namespace KnownFirst.Services.Study;

public interface ILearningService
{
    Task<LearningLoadResult> GetOrStartAsync();

    Task RevealAnswerAsync(int queueItemId);

    Task<SpellingSubmissionResult> CheckSpellingAsync(int queueItemId, string enteredAnswer);

    Task<LearningLoadResult> RateAsync(int queueItemId, ReviewRating rating);

    Task<bool> MarkPermanentlyKnownAsync(int wordId, bool confirmed);

    Task<int> RunMaintenanceAsync();
}
