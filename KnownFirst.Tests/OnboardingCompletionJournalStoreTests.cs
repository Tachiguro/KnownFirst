using System.Text.Json;
using KnownFirst.Core.Settings;
using KnownFirst.Services.Onboarding;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingCompletionJournalStoreTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _values = new();

        public Func<string, object, object>? MutateOnSet { get; set; }
        public Func<string, object?, object?>? MutateOnGet { get; set; }

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
                var storedValue = MutateOnSet != null ? MutateOnSet(key, value) : value;
                _values[key] = storedValue;
            }
        }

        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        {
            if (_values.TryGetValue(key, out var val))
            {
                if (MutateOnGet != null)
                {
                    val = MutateOnGet(key, val);
                }

                if (val is T typedVal)
                {
                    return typedVal;
                }
            }

            return defaultValue;
        }

        public void SetRawString(string key, string value) => _values[key] = value;

        public string? GetRawString(string key) =>
            _values.TryGetValue(key, out var val) ? val as string : null;
    }

    private static Type GetJournalType() =>
        typeof(OnboardingState).Assembly.GetType("KnownFirst.Core.Settings.OnboardingCompletionJournal")
        ?? throw new AssertFailedException("Missing B1 behavior: OnboardingCompletionJournal type must exist.");

    private static Type GetJournalStoreType() =>
        typeof(InstallOriginClassifier).Assembly.GetType("KnownFirst.Services.Onboarding.MauiOnboardingCompletionJournalStore")
        ?? throw new AssertFailedException("Missing B1 behavior: MauiOnboardingCompletionJournalStore type must exist.");

    private static object CreateJournal(
        int version = 1,
        string attemptId = "attempt-001",
        string targetFingerprint = "fingerprint-abc-123",
        string uiLanguage = "en",
        ThemePreference theme = ThemePreference.Light,
        string? displayName = "Test User",
        bool? onlineLookupConsent = true,
        bool enhancedTermRecognitionEnabled = true,
        CardDirectionPreference cardDirection = CardDirectionPreference.Both,
        LearningMode learningMode = LearningMode.Automatic,
        int preparationLimit = 5,
        LearningTimezoneMode learningTimezoneMode = LearningTimezoneMode.System,
        string? explicitLearningTimezoneId = null,
        int learningDayCutoffMinutes = 0,
        string appVersion = "1.0.0-beta.13")
    {
        var journalType = GetJournalType();
        var ctors = journalType.GetConstructors();
        Assert.IsTrue(ctors.Length > 0, "OnboardingCompletionJournal must have a constructor.");
        var ctor = ctors[0];
        var parameters = ctor.GetParameters();

        if (parameters.Length == 0)
        {
            var instance = Activator.CreateInstance(journalType)!;
            SetProp(instance, "Version", version);
            SetProp(instance, "AttemptId", attemptId);
            SetProp(instance, "TargetFingerprint", targetFingerprint);
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
            SetProp(instance, "AppVersion", appVersion);
            return instance;
        }

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            args[i] = p.Name?.ToLowerInvariant() switch
            {
                "version" => version,
                "attemptid" => attemptId,
                "targetfingerprint" => targetFingerprint,
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
                "appversion" => appVersion,
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
        var storeType = GetJournalStoreType();
        return Activator.CreateInstance(storeType, preferences)
            ?? throw new AssertFailedException("Failed to instantiate MauiOnboardingCompletionJournalStore.");
    }

    private static (string Status, object? Journal, string? ErrorMessage) ReadFromStore(object store)
    {
        var readMethod = store.GetType().GetMethod("Read", Type.EmptyTypes);
        Assert.IsNotNull(readMethod, "MauiOnboardingCompletionJournalStore.Read() must exist.");
        var result = readMethod.Invoke(store, null)!;

        var statusProp = result.GetType().GetProperty("Status");
        Assert.IsNotNull(statusProp, "Result must have 'Status' property.");
        var status = statusProp.GetValue(result)!.ToString()!;

        var journalProp = result.GetType().GetProperty("Journal");
        var journal = journalProp?.GetValue(result);

        var errorProp = result.GetType().GetProperty("ErrorMessage");
        var error = errorProp?.GetValue(result) as string;

        return (status, journal, error);
    }

    private static void SaveToStore(object store, object journal)
    {
        var saveMethod = store.GetType().GetMethod("Save");
        Assert.IsNotNull(saveMethod, "MauiOnboardingCompletionJournalStore.Save(journal) must exist.");
        saveMethod.Invoke(store, [journal]);
    }

    private static bool SaveVerifiedToStore(object store, object journal)
    {
        var saveVerifiedMethod = store.GetType().GetMethod("SaveVerified", [journal.GetType()]);
        Assert.IsNotNull(saveVerifiedMethod, "MauiOnboardingCompletionJournalStore.SaveVerified(journal) must exist.");
        return (bool)saveVerifiedMethod.Invoke(store, [journal])!;
    }

    private static void ClearStore(object store)
    {
        var clearMethod = store.GetType().GetMethod("Clear", Type.EmptyTypes);
        Assert.IsNotNull(clearMethod, "MauiOnboardingCompletionJournalStore.Clear() must exist.");
        clearMethod.Invoke(store, null);
    }

    [TestMethod]
    public void AllFrozenTargetFields_SurviveSerializationRoundTrip()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var original = CreateJournal(
            version: 1,
            attemptId: "attempt-xyz-789",
            targetFingerprint: "fingerprint-canonical-hash-456",
            uiLanguage: "de",
            theme: ThemePreference.Dark,
            displayName: "Test User",
            onlineLookupConsent: false,
            enhancedTermRecognitionEnabled: false,
            cardDirection: CardDirectionPreference.MeaningToTerm,
            learningMode: LearningMode.Reading,
            preparationLimit: 10,
            learningTimezoneMode: LearningTimezoneMode.Explicit,
            explicitLearningTimezoneId: "Europe/Berlin",
            learningDayCutoffMinutes: 240,
            appVersion: "1.0.0-beta.13");

        SaveToStore(store, original);

        var (status, readJournal, _) = ReadFromStore(store);
        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readJournal);

        Assert.AreEqual(1, GetProp<int>(readJournal, "Version"));
        Assert.AreEqual("attempt-xyz-789", GetProp<string>(readJournal, "AttemptId"));
        Assert.AreEqual("fingerprint-canonical-hash-456", GetProp<string>(readJournal, "TargetFingerprint"));
        Assert.AreEqual("de", GetProp<string>(readJournal, "UiLanguage"));
        Assert.AreEqual(ThemePreference.Dark, GetProp<ThemePreference>(readJournal, "Theme"));
        Assert.AreEqual("Test User", GetProp<string?>(readJournal, "DisplayName"));
        Assert.AreEqual(false, GetProp<bool?>(readJournal, "OnlineLookupConsent"));
        Assert.IsFalse(GetProp<bool>(readJournal, "EnhancedTermRecognitionEnabled"));
        Assert.AreEqual(CardDirectionPreference.MeaningToTerm, GetProp<CardDirectionPreference>(readJournal, "CardDirection"));
        Assert.AreEqual(LearningMode.Reading, GetProp<LearningMode>(readJournal, "LearningMode"));
        Assert.AreEqual(10, GetProp<int>(readJournal, "PreparationLimit"));
        Assert.AreEqual(LearningTimezoneMode.Explicit, GetProp<LearningTimezoneMode>(readJournal, "LearningTimezoneMode"));
        Assert.AreEqual("Europe/Berlin", GetProp<string?>(readJournal, "ExplicitLearningTimezoneId"));
        Assert.AreEqual(240, GetProp<int>(readJournal, "LearningDayCutoffMinutes"));
        Assert.AreEqual("1.0.0-beta.13", GetProp<string>(readJournal, "AppVersion"));
    }

    [TestMethod]
    public void AttemptIdAndTargetFingerprint_RoundTripExactly()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var journal = CreateJournal(
            attemptId: "unique-attempt-uuid-12345",
            targetFingerprint: "sha256-fingerprint-67890");

        SaveToStore(store, journal);

        var (status, readJournal, _) = ReadFromStore(store);
        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readJournal);
        Assert.AreEqual("unique-attempt-uuid-12345", GetProp<string>(readJournal, "AttemptId"));
        Assert.AreEqual("sha256-fingerprint-67890", GetProp<string>(readJournal, "TargetFingerprint"));
    }

    [TestMethod]
    public void ValidJournal_SaveAndRead_Works()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var journal = CreateJournal();
        SaveToStore(store, journal);

        var (status, readJournal, _) = ReadFromStore(store);
        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readJournal);
    }

    [TestMethod]
    public void MissingJournal_IsDistinguishableFromValidJournal()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var (status, journal, _) = ReadFromStore(store);
        Assert.AreEqual("Missing", status);
        Assert.IsNull(journal);
    }

    [TestMethod]
    public void MalformedJournal_IsReportedDistinctly()
    {
        var prefs = new InMemoryPreferences();
        const string journalKey = "onboarding_completion_journal";
        prefs.SetRawString(journalKey, "{\"version\":1, not a valid json");

        var store = CreateStore(prefs);
        var (status, journal, _) = ReadFromStore(store);

        Assert.AreEqual("Malformed", status);
        Assert.IsNull(journal);
        Assert.AreEqual("{\"version\":1, not a valid json", prefs.GetRawString(journalKey),
            "Malformed journal data must not be overwritten by read.");
    }

    [TestMethod]
    public void UnsupportedFutureVersion_IsReportedDistinctly()
    {
        var prefs = new InMemoryPreferences();
        const string journalKey = "onboarding_completion_journal";
        const string futureJson = "{\"version\":999,\"attemptId\":\"att\",\"targetFingerprint\":\"fp\",\"appVersion\":\"1.0\"}";
        prefs.SetRawString(journalKey, futureJson);

        var store = CreateStore(prefs);
        var (status, journal, _) = ReadFromStore(store);

        Assert.AreEqual("UnsupportedVersion", status);
        Assert.IsNull(journal);
        Assert.AreEqual(futureJson, prefs.GetRawString(journalKey));
    }

    [TestMethod]
    public void InvalidJournalFields_AreRejectedAndClassified()
    {
        var prefs = new InMemoryPreferences();
        const string journalKey = "onboarding_completion_journal";
        // AttemptId is empty, which is invalid
        const string invalidJson = "{\"version\":1,\"attemptId\":\"\",\"targetFingerprint\":\"fp\",\"appVersion\":\"1.0\",\"preparationLimit\":5}";
        prefs.SetRawString(journalKey, invalidJson);

        var store = CreateStore(prefs);
        var (status, journal, _) = ReadFromStore(store);

        Assert.AreEqual("Invalid", status);
        Assert.IsNull(journal);
        Assert.AreEqual(invalidJson, prefs.GetRawString(journalKey));
    }

    [TestMethod]
    public void VerifiedSave_SucceedsOnlyWhenReadBackPayloadExactlyEqualsRequestedJournal()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        var journal = CreateJournal();
        var success = SaveVerifiedToStore(store, journal);

        Assert.IsTrue(success, "SaveVerified must return true when read-back payload matches exactly.");

        var (status, readJournal, _) = ReadFromStore(store);
        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readJournal);
        Assert.AreEqual(GetProp<string>(journal, "AttemptId"), GetProp<string>(readJournal, "AttemptId"));
        Assert.AreEqual(GetProp<string>(journal, "TargetFingerprint"), GetProp<string>(readJournal, "TargetFingerprint"));
    }

    [TestMethod]
    public void VerifiedSave_WriteReadDeserializeValidationMismatch_ReturnsFailure()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        // Simulate a corrupted write/read where the stored value gets mutated
        prefs.MutateOnGet = (key, val) =>
        {
            if (key == "onboarding_completion_journal" && val is string s)
            {
                // Mutate the attemptId so read-back target equality check fails
                return s.Replace("attempt-001", "attempt-corrupted-mismatch");
            }
            return val;
        };

        var journal = CreateJournal(attemptId: "attempt-001");
        var success = SaveVerifiedToStore(store, journal);

        Assert.IsFalse(success, "SaveVerified must return false when read-back payload mismatches.");
    }

    [TestMethod]
    public void FailingVerifiedSave_DoesNotReportSuccess()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        // Simulate read-back returning null (e.g. storage error)
        prefs.MutateOnGet = (key, _) => key == "onboarding_completion_journal" ? null : null;

        var journal = CreateJournal();
        var success = SaveVerifiedToStore(store, journal);

        Assert.IsFalse(success, "SaveVerified must not report success if read-back returns null or fails.");
    }

    [TestMethod]
    public void Clear_RemovesOnlyJournalKey()
    {
        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);

        const string otherKey = "unrelated_journal_test_key";
        prefs.SetRawString(otherKey, "unrelated_val");

        var journal = CreateJournal();
        SaveToStore(store, journal);

        Assert.IsTrue(prefs.ContainsKey("onboarding_completion_journal"));
        Assert.IsTrue(prefs.ContainsKey(otherKey));

        ClearStore(store);

        Assert.IsFalse(prefs.ContainsKey("onboarding_completion_journal"));
        Assert.IsTrue(prefs.ContainsKey(otherKey), "Clear must remove only the journal key.");
    }

    [TestMethod]
    public void JournalSerialization_WorksWithReflectionDisabledByDefault()
    {
        Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault,
            "Tests must execute with reflection serialization disabled by default.");

        var prefs = new InMemoryPreferences();
        var store = CreateStore(prefs);
        var journal = CreateJournal();

        var success = SaveVerifiedToStore(store, journal);
        Assert.IsTrue(success);

        var (status, readJournal, _) = ReadFromStore(store);
        Assert.AreEqual("Valid", status);
        Assert.IsNotNull(readJournal);
    }

    [TestMethod]
    public void JournalKey_IsNotPartOfLegacyInstallOriginEvidence()
    {
        var storeType = GetJournalStoreType();
        var keyField = storeType.GetField("JournalPreferenceKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(keyField, "MauiOnboardingCompletionJournalStore.JournalPreferenceKey must exist.");
        var journalKey = (string)keyField.GetValue(null)!;
        Assert.AreEqual("onboarding_completion_journal", journalKey);

        Assert.IsFalse(
            InstallOriginClassifier.LegacyPreferenceEvidenceKeys.Contains(journalKey),
            $"The journal preference key '{journalKey}' must NOT be included in InstallOriginClassifier.LegacyPreferenceEvidenceKeys.");
    }
}
