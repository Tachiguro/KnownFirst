using System.Text.Json;
using KnownFirst.Core.Language;
using KnownFirst.Core.Settings;
using KnownFirst.Services.Onboarding;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingDraftStoreTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _values = new();

        public IReadOnlyCollection<string> Keys => _values.Keys.ToArray();

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);

        public void Remove(string key, string? sharedName = null) => _values.Remove(key);

        public void Clear(string? sharedName = null) => _values.Clear();

        public void Set<T>(string key, T value, string? sharedName = null)
        {
            if (value is null)
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }
        }

        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        {
            if (_values.TryGetValue(key, out var val) && val is T typedVal)
            {
                return typedVal;
            }

            return defaultValue;
        }

        public void SetRawString(string key, string value) => _values[key] = value;

        public string? GetRawString(string key) =>
            _values.TryGetValue(key, out var val) ? val as string : null;
    }

    private static Type GetDraftType() =>
        typeof(OnboardingState).Assembly.GetType("KnownFirst.Core.Settings.OnboardingDraft")
        ?? throw new AssertFailedException("Missing B1 behavior: OnboardingDraft type must exist.");

    private static Type GetDraftPolicyType() =>
        typeof(OnboardingState).Assembly.GetType("KnownFirst.Core.Settings.OnboardingDraftPolicy")
        ?? throw new AssertFailedException("Missing B1 behavior: OnboardingDraftPolicy type must exist.");

    private static Type GetDraftStoreType() =>
        typeof(InstallOriginClassifier).Assembly.GetType("KnownFirst.Services.Onboarding.MauiOnboardingDraftStore")
        ?? throw new AssertFailedException("Missing B1 behavior: MauiOnboardingDraftStore type must exist.");

    private static object CreateDefaultDraft()
    {
        var policyType = GetDraftPolicyType();
        var createDefaultMethod = policyType.GetMethod("CreateDefault", Type.EmptyTypes);
        Assert.IsNotNull(createDefaultMethod, "OnboardingDraftPolicy.CreateDefault() must exist.");
        return createDefaultMethod.Invoke(null, null)!;
    }

    private static object CreateCustomDraft(
        int version,
        string uiLanguage,
        ThemePreference theme,
        string? displayName,
        bool? onlineLookupConsent,
        bool enhancedTermRecognitionEnabled,
        CardDirectionPreference cardDirection,
        LearningMode learningMode,
        int preparationLimit,
        LearningTimezoneMode learningTimezoneMode,
        string? explicitLearningTimezoneId,
        int learningDayCutoffMinutes,
        string? lastCompletionAttemptFingerprint)
    {
        var draftType = GetDraftType();
        var constructors = draftType.GetConstructors();
        Assert.IsTrue(constructors.Length > 0, "OnboardingDraft must have a constructor.");
        var ctor = constructors[0];
        var parameters = ctor.GetParameters();

        // Create with positional args matching constructor or default constructor + reflection property set
        if (parameters.Length == 0)
        {
            var instance = Activator.CreateInstance(draftType)!;
            SetProp(instance, "Version", version);
            SetProp(instance, "UiLanguage", uiLanguage);
            SetProp(instance, "Theme", theme);
            SetProp(instance, "DisplayName", displayName);
            SetProp(instance, "OnlineLookupConsent", onlineLookupConsent);
            SetProp(instance, "EnhancedTermRecognitionEnabled", enhancedTermRecognitionEnabled);
            SetProp(instance, "CardDirection", cardDirection);
            SetProp(instance, "LearningMode", learningMode);
            SetProp(instance, "PreparationLimit", preparationLimit);
            SetProp(instance, "LearningTimezoneMode", learningTimezoneMode);
            SetProp(instance, "ExplicitLearningTimezoneId", explicitLearningTimezoneId);
            SetProp(instance, "LearningDayCutoffMinutes", learningDayCutoffMinutes);
            SetProp(instance, "LastCompletionAttemptFingerprint", lastCompletionAttemptFingerprint);
            return instance;
        }

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            args[i] = p.Name?.ToLowerInvariant() switch
            {
                "version" => version,
                "uilanguage" => uiLanguage,
                "theme" => theme,
                "displayname" => displayName,
                "onlinelookupconsent" => onlineLookupConsent,
                "enhancedtermrecognitionenabled" => enhancedTermRecognitionEnabled,
                "carddirection" => cardDirection,
                "learningmode" => learningMode,
                "preparationlimit" => preparationLimit,
                "learningtimezonemode" => learningTimezoneMode,
                "explicitlearningtimezoneid" => explicitLearningTimezoneId,
                "learningdaycutoffminutes" => learningDayCutoffMinutes,
                "lastcompletionattemptfingerprint" => lastCompletionAttemptFingerprint,
                _ => throw new AssertFailedException($"Unknown parameter: {p.Name}")
            };
        }

        return ctor.Invoke(args);
    }

    private static T GetProp<T>(object instance, string propName)
    {
        var prop = instance.GetType().GetProperty(propName);
        Assert.IsNotNull(prop, $"Property '{propName}' must exist on {instance.GetType().Name}.");
        return (T)prop.GetValue(instance)!;
    }

    private static void SetProp(object instance, string propName, object? value)
    {
        var prop = instance.GetType().GetProperty(propName);
        Assert.IsNotNull(prop, $"Property '{propName}' must exist on {instance.GetType().Name}.");
        prop.SetValue(instance, value);
    }

    private static object CreateStore(InMemoryPreferences preferences)
    {
        var storeType = GetDraftStoreType();
        return Activator.CreateInstance(storeType, preferences)
            ?? throw new AssertFailedException("Failed to instantiate MauiOnboardingDraftStore.");
    }

    private static (string Status, object? Draft, string? ErrorMessage) ReadFromStore(object store)
    {
        var readMethod = store.GetType().GetMethod("Read", Type.EmptyTypes);
        Assert.IsNotNull(readMethod, "MauiOnboardingDraftStore.Read() must exist.");
        var result = readMethod.Invoke(store, null)!;

        var statusProp = result.GetType().GetProperty("Status");
        Assert.IsNotNull(statusProp, "Result must have 'Status' property.");
        var status = statusProp.GetValue(result)!.ToString()!;

        var draftProp = result.GetType().GetProperty("Draft");
        var draft = draftProp?.GetValue(result);

        var errorProp = result.GetType().GetProperty("ErrorMessage");
        var error = errorProp?.GetValue(result) as string;

        return (status, draft, error);
    }

    private static void SaveToStore(object store, object draft)
    {
        var saveMethod = store.GetType().GetMethod("Save");
        Assert.IsNotNull(saveMethod, "MauiOnboardingDraftStore.Save(draft) must exist.");
        saveMethod.Invoke(store, [draft]);
    }

    private static void ClearStore(object store)
    {
        var clearMethod = store.GetType().GetMethod("Clear", Type.EmptyTypes);
        Assert.IsNotNull(clearMethod, "MauiOnboardingDraftStore.Clear() must exist.");
        clearMethod.Invoke(store, null);
    }

    [TestMethod]
    public void FreshDefaultDraft_HasCorrectVersionAndDefaultFields()
    {
        var draft = CreateDefaultDraft();

        Assert.AreEqual(1, GetProp<int>(draft, "Version"));
        Assert.AreEqual(LanguagePreferencePolicy.SystemPreferenceCode, GetProp<string>(draft, "UiLanguage"));
        Assert.AreEqual(ThemePreference.System, GetProp<ThemePreference>(draft, "Theme"));
        Assert.IsNull(GetProp<string?>(draft, "DisplayName"));
        Assert.IsNull(GetProp<bool?>(draft, "OnlineLookupConsent"));
        Assert.AreEqual(EnhancedTermRecognitionPolicy.DefaultEnabled, GetProp<bool>(draft, "EnhancedTermRecognitionEnabled"));
        Assert.AreEqual(CardDirectionPreferencePolicy.DefaultPreference, GetProp<CardDirectionPreference>(draft, "CardDirection"));
        Assert.AreEqual(LearningModePolicy.DefaultMode, GetProp<LearningMode>(draft, "LearningMode"));
        Assert.AreEqual(PreparationLimitPolicy.DefaultLimit, GetProp<int>(draft, "PreparationLimit"));
        Assert.AreEqual(LearningTimezoneMode.System, GetProp<LearningTimezoneMode>(draft, "LearningTimezoneMode"));
        Assert.IsNull(GetProp<string?>(draft, "ExplicitLearningTimezoneId"));
        Assert.AreEqual(0, GetProp<int>(draft, "LearningDayCutoffMinutes"));
        Assert.IsNull(GetProp<string?>(draft, "LastCompletionAttemptFingerprint"));
    }

    [TestMethod]
    public void OnlineLookupConsent_DefaultIsNull()
    {
        var draft = CreateDefaultDraft();
        var consent = GetProp<bool?>(draft, "OnlineLookupConsent");
        Assert.IsNull(consent, "Default OnlineLookupConsent must be null (undecided), never false.");
    }

    [TestMethod]
    public void AllRequiredOnboardingFields_SurviveSerializationRoundTrip()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var original = CreateCustomDraft(
            version: 1,
            uiLanguage: "de",
            theme: ThemePreference.Dark,
            displayName: "Max Mustermann",
            onlineLookupConsent: true,
            enhancedTermRecognitionEnabled: false,
            cardDirection: CardDirectionPreference.TermToMeaning,
            learningMode: LearningMode.Typing,
            preparationLimit: 10,
            learningTimezoneMode: LearningTimezoneMode.Explicit,
            explicitLearningTimezoneId: "Europe/Berlin",
            learningDayCutoffMinutes: 180,
            lastCompletionAttemptFingerprint: "fp-test-12345");

        SaveToStore(store, original);

        var (status, readDraft, error) = ReadFromStore(store);
        Assert.AreEqual("Valid", status, $"Expected Valid status, got {status} ({error})");
        Assert.IsNotNull(readDraft);

        Assert.AreEqual(1, GetProp<int>(readDraft, "Version"));
        Assert.AreEqual("de", GetProp<string>(readDraft, "UiLanguage"));
        Assert.AreEqual(ThemePreference.Dark, GetProp<ThemePreference>(readDraft, "Theme"));
        Assert.AreEqual("Max Mustermann", GetProp<string?>(readDraft, "DisplayName"));
        Assert.AreEqual(true, GetProp<bool?>(readDraft, "OnlineLookupConsent"));
        Assert.IsFalse(GetProp<bool>(readDraft, "EnhancedTermRecognitionEnabled"));
        Assert.AreEqual(CardDirectionPreference.TermToMeaning, GetProp<CardDirectionPreference>(readDraft, "CardDirection"));
        Assert.AreEqual(LearningMode.Typing, GetProp<LearningMode>(readDraft, "LearningMode"));
        Assert.AreEqual(10, GetProp<int>(readDraft, "PreparationLimit"));
        Assert.AreEqual(LearningTimezoneMode.Explicit, GetProp<LearningTimezoneMode>(readDraft, "LearningTimezoneMode"));
        Assert.AreEqual("Europe/Berlin", GetProp<string?>(readDraft, "ExplicitLearningTimezoneId"));
        Assert.AreEqual(180, GetProp<int>(readDraft, "LearningDayCutoffMinutes"));
        Assert.AreEqual("fp-test-12345", GetProp<string?>(readDraft, "LastCompletionAttemptFingerprint"));
    }

    [TestMethod]
    public void SupportedValidDraft_RoundTripsThroughPreferenceStore()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var draft = CreateDefaultDraft();
        SaveToStore(store, draft);

        var (status, readDraft, _) = ReadFromStore(store);
        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readDraft);
        Assert.AreEqual(GetProp<int>(draft, "Version"), GetProp<int>(readDraft, "Version"));
        Assert.AreEqual(GetProp<string>(draft, "UiLanguage"), GetProp<string>(readDraft, "UiLanguage"));
        Assert.AreEqual(GetProp<ThemePreference>(draft, "Theme"), GetProp<ThemePreference>(readDraft, "Theme"));
    }

    [TestMethod]
    public void MissingDraft_IsDistinguishableFromValidDraft()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var (status, draft, _) = ReadFromStore(store);
        Assert.AreEqual("Missing", status);
        Assert.IsNull(draft);
    }

    [TestMethod]
    public void MalformedJson_IsReportedAsMalformedOrInvalid_NotSilentlyReplaced()
    {
        var prefs = new InMemoryPreferences();
        const string draftKey = "onboarding_draft";
        prefs.SetRawString(draftKey, "{\"version\": 1, not valid json at all");

        var store = CreateStore(prefs);
        var (status, draft, error) = ReadFromStore(store);

        Assert.AreEqual("Malformed", status, $"Expected Malformed, got {status} ({error})");
        Assert.IsNull(draft);
        Assert.AreEqual("{\"version\": 1, not valid json at all", prefs.GetRawString(draftKey),
            "Malformed persisted data must not be silently replaced or overwritten.");
    }

    [TestMethod]
    public void UnsupportedFutureVersion_IsReportedDistinctly_AndPersistedDataNotOverwritten()
    {
        var prefs = new InMemoryPreferences();
        const string draftKey = "onboarding_draft";
        const string futureJson = "{\"version\":999,\"uiLanguage\":\"en\",\"theme\":\"System\"}";
        prefs.SetRawString(draftKey, futureJson);

        var store = CreateStore(prefs);
        var (status, draft, _) = ReadFromStore(store);

        Assert.AreEqual("UnsupportedVersion", status);
        Assert.IsNull(draft);
        Assert.AreEqual(futureJson, prefs.GetRawString(draftKey),
            "Unsupported future version must not be overwritten merely by reading it.");
    }

    [TestMethod]
    public void SupportedVersionInvalidFieldValues_AreRejectedAndClassified()
    {
        var prefs = new InMemoryPreferences();
        const string draftKey = "onboarding_draft";
        // Preparation limit 999 is invalid according to PreparationLimitPolicy (max 50)
        const string invalidJson = "{\"version\":1,\"preparationLimit\":999,\"uiLanguage\":\"en\"}";
        prefs.SetRawString(draftKey, invalidJson);

        var store = CreateStore(prefs);
        var (status, draft, _) = ReadFromStore(store);

        Assert.AreEqual("Invalid", status);
        Assert.IsNull(draft);
        Assert.AreEqual(invalidJson, prefs.GetRawString(draftKey));
    }

    [TestMethod]
    public void ExplicitNullDisplayNameAndTimezone_RoundTripCorrectly()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var draft = CreateCustomDraft(
            version: 1,
            uiLanguage: "en",
            theme: ThemePreference.Light,
            displayName: null,
            onlineLookupConsent: false,
            enhancedTermRecognitionEnabled: true,
            cardDirection: CardDirectionPreference.Both,
            learningMode: LearningMode.Automatic,
            preparationLimit: 5,
            learningTimezoneMode: LearningTimezoneMode.System,
            explicitLearningTimezoneId: null,
            learningDayCutoffMinutes: 0,
            lastCompletionAttemptFingerprint: null);

        SaveToStore(store, draft);

        var (status, readDraft, _) = ReadFromStore(store);
        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readDraft);
        Assert.IsNull(GetProp<string?>(readDraft, "DisplayName"));
        Assert.IsNull(GetProp<string?>(readDraft, "ExplicitLearningTimezoneId"));
        Assert.AreEqual(false, GetProp<bool?>(readDraft, "OnlineLookupConsent"));
    }

    [TestMethod]
    public void Clear_RemovesOnlyDraftKey()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        const string otherKey = "unrelated_test_key";
        prefs.SetRawString(otherKey, "unrelated_value");

        var draft = CreateDefaultDraft();
        SaveToStore(store, draft);

        Assert.IsTrue(prefs.ContainsKey("onboarding_draft"));
        Assert.IsTrue(prefs.ContainsKey(otherKey));

        ClearStore(store);

        Assert.IsFalse(prefs.ContainsKey("onboarding_draft"));
        Assert.IsTrue(prefs.ContainsKey(otherKey), "Clear must remove only the draft key.");
    }

    [TestMethod]
    public void Serialization_WorksWithReflectionDisabledByDefault()
    {
        Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault,
            "Tests must execute with reflection serialization disabled by default.");

        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);
        var draft = CreateDefaultDraft();

        SaveToStore(store, draft);
        var (status, readDraft, _) = ReadFromStore(store);

        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readDraft);
    }

    [TestMethod]
    public void DraftKey_IsNotPartOfLegacyInstallOriginEvidence()
    {
        var storeType = GetDraftStoreType();
        var keyField = storeType.GetField("DraftPreferenceKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(keyField, "MauiOnboardingDraftStore.DraftPreferenceKey must exist.");
        var draftKey = (string)keyField.GetValue(null)!;
        Assert.AreEqual("onboarding_draft", draftKey);

        Assert.IsFalse(
            InstallOriginClassifier.LegacyPreferenceEvidenceKeys.Contains(draftKey),
            $"The draft preference key '{draftKey}' must NOT be included in InstallOriginClassifier.LegacyPreferenceEvidenceKeys.");
    }
}
