using KnownFirst.Core.Settings;
using KnownFirst.Services.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnboardingHostTests
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
    }

    [TestMethod]
    public void OnboardingStepPolicy_FirstAndLastStepsMatchApprovedNineStepSequence()
    {
        Assert.AreEqual(OnboardingStep.WelcomeLanguage, OnboardingStepPolicy.FirstStep);
        Assert.AreEqual(OnboardingStep.Summary, OnboardingStepPolicy.LastStep);
        Assert.AreEqual(1, (int)OnboardingStep.WelcomeLanguage);
        Assert.AreEqual(2, (int)OnboardingStep.DisplayName);
        Assert.AreEqual(3, (int)OnboardingStep.Workflow);
        Assert.AreEqual(4, (int)OnboardingStep.OnlineLookup);
        Assert.AreEqual(5, (int)OnboardingStep.EnhancedTermRecognition);
        Assert.AreEqual(6, (int)OnboardingStep.Practice);
        Assert.AreEqual(7, (int)OnboardingStep.DailyPace);
        Assert.AreEqual(8, (int)OnboardingStep.LearningDayTiming);
        Assert.AreEqual(9, (int)OnboardingStep.Summary);
    }

    [TestMethod]
    public void VisualConsistencySliceFour_OnboardingStepIdentitiesRemainExactlyOneThroughNine()
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(OnboardingStep.WelcomeLanguage)] = 1,
            [nameof(OnboardingStep.DisplayName)] = 2,
            [nameof(OnboardingStep.Workflow)] = 3,
            [nameof(OnboardingStep.OnlineLookup)] = 4,
            [nameof(OnboardingStep.EnhancedTermRecognition)] = 5,
            [nameof(OnboardingStep.Practice)] = 6,
            [nameof(OnboardingStep.DailyPace)] = 7,
            [nameof(OnboardingStep.LearningDayTiming)] = 8,
            [nameof(OnboardingStep.Summary)] = 9
        };

        var actual = Enum.GetValues<OnboardingStep>()
            .ToDictionary(step => step.ToString(), step => (int)step, StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actual.Keys.ToArray());
        foreach (var entry in expected)
        {
            Assert.AreEqual(entry.Value, actual[entry.Key], entry.Key);
        }
    }

    [TestMethod]
    public void OnboardingStepPolicy_TryGetNext_FollowsStrictSequentialOrder()
    {
        var current = OnboardingStep.WelcomeLanguage;
        var expectedNext = new[]
        {
            OnboardingStep.DisplayName,
            OnboardingStep.Workflow,
            OnboardingStep.OnlineLookup,
            OnboardingStep.EnhancedTermRecognition,
            OnboardingStep.Practice,
            OnboardingStep.DailyPace,
            OnboardingStep.LearningDayTiming,
            OnboardingStep.Summary
        };

        foreach (var expected in expectedNext)
        {
            Assert.IsTrue(OnboardingStepPolicy.TryGetNext(current, out var next));
            Assert.AreEqual(expected, next);
            current = next;
        }

        Assert.IsFalse(OnboardingStepPolicy.TryGetNext(OnboardingStep.Summary, out var boundaryNext));
        Assert.AreEqual(OnboardingStep.Summary, boundaryNext);
    }

    [TestMethod]
    public void OnboardingStepPolicy_TryGetPrevious_FollowsStrictReverseSequentialOrder()
    {
        var current = OnboardingStep.Summary;
        var expectedPrevious = new[]
        {
            OnboardingStep.LearningDayTiming,
            OnboardingStep.DailyPace,
            OnboardingStep.Practice,
            OnboardingStep.EnhancedTermRecognition,
            OnboardingStep.OnlineLookup,
            OnboardingStep.Workflow,
            OnboardingStep.DisplayName,
            OnboardingStep.WelcomeLanguage
        };

        foreach (var expected in expectedPrevious)
        {
            Assert.IsTrue(OnboardingStepPolicy.TryGetPrevious(current, out var prev));
            Assert.AreEqual(expected, prev);
            current = prev;
        }

        Assert.IsFalse(OnboardingStepPolicy.TryGetPrevious(OnboardingStep.WelcomeLanguage, out var boundaryPrev));
        Assert.AreEqual(OnboardingStep.WelcomeLanguage, boundaryPrev);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(10)]
    [DataRow(99)]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void OnboardingStepPolicy_InvalidRawValues_FailSafelyToWelcomeLanguage(int invalidRaw)
    {
        Assert.IsFalse(OnboardingStepPolicy.TryNormalize(invalidRaw, out var step));
        Assert.AreEqual(OnboardingStep.WelcomeLanguage, step);
        Assert.AreEqual(OnboardingStep.WelcomeLanguage, OnboardingStepPolicy.Normalize(invalidRaw));
    }

    [TestMethod]
    public void MauiOnboardingProgressStore_PersistsAndClearsStep()
    {
        var preferences = new InMemoryPreferences();
        var store = new MauiOnboardingProgressStore(preferences);

        Assert.IsNull(store.GetCurrentStep());

        store.SetCurrentStep(OnboardingStep.DisplayName);
        Assert.AreEqual(OnboardingStep.DisplayName, store.GetCurrentStep());
        Assert.AreEqual((int)OnboardingStep.DisplayName, preferences.Get("onboarding_step", -1));

        store.ClearProgress();
        Assert.IsNull(store.GetCurrentStep());
        Assert.IsFalse(preferences.ContainsKey("onboarding_step"));
    }

    [TestMethod]
    public void OnboardingStep_IsExcludedFromLegacyInstallOriginEvidence()
    {
        var preferences = new InMemoryPreferences();
        var onboardingStateStore = new MauiOnboardingStateStore(preferences);
        var classifier = new InstallOriginClassifier(
            preferences,
            onboardingStateStore,
            NullLogger<InstallOriginClassifier>.Instance);

        preferences.Set("onboarding_step", 2);

        var state = classifier.EnsureClassified();

        Assert.AreEqual(OnboardingState.Required, state,
            "onboarding_step must never be treated as legacy pre-existing evidence.");
    }
}
