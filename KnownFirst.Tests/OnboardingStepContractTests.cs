using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Settings;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingStepContractTests
{
    private static string LoadStepUi(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Ui",
        "Steps",
        fileName));

    private static string LoadUi(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Ui",
        fileName));

    [TestMethod]
    public void DisplayNameStep_ReusesExistingDisplayNameStore_AndSupportsOptionalEmptyName()
    {
        var markup = LoadStepUi("DisplayNameStep.razor");

        Assert.Contains("IDisplayNameStore", markup);
        Assert.Contains("DisplayNamePolicy", markup);
        Assert.Contains("Onboarding_DisplayNameTitle", markup);
        Assert.Contains("id=\"onboarding-display-name-input\"", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void WorkflowStep_ExplainsCoreWorkflowWithoutPersistedSetting()
    {
        var markup = LoadStepUi("WorkflowStep.razor");

        Assert.Contains("Onboarding_WorkflowTitle", markup);
        Assert.Contains("Onboarding_WorkflowStep1", markup);
        Assert.Contains("Onboarding_WorkflowStep2", markup);
        Assert.Contains("Onboarding_WorkflowStep3", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("IPreferences", markup);
        Assert.DoesNotContain("IAppSettingsService", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void OnlineLookupStep_ReusesExistingConsentApi_AndDoesNotGrantConsentOnRenderOrSkip()
    {
        var markup = LoadStepUi("OnlineLookupStep.razor");

        Assert.Contains("IAppSettingsService", markup);
        Assert.Contains("HasOnlineLookupConsent", markup);
        Assert.Contains("GrantOnlineLookupConsent", markup);
        Assert.Contains("RevokeOnlineLookupConsent", markup);
        Assert.Contains("Onboarding_OnlineLookupTitle", markup);
        Assert.Contains("Onboarding_OnlineLookupDescription", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void EnhancedTermRecognitionStep_ReusesExistingEtrSetting()
    {
        var markup = LoadStepUi("EnhancedTermRecognitionStep.razor");

        Assert.Contains("IAppSettingsService", markup);
        Assert.Contains("EnhancedTermRecognitionEnabled", markup);
        Assert.Contains("SetEnhancedTermRecognitionEnabled", markup);
        Assert.Contains("Onboarding_EnhancedTermRecognitionTitle", markup);
        Assert.Contains("Onboarding_EnhancedTermRecognitionDescription", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void PracticeStep_ReusesCardDirectionAndLearningModeContracts()
    {
        var markup = LoadStepUi("PracticeStep.razor");

        Assert.Contains("IAppSettingsService", markup);
        Assert.Contains("CardDirectionPreference", markup);
        Assert.Contains("LearningMode", markup);
        Assert.Contains("SetCardDirection", markup);
        Assert.Contains("SetLearningMode", markup);
        Assert.Contains("Onboarding_PracticeTitle", markup);
        Assert.Contains("Onboarding_PracticeDescription", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void DailyPaceStep_ReusesPreparationLimitPolicyAndPresets()
    {
        var markup = LoadStepUi("DailyPaceStep.razor");

        Assert.Contains("PreparationLimitPolicy", markup);
        Assert.Contains("IAppSettingsService", markup);
        Assert.Contains("SetPreparationLimit", markup);
        Assert.Contains("Onboarding_DailyPaceTitle", markup);
        Assert.Contains("Onboarding_DailyPaceDescription", markup);
        Assert.Contains("Settings_PreparationLimitHighWarning", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void LearningDayTimingStep_ReusesTimezoneCatalogAndCutoff()
    {
        var markup = LoadStepUi("LearningDayTimingStep.razor");

        Assert.Contains("LearningTimezoneCatalog", markup);
        Assert.Contains("IAppSettingsService", markup);
        Assert.Contains("SetLearningDayCutoffMinutes", markup);
        Assert.Contains("Onboarding_LearningDayTimingTitle", markup);
        Assert.Contains("Onboarding_LearningDayTimingDescription", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void SummaryStep_DisplaysPersistedChoicesAndFinishButton()
    {
        var markup = LoadStepUi("SummaryStep.razor");

        Assert.Contains("IAppSettingsService", markup);
        Assert.Contains("IDisplayNameStore", markup);
        Assert.Contains("ILanguageSelectionService", markup);
        Assert.Contains("Onboarding_SummaryTitle", markup);
        Assert.Contains("Onboarding_FinishSetup", markup);
        Assert.Contains("id=\"onboarding-finish-button\"", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void OnboardingHost_RendersAllNineStepsAndHandlesCompletion()
    {
        var markup = LoadUi("OnboardingHost.razor");

        Assert.Contains("DisplayNameStep", markup);
        Assert.Contains("WorkflowStep", markup);
        Assert.Contains("OnlineLookupStep", markup);
        Assert.Contains("EnhancedTermRecognitionStep", markup);
        Assert.Contains("PracticeStep", markup);
        Assert.Contains("DailyPaceStep", markup);
        Assert.Contains("LearningDayTimingStep", markup);
        Assert.Contains("SummaryStep", markup);
        Assert.Contains("OnCompleted", markup);
        Assert.Contains("IOnboardingCompletionService", markup);
        Assert.Contains("CompleteOnboarding", markup);
    }

    [TestMethod]
    public void Routes_PassesCompletionCallbackToOnboardingHost()
    {
        var markup = LoadUi("Routes.razor");

        Assert.Contains("OnboardingHost", markup);
        Assert.Contains("OnCompleted=", markup);
    }
}
