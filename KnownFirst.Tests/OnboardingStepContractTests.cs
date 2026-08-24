using KnownFirst.Core.Settings;
using KnownFirst.Services;
using KnownFirst.Services.Settings;
using Microsoft.Maui.Storage;
using System.Text.RegularExpressions;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingStepContractTests
{
    private static readonly string[] StepFileNames =
    [
        "DisplayNameStep.razor",
        "WorkflowStep.razor",
        "OnlineLookupStep.razor",
        "EnhancedTermRecognitionStep.razor",
        "PracticeStep.razor",
        "DailyPaceStep.razor",
        "LearningDayTimingStep.razor",
        "SummaryStep.razor",
    ];

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
    public void VisualConsistencySliceTwo_DailyPaceUsesBindingOrderSharedStateAndConditionalNumericEditor()
    {
        var markup = LoadStepUi("DailyPaceStep.razor");

        var recommended = markup.IndexOf("id=\"onboarding-daily-pace-preset-5\"", StringComparison.Ordinal);
        var one = markup.IndexOf("id=\"onboarding-daily-pace-preset-1\"", StringComparison.Ordinal);
        var ten = markup.IndexOf("id=\"onboarding-daily-pace-preset-10\"", StringComparison.Ordinal);
        var custom = markup.IndexOf("id=\"onboarding-daily-pace-preset-custom\"", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, recommended);
        Assert.IsGreaterThan(recommended, one, "The recommended value 5 must render before preset 1.");
        Assert.IsGreaterThan(one, ten, "Preset 1 must render before preset 10.");
        Assert.IsGreaterThan(ten, custom, "Preset 10 must render before Custom.");

        Assert.Contains("class=\"choice-button choice-button-recommended @(IsPresetActive(5) ? \"active\" : null)\"", markup);
        Assert.Contains("aria-pressed=\"@IsPresetActive(5)\"", markup);
        Assert.Contains("class=\"choice-button @(IsPresetActive(1) ? \"active\" : null)\"", markup);
        Assert.Contains("aria-pressed=\"@IsPresetActive(1)\"", markup);
        Assert.Contains("class=\"choice-button @(IsPresetActive(10) ? \"active\" : null)\"", markup);
        Assert.Contains("aria-pressed=\"@IsPresetActive(10)\"", markup);
        Assert.Contains("class=\"choice-button @(_isCustomActive ? \"active\" : null)\"", markup);
        Assert.Contains("aria-pressed=\"@_isCustomActive\"", markup);

        var customCondition = markup.IndexOf("@if (_isCustomActive)", StringComparison.Ordinal);
        var customInput = markup.IndexOf("id=\"onboarding-daily-pace-input\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, customCondition);
        Assert.IsGreaterThan(customCondition, customInput, "The numeric editor must exist only inside the Custom-state block.");
        Assert.Contains("class=\"text-input\"", markup);
        Assert.Contains("type=\"number\"", markup);
        Assert.Contains("min=\"1\"", markup);
        Assert.Contains("max=\"50\"", markup);
        Assert.Contains("step=\"1\"", markup);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(5, false)]
    [DataRow(10, false)]
    [DataRow(20, true)]
    public void VisualConsistencySliceTwo_DailyPaceCanonicalizesCustomOnlyWhenContinueCommits(
        int committedValue,
        bool remainsCustom)
    {
        var markup = LoadStepUi("DailyPaceStep.razor");
        var inputChanged = ExtractMethodBody(markup, "private void OnCustomInputChanged()");
        var handleContinue = ExtractMethodBody(markup, "private async Task HandleContinue()");

        Assert.AreEqual(remainsCustom, !PreparationLimitPolicy.IsPreset(committedValue));
        Assert.Contains("@bind:event=\"oninput\"", markup);
        Assert.Contains("@bind:after=\"OnCustomInputChanged\"", markup);
        Assert.DoesNotContain("SetPreparationLimit", inputChanged, StringComparison.Ordinal);
        Assert.DoesNotContain("PreparationLimitPolicy.IsPreset", inputChanged, StringComparison.Ordinal);
        Assert.Contains("AppSettings.SetPreparationLimit", handleContinue);
        Assert.Contains("PreparationLimitPolicy.IsPreset", handleContinue);
        Assert.Contains("OnContinue.InvokeAsync", handleContinue);
    }

    [TestMethod]
    public void VisualConsistencySliceTwo_DailyPaceHighBudgetWarningIsPolicyDrivenAndNonBlocking()
    {
        var markup = LoadStepUi("DailyPaceStep.razor");
        var warningStart = markup.IndexOf("private bool ShowHighLimitWarning", StringComparison.Ordinal);
        var selectPresetStart = markup.IndexOf("private void SelectPreset", warningStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, warningStart);
        Assert.IsGreaterThan(warningStart, selectPresetStart);
        var warningProperty = markup[warningStart..selectPresetStart];
        var handleContinue = ExtractMethodBody(markup, "private async Task HandleContinue()");

        Assert.Contains("PreparationLimitPolicy.IsValid", warningProperty);
        Assert.Contains("PreparationLimitPolicy.RequiresHighBudgetWarning", warningProperty);
        Assert.DoesNotContain("RequiresHighBudgetWarning", handleContinue, StringComparison.Ordinal);
        Assert.IsFalse(PreparationLimitPolicy.RequiresHighBudgetWarning(15));
        Assert.IsTrue(PreparationLimitPolicy.RequiresHighBudgetWarning(16));
        Assert.IsTrue(PreparationLimitPolicy.RequiresHighBudgetWarning(50));
        Assert.IsFalse(PreparationLimitPolicy.RequiresHighBudgetWarning(51));
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
    public void VisualConsistencySliceThree_OnboardingUsesGlobalThemeAndSpacingWithoutOwningSharedControls()
    {
        var styles = LoadUi("OnboardingHost.razor.css").Replace("\r\n", "\n", StringComparison.Ordinal);
        var sharedStyles = LoadUi("app.css").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("--kf-color-", styles, StringComparison.Ordinal);
        Assert.Contains("var(--color-background)", styles);
        Assert.Contains("var(--color-surface)", styles);
        Assert.Contains("var(--color-text)", styles);
        Assert.Contains("var(--color-muted)", styles);
        Assert.Contains("var(--color-border)", styles);
        Assert.Contains("var(--color-primary)", styles);

        Assert.Contains(".onboarding-container ::deep .onboarding-step", styles);
        Assert.Contains(".onboarding-container ::deep .onboarding-actions", styles);
        Assert.Contains("padding: var(--space-8) var(--space-6)", styles);
        Assert.Contains("gap: var(--space-8)", styles);
        Assert.Contains("gap: var(--space-6)", styles);
        Assert.Contains("gap: var(--space-3)", styles);
        var literalSpacing = Regex.Match(
            styles,
            @"(?:gap|margin(?:-[a-z]+)?|padding(?:-[a-z]+)?)\s*:\s*[^;]*(?:0\.75|1|1\.25|1\.5|2)rem");
        Assert.IsFalse(literalSpacing.Success, "Onboarding layout spacing must use the shared --space-* scale: " + literalSpacing.Value);

        foreach (var sharedOwner in new[] { ".button {", ".choice-button {", ".text-input {", ".field-group {" })
        {
            Assert.Contains("\n" + sharedOwner, sharedStyles);
            Assert.DoesNotContain("\n" + sharedOwner, styles);
        }

        Assert.DoesNotContain("min-height: 2.75rem", styles, StringComparison.Ordinal);
    }

    [TestMethod]
    public void VisualConsistencySliceThree_EveryOnboardingButtonUsesSharedSemanticOrChoiceStyling()
    {
        var sources = StepFileNames
            .Select(fileName => (Name: fileName, Markup: LoadStepUi(fileName)))
            .Append(("OnboardingHost.razor", LoadUi("OnboardingHost.razor")));

        var actionButtonCount = 0;
        foreach (var (name, markup) in sources)
        {
            foreach (Match match in Regex.Matches(markup, @"<button\b[\s\S]*?</button>"))
            {
                var button = match.Value;
                if (button.Contains("choice-button", StringComparison.Ordinal))
                {
                    Assert.DoesNotContain("class=\"button choice-button", button, StringComparison.Ordinal);
                    continue;
                }

                actionButtonCount++;
                var usesSemanticButton = button.Contains("class=\"button button-primary", StringComparison.Ordinal)
                    || button.Contains("class=\"button button-secondary", StringComparison.Ordinal)
                    || button.Contains("class=\"button button-danger", StringComparison.Ordinal);
                Assert.IsTrue(
                    usesSemanticButton,
                    $"{name} contains an action button without a shared semantic .button variant: {button}");
            }
        }

        Assert.IsGreaterThan(0, actionButtonCount);
    }

    [TestMethod]
    public void VisualConsistencySliceThree_AllChoicesUseSharedActiveStateAndAriaPressed()
    {
        var onboardingMarkup = string.Join(
            Environment.NewLine,
            StepFileNames.Select(LoadStepUi).Append(LoadUi("OnboardingHost.razor")));

        Assert.DoesNotContain("choice-button-active", onboardingMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("choice-button-idle", onboardingMarkup, StringComparison.Ordinal);

        var choices = Regex.Matches(onboardingMarkup, @"<button\b[\s\S]*?</button>")
            .Cast<Match>()
            .Where(match => match.Value.Contains("choice-button", StringComparison.Ordinal))
            .ToArray();
        Assert.IsGreaterThan(0, choices.Length);
        foreach (var choice in choices)
        {
            Assert.Contains("aria-pressed=", choice.Value);
            Assert.Contains("\"active\"", choice.Value);
        }
    }

    [TestMethod]
    public void VisualConsistencySliceThree_FieldsReuseSharedInputTreatmentAndNativeTimezoneSelect()
    {
        var displayName = LoadStepUi("DisplayNameStep.razor");
        var dailyPace = LoadStepUi("DailyPaceStep.razor");
        var learningDay = LoadStepUi("LearningDayTimingStep.razor");

        Assert.Contains("class=\"field-group\"", displayName);
        Assert.Contains("class=\"text-input\"", displayName);
        Assert.Contains("class=\"field-group\"", dailyPace);
        Assert.Contains("class=\"text-input\"", dailyPace);
        Assert.Contains("<select id=\"onboarding-learning-timezone-select\"", learningDay);
        Assert.Contains("class=\"field-group\"", learningDay);
        Assert.DoesNotContain("custom-dropdown", learningDay, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void VisualConsistencySliceThree_OnlineConsentUsesPrimaryGrantAndConfirmedDangerRevocation()
    {
        var markup = LoadStepUi("OnlineLookupStep.razor");

        Assert.Contains("if (AppSettings.HasOnlineLookupConsent)", markup);
        Assert.Contains("id=\"onboarding-online-consent-activate-button\"", markup);
        Assert.Contains("class=\"button button-primary\"", markup);
        Assert.Contains("@onclick=\"ActivateOnlineConsent\"", markup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-button\"", markup);
        Assert.Contains("class=\"button button-danger\"", markup);
        Assert.Contains("@onclick=\"ShowOnlineConsentRevokeConfirmation\"", markup);
        Assert.Contains("if (!_showOnlineConsentRevokeConfirmation)", markup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-confirmation\"", markup);
        Assert.Contains("class=\"destructive-confirmation\"", markup);
        Assert.Contains("Settings_RevokeOnlineConsentConfirmMessage", markup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-cancel-button\"", markup);
        Assert.Contains("class=\"button button-secondary\"", markup);
        Assert.Contains("@onclick=\"CancelOnlineConsentRevocation\"", markup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-confirm-button\"", markup);
        Assert.Contains("@onclick=\"ConfirmOnlineConsentRevocation\"", markup);

        var showHandler = ExtractMethodBody(markup, "private void ShowOnlineConsentRevokeConfirmation()");
        var cancelHandler = ExtractMethodBody(markup, "private void CancelOnlineConsentRevocation()");
        var confirmHandler = ExtractMethodBody(markup, "private void ConfirmOnlineConsentRevocation()");
        var activateHandler = ExtractMethodBody(markup, "private void ActivateOnlineConsent()");

        Assert.DoesNotContain("RevokeOnlineLookupConsent", showHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeOnlineLookupConsent", cancelHandler, StringComparison.Ordinal);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = true;", showHandler);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = false;", cancelHandler);
        Assert.Contains("AppSettings.RevokeOnlineLookupConsent();", confirmHandler);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = false;", confirmHandler);
        Assert.Contains("AppSettings.GrantOnlineLookupConsent();", activateHandler);
        Assert.AreEqual(1, Regex.Matches(markup, "GrantOnlineLookupConsent").Count);
        Assert.AreEqual(1, Regex.Matches(markup, "RevokeOnlineLookupConsent").Count);

        var continueButtonStart = markup.IndexOf("id=\"onboarding-online-lookup-continue-button\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, continueButtonStart);
        var continueButtonEnd = markup.IndexOf("</button>", continueButtonStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(continueButtonStart, continueButtonEnd);
        var continueButton = markup[continueButtonStart..continueButtonEnd];
        Assert.Contains("@onclick=\"OnContinue\"", continueButton);
        Assert.DoesNotContain("GrantOnlineLookupConsent", continueButton, StringComparison.Ordinal);
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

    private static string ExtractMethodBody(string markup, string signature)
    {
        var start = markup.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, "The method '" + signature + "' is missing.");

        var braceStart = markup.IndexOf('{', start);
        Assert.IsGreaterThan(start, braceStart);

        var depth = 0;
        for (var index = braceStart; index < markup.Length; index++)
        {
            if (markup[index] == '{')
            {
                depth++;
            }
            else if (markup[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return markup[braceStart..(index + 1)];
                }
            }
        }

        Assert.Fail("The method '" + signature + "' is not correctly delimited.");
        return string.Empty;
    }
}
