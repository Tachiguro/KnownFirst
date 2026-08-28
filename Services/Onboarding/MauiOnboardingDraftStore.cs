using System.Text.Json;
using KnownFirst.Core.Settings;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services.Onboarding;

public sealed class MauiOnboardingDraftStore(IPreferences preferences) : IOnboardingDraftStore
{
    public const string DraftPreferenceKey = "onboarding_draft";

    public OnboardingDraftReadResult Read()
    {
        if (!preferences.ContainsKey(DraftPreferenceKey))
        {
            return OnboardingDraftReadResult.Missing();
        }

        var rawJson = preferences.Get(DraftPreferenceKey, (string?)null);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return OnboardingDraftReadResult.Missing();
        }

        // Validate JSON structure and version probe before deserialization
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OnboardingDraftReadResult.Malformed("Draft JSON root must be an object.");
            }

            if (doc.RootElement.TryGetProperty("version", out var versionProp) ||
                doc.RootElement.TryGetProperty("Version", out versionProp))
            {
                if (!versionProp.TryGetInt32(out var probeVersion))
                {
                    return OnboardingDraftReadResult.Invalid("Version must be an integer.");
                }

                if (probeVersion > OnboardingDraftPolicy.CurrentVersion)
                {
                    return OnboardingDraftReadResult.UnsupportedVersion(probeVersion);
                }

                if (probeVersion < 1)
                {
                    return OnboardingDraftReadResult.Invalid("Version must be at least 1.");
                }
            }
            else
            {
                return OnboardingDraftReadResult.Invalid("Draft JSON is missing version property.");
            }
        }
        catch (JsonException ex)
        {
            return OnboardingDraftReadResult.Malformed(ex.Message);
        }

        OnboardingDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize(
                rawJson,
                OnboardingJsonSerializerContext.Default.OnboardingDraft);
        }
        catch (JsonException ex)
        {
            return OnboardingDraftReadResult.Malformed(ex.Message);
        }

        if (draft is null)
        {
            return OnboardingDraftReadResult.Malformed("Deserialization returned null.");
        }

        if (draft.Version > OnboardingDraftPolicy.CurrentVersion)
        {
            return OnboardingDraftReadResult.UnsupportedVersion(draft.Version);
        }

        if (draft.Version < 1)
        {
            return OnboardingDraftReadResult.Invalid("Version must be at least 1.");
        }

        if (!OnboardingDraftPolicy.IsValid(draft, out var reason))
        {
            return OnboardingDraftReadResult.Invalid(reason ?? "Invalid field values.");
        }

        return OnboardingDraftReadResult.Valid(draft);
    }

    public void Save(OnboardingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var json = JsonSerializer.Serialize(
            draft,
            OnboardingJsonSerializerContext.Default.OnboardingDraft);
        preferences.Set(DraftPreferenceKey, json);
    }

    public void Clear()
    {
        preferences.Remove(DraftPreferenceKey);
    }
}
