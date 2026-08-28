using KnownFirst.Core.Settings;

namespace KnownFirst.Services.Onboarding;

public interface IOnboardingDraftStore
{
    OnboardingDraftReadResult Read();

    void Save(OnboardingDraft draft);

    void Clear();
}
