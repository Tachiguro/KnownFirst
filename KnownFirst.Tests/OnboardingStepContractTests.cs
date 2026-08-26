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
        Assert.Contains("Onboarding_DisplayNameSkip", markup);
        Assert.Contains("Common_Continue", markup);
        Assert.Contains("string.IsNullOrWhiteSpace(_nameInput)", markup);
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
    public void OnlineLookupStep_ReusesExistingConsentApi_AndRequiresExplicitDecisionBeforeProgression()
    {
        var markup = LoadStepUi("OnlineLookupStep.razor");

        Assert.Contains("IAppSettingsService", markup);
        Assert.Contains("HasOnlineLookupConsent", markup);
        Assert.Contains("GrantOnlineLookupConsent", markup);
        Assert.Contains("RevokeOnlineLookupConsent", markup);
        Assert.Contains("Onboarding_OnlineLookupTitle", markup);
        Assert.Contains("Onboarding_OnlineLookupDescription", markup);
        Assert.Contains("Onboarding_OnlineLookupServiceWiktionary", markup);
        Assert.Contains("Onboarding_OnlineLookupServiceWikipedia", markup);
        Assert.Contains("Onboarding_OnlineLookupPrivacyNotice", markup);
        Assert.Contains("id=\"onboarding-online-consent-enable-button\"", markup);
        Assert.Contains("id=\"onboarding-online-consent-disable-button\"", markup);
        Assert.Contains("disabled=\"@(_explicitConsentChoice is null)\"", markup);
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
        Assert.Contains("Settings_CardDirectionHelp", markup);
        Assert.Contains("Settings_LearningModeHelp", markup);
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
        Assert.Contains("Onboarding_SummarySettingsNotice", markup);
        Assert.Contains("Onboarding_FinishSetup", markup);
        Assert.Contains("id=\"onboarding-finish-button\"", markup);
        Assert.Contains("<h1", markup);
        Assert.DoesNotContain("NavMenu", markup);
        Assert.DoesNotContain("WhatsNewModal", markup);
    }

    [TestMethod]
    public void VisualConsistencySliceFour_WelcomeOffersSystemLanguageAndAppearanceThroughExistingServices()
    {
        var markup = LoadUi("OnboardingHost.razor");

        Assert.Contains("<select id=\"onboarding-ui-language-select\"", markup);
        Assert.Contains("LanguagePreferencePolicy.UiLanguageOptions", markup);
        Assert.Contains("<label for=\"onboarding-ui-language-select\"", markup);
        Assert.DoesNotContain("id=\"onboarding-lang-system\"", markup);

        Assert.Contains("@inject IDeviceCultureProvider DeviceCultureProvider", markup);
        Assert.Contains("DeviceCultureProvider.GetDeviceCultureName()", markup);
        Assert.Contains("LanguagePreferencePolicy.ClassifyDeviceCulture", markup);
        Assert.Contains("@if (LanguageSelection.IsSystemPreferenceActive)", markup);
        Assert.Contains("Onboarding_SystemLanguageDetected", markup);
        Assert.Contains("@if (!_deviceLanguageClassification.IsSupported)", markup);
        Assert.Contains("Onboarding_SystemLanguageUnsupported", markup);

        var languageSection = markup.IndexOf("class=\"onboarding-language-section\"", StringComparison.Ordinal);
        var appearanceSection = markup.IndexOf("class=\"onboarding-appearance-section\"", StringComparison.Ordinal);
        var continueButton = markup.IndexOf("id=\"onboarding-continue-button\"", StringComparison.Ordinal);
        Assert.IsGreaterThan(languageSection, appearanceSection, "Appearance must follow Language in the existing Welcome step.");
        Assert.IsGreaterThan(appearanceSection, continueButton, "Appearance must remain inside Welcome before Continue.");

        var systemTheme = markup.IndexOf("id=\"onboarding-theme-system\"", StringComparison.Ordinal);
        var lightTheme = markup.IndexOf("id=\"onboarding-theme-light\"", StringComparison.Ordinal);
        var darkTheme = markup.IndexOf("id=\"onboarding-theme-dark\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, systemTheme);
        Assert.IsGreaterThan(systemTheme, lightTheme);
        Assert.IsGreaterThan(lightTheme, darkTheme);
        Assert.Contains("@inject IThemeService ThemeService", markup);
        Assert.Contains("ThemeService.Preference == ThemePreference.System", markup);
        Assert.Contains("ThemeService.Preference == ThemePreference.Light", markup);
        Assert.Contains("ThemeService.Preference == ThemePreference.Dark", markup);
        Assert.Contains("ThemeService.SetPreference(preference)", markup);

        Assert.Contains("class=\"choice-grid", markup);
        Assert.Contains("class=\"choice-button", markup);
        Assert.Contains("aria-pressed=", markup);
        Assert.DoesNotContain("AppearanceStep", markup, StringComparison.Ordinal);
    }

    [TestMethod]
    public void VisualConsistencySliceFour_SummaryReportsPreferenceModesInsteadOfEffectiveValues()
    {
        var markup = LoadStepUi("SummaryStep.razor");

        Assert.Contains("LanguageSelection.IsSystemPreferenceActive", markup);
        Assert.Contains("Settings_UILanguageSystem", markup);
        Assert.Contains("LanguageSelection.CurrentUiLanguage", markup);
        Assert.Contains("@inject IThemeService ThemeService", markup);
        Assert.Contains("ThemeService.Preference", markup);
        Assert.Contains("Settings_Appearance", markup);
        Assert.Contains("Settings_AppearanceSystem", markup);
        Assert.Contains("Settings_AppearanceLight", markup);
        Assert.Contains("Settings_AppearanceDark", markup);
        Assert.DoesNotContain("EffectiveTheme", markup, StringComparison.Ordinal);
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
    public void VisualConsistencySliceThree_OnlineConsentUsesExplicitChoicesAndConfirmedDangerRevocation()
    {
        var markup = LoadStepUi("OnlineLookupStep.razor");

        Assert.Contains("id=\"onboarding-online-consent-enable-button\"", markup);
        Assert.Contains("id=\"onboarding-online-consent-disable-button\"", markup);
        Assert.Contains("class=\"choice-button", markup);
        Assert.Contains("aria-pressed=", markup);
        Assert.Contains("disabled=\"@(_explicitConsentChoice is null)\"", markup);

        Assert.Contains("id=\"onboarding-online-consent-revoke-confirmation\"", markup);
        Assert.Contains("class=\"destructive-confirmation\"", markup);
        Assert.Contains("Settings_RevokeOnlineConsentConfirmMessage", markup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-cancel-button\"", markup);
        Assert.Contains("class=\"button button-secondary\"", markup);
        Assert.Contains("@onclick=\"CancelOnlineConsentRevocation\"", markup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-confirm-button\"", markup);
        Assert.Contains("@onclick=\"ConfirmOnlineConsentRevocation\"", markup);

        var cancelHandler = ExtractMethodBody(markup, "private void CancelOnlineConsentRevocation()");
        var confirmHandler = ExtractMethodBody(markup, "private void ConfirmOnlineConsentRevocation()");
        var enableHandler = ExtractMethodBody(markup, "private void EnableOnlineConsent()");

        Assert.DoesNotContain("RevokeOnlineLookupConsent", cancelHandler, StringComparison.Ordinal);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = false;", cancelHandler);
        Assert.Contains("AppSettings.RevokeOnlineLookupConsent();", confirmHandler);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = false;", confirmHandler);
        Assert.Contains("_explicitConsentChoice = false;", confirmHandler);
        Assert.Contains("AppSettings.GrantOnlineLookupConsent();", enableHandler);
        Assert.Contains("_explicitConsentChoice = true;", enableHandler);

        var continueButtonStart = markup.IndexOf("id=\"onboarding-online-lookup-continue-button\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, continueButtonStart);
        var continueButtonEnd = markup.IndexOf("</button>", continueButtonStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(continueButtonStart, continueButtonEnd);
        var continueButton = markup[continueButtonStart..continueButtonEnd];
        Assert.Contains("@onclick=\"OnContinue\"", continueButton);
        Assert.Contains("disabled=\"@(_explicitConsentChoice is null)\"", continueButton);
    }

    [TestMethod]
    public void VisualConsistencyPostReviewConsentFocus_OnboardingRevocationUsesPostRenderFocusLifecycle()
    {
        var markup = LoadStepUi("OnlineLookupStep.razor");

        Assert.Contains("@ref=\"_onlineConsentRevokeButton\"", markup);
        Assert.Contains("@ref=\"_cancelOnlineConsentRevokeButton\"", markup);
        Assert.Contains("private ElementReference _onlineConsentRevokeButton;", markup);
        Assert.Contains("private ElementReference _cancelOnlineConsentRevokeButton;", markup);

        var showHandler = ExtractMethodBody(markup, "private void ShowOnlineConsentRevokeConfirmation()");
        var cancelHandler = ExtractMethodBody(markup, "private void CancelOnlineConsentRevocation()");
        var afterRenderHandler = ExtractMethodBody(markup, "protected override async Task OnAfterRenderAsync(bool firstRender)");

        Assert.Contains("_showOnlineConsentRevokeConfirmation = true;", showHandler);
        Assert.Contains("_revealOnlineConsentRevokeConfirmation = true;", showHandler);
        Assert.DoesNotContain("FocusAsync", showHandler, StringComparison.Ordinal);
        Assert.Contains("_showOnlineConsentRevokeConfirmation = false;", cancelHandler);
        Assert.Contains("_returnFocusToOnlineConsentRevokeButton = true;", cancelHandler);
        Assert.DoesNotContain("FocusAsync", cancelHandler, StringComparison.Ordinal);
        Assert.Contains("_revealOnlineConsentRevokeConfirmation = false;", afterRenderHandler);
        Assert.Contains("await _cancelOnlineConsentRevokeButton.FocusAsync(preventScroll: true);", afterRenderHandler);
        Assert.Contains("_returnFocusToOnlineConsentRevokeButton = false;", afterRenderHandler);
        Assert.Contains("await _onlineConsentRevokeButton.FocusAsync();", afterRenderHandler);
    }

    [TestMethod]
    public void VisualConsistencyPostReviewConsentFocus_EscapeUsesNonDestructiveCancelAndPreservesAlertDialog()
    {
        var markup = LoadStepUi("OnlineLookupStep.razor");

        Assert.Contains("role=\"alertdialog\"", markup);
        Assert.Contains("aria-labelledby=\"onboarding-online-consent-revoke-confirmation-message\"", markup);
        Assert.Contains("@onkeydown=\"HandleOnlineConsentRevokeDialogKeyDown\"", markup);

        var showHandler = ExtractMethodBody(markup, "private void ShowOnlineConsentRevokeConfirmation()");
        var cancelHandler = ExtractMethodBody(markup, "private void CancelOnlineConsentRevocation()");
        var escapeHandler = ExtractMethodBody(markup, "private void HandleOnlineConsentRevokeDialogKeyDown(KeyboardEventArgs eventArgs)");
        var confirmHandler = ExtractMethodBody(markup, "private void ConfirmOnlineConsentRevocation()");

        Assert.DoesNotContain("RevokeOnlineLookupConsent", showHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeOnlineLookupConsent", cancelHandler, StringComparison.Ordinal);
        Assert.Contains("eventArgs.Key == \"Escape\"", escapeHandler);
        Assert.Contains("CancelOnlineConsentRevocation();", escapeHandler);
        Assert.DoesNotContain("RevokeOnlineLookupConsent", escapeHandler, StringComparison.Ordinal);
        Assert.Contains("AppSettings.RevokeOnlineLookupConsent();", confirmHandler);
        Assert.AreEqual(1, Regex.Matches(markup, "RevokeOnlineLookupConsent").Count);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_OnboardingFieldsHaveExplicitAccessibleNames()
    {
        var displayName = LoadStepUi("DisplayNameStep.razor");
        var learningDay = LoadStepUi("LearningDayTimingStep.razor");

        Assert.Contains("<label for=\"onboarding-display-name-input\"", displayName);
        Assert.Contains("Settings_DisplayName", displayName);
        Assert.Contains("id=\"onboarding-display-name-input\"", displayName);

        Assert.Contains("id=\"onboarding-learning-timezone-select\"", learningDay);
        Assert.Contains("aria-labelledby=\"onboarding-timezone-title\"", learningDay);
        Assert.Contains("aria-describedby=\"onboarding-timezone-help\"", learningDay);
        Assert.Contains("aria-label=\"@Localizer[\"Settings_LearningDayCutoffHours\"]\"", learningDay);
        Assert.Contains("aria-label=\"@Localizer[\"Settings_LearningDayCutoffMinutes\"]\"", learningDay);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_SystemLanguageUsesOnePoliteStatusAndAdvisorySeverity()
    {
        var markup = LoadUi("OnboardingHost.razor");
        var systemStatusStart = markup.IndexOf("id=\"onboarding-system-language-status\"", StringComparison.Ordinal);
        var appearanceStart = markup.IndexOf("class=\"onboarding-appearance-section\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, systemStatusStart);
        Assert.IsGreaterThan(systemStatusStart, appearanceStart);
        var systemStatus = markup[systemStatusStart..appearanceStart];

        Assert.Contains("role=\"status\"", systemStatus);
        Assert.Contains("aria-live=\"polite\"", systemStatus);
        Assert.AreEqual(1, Regex.Matches(systemStatus, "role=\\\"status\\\"").Count);
        Assert.Contains("id=\"onboarding-system-language-fallback\"", systemStatus);
        Assert.Contains("setting-feedback-advisory", systemStatus);
        Assert.DoesNotContain("role=\"alert\"", systemStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-live=\"assertive\"", systemStatus, StringComparison.Ordinal);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_ConfirmationAndChoiceSemanticsRemainDeterministic()
    {
        var onboardingMarkup = string.Join(
            Environment.NewLine,
            StepFileNames.Select(LoadStepUi).Append(LoadUi("OnboardingHost.razor")));
        var onlineLookup = LoadStepUi("OnlineLookupStep.razor");

        foreach (Match choice in Regex.Matches(onboardingMarkup, @"<button\b[\s\S]*?</button>"))
        {
            if (choice.Value.Contains("choice-button", StringComparison.Ordinal))
            {
                Assert.Contains("aria-pressed=", choice.Value);
                Assert.Contains("type=\"button\"", choice.Value);
            }
        }

        Assert.Contains("role=\"alertdialog\"", onlineLookup);
        Assert.Contains("aria-labelledby=\"onboarding-online-consent-revoke-confirmation-message\"", onlineLookup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-cancel-button\"", onlineLookup);
        Assert.Contains("class=\"button button-secondary\"", onlineLookup);
        Assert.Contains("id=\"onboarding-online-consent-revoke-confirm-button\"", onlineLookup);
        Assert.Contains("class=\"button button-danger\"", onlineLookup);
        Assert.Contains("data-destructive-confirm", onlineLookup);
    }

    [TestMethod]
    public void VisualConsistencySliceFive_SummaryRetainsDefinitionListAndStacksAtNarrowWidths()
    {
        var markup = LoadStepUi("SummaryStep.razor");
        var styles = LoadUi("OnboardingHost.razor.css").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("<dl class=\"summary-list\">", markup);
        Assert.Contains("<dt>", markup);
        Assert.Contains("<dd>", markup);
        Assert.Contains("min-width: 0", ExtractCssRule(styles, ".onboarding-container ::deep .summary-item"));
        Assert.Contains("min-width: 0", ExtractCssRule(styles, ".onboarding-container ::deep .summary-item dt"));
        Assert.Contains("min-width: 0", ExtractCssRule(styles, ".onboarding-container ::deep .summary-item dd"));
        Assert.Contains("@media (max-width: 380px)", styles);
        Assert.Contains(".onboarding-container ::deep .summary-item {\n        align-items: stretch;\n        flex-direction: column;", styles);
        Assert.Contains("text-align: left", styles);
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

    [TestMethod]
    public void OnboardingHost_LayoutOwnsBoundedScrollSurfaceAndMirrorsApplicationScrollPattern()
    {
        var styles = LoadUi("OnboardingHost.razor.css").Replace("\r\n", "\n", StringComparison.Ordinal);
        var hostRule = ExtractCssRule(styles, ".onboarding-host");
        var mainRule = ExtractCssRule(styles, ".onboarding-main");

        Assert.Contains("display: flex;", hostRule);
        Assert.Contains("flex-direction: column;", hostRule);
        Assert.Contains("width: 100%;", hostRule);
        Assert.Contains("height: 100%;", hostRule);
        Assert.Contains("height: 100dvh;", hostRule);
        Assert.Contains("min-width: 0;", hostRule);
        Assert.Contains("min-height: 0;", hostRule);
        Assert.Contains("overflow-y: auto;", hostRule);
        Assert.Contains("overflow-x: hidden;", hostRule);

        Assert.DoesNotContain("align-items: center;", hostRule, StringComparison.Ordinal);
        Assert.DoesNotContain("justify-content: center;", hostRule, StringComparison.Ordinal);
        Assert.DoesNotContain("min-height: 100vh;", hostRule, StringComparison.Ordinal);
        Assert.DoesNotContain("min-height: 100dvh;", hostRule, StringComparison.Ordinal);

        Assert.Contains("margin: auto;", mainRule);
        Assert.DoesNotContain("margin: 0 auto;", mainRule, StringComparison.Ordinal);
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

    private static string ExtractCssRule(string styles, string selector)
    {
        var start = styles.IndexOf(selector, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, "The CSS selector '" + selector + "' is missing.");

        var end = styles.IndexOf('}', start);
        Assert.IsGreaterThan(start, end, "The CSS selector '" + selector + "' has no closing brace.");
        return styles[start..(end + 1)];
    }
}
