using KnownFirst.Core.Settings;

namespace KnownFirst.Services.Onboarding;

public interface IOnboardingCompletionJournalStore
{
    OnboardingCompletionJournalReadResult Read();

    void Save(OnboardingCompletionJournal journal);

    bool SaveVerified(OnboardingCompletionJournal journal);

    void Clear();
}
