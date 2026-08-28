using System.Text.Json;
using KnownFirst.Core.Settings;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services.Onboarding;

public sealed class MauiOnboardingCompletionJournalStore(IPreferences preferences) : IOnboardingCompletionJournalStore
{
    public const string JournalPreferenceKey = "onboarding_completion_journal";

    public OnboardingCompletionJournalReadResult Read()
    {
        if (!preferences.ContainsKey(JournalPreferenceKey))
        {
            return OnboardingCompletionJournalReadResult.Missing();
        }

        var rawJson = preferences.Get(JournalPreferenceKey, (string?)null);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return OnboardingCompletionJournalReadResult.Missing();
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OnboardingCompletionJournalReadResult.Malformed("Journal JSON root must be an object.");
            }

            if (doc.RootElement.TryGetProperty("version", out var versionProp) ||
                doc.RootElement.TryGetProperty("Version", out versionProp))
            {
                if (!versionProp.TryGetInt32(out var probeVersion))
                {
                    return OnboardingCompletionJournalReadResult.Invalid("Version must be an integer.");
                }

                if (probeVersion > OnboardingCompletionJournalPolicy.CurrentVersion)
                {
                    return OnboardingCompletionJournalReadResult.UnsupportedVersion(probeVersion);
                }

                if (probeVersion < 1)
                {
                    return OnboardingCompletionJournalReadResult.Invalid("Version must be at least 1.");
                }
            }
            else
            {
                return OnboardingCompletionJournalReadResult.Invalid("Journal JSON is missing version property.");
            }
        }
        catch (JsonException ex)
        {
            return OnboardingCompletionJournalReadResult.Malformed(ex.Message);
        }

        OnboardingCompletionJournal? journal;
        try
        {
            journal = JsonSerializer.Deserialize(
                rawJson,
                OnboardingJsonSerializerContext.Default.OnboardingCompletionJournal);
        }
        catch (JsonException ex)
        {
            return OnboardingCompletionJournalReadResult.Malformed(ex.Message);
        }

        if (journal is null)
        {
            return OnboardingCompletionJournalReadResult.Malformed("Deserialization returned null.");
        }

        if (journal.Version > OnboardingCompletionJournalPolicy.CurrentVersion)
        {
            return OnboardingCompletionJournalReadResult.UnsupportedVersion(journal.Version);
        }

        if (journal.Version < 1)
        {
            return OnboardingCompletionJournalReadResult.Invalid("Version must be at least 1.");
        }

        if (!OnboardingCompletionJournalPolicy.IsValid(journal, out var reason))
        {
            return OnboardingCompletionJournalReadResult.Invalid(reason ?? "Invalid field values.");
        }

        return OnboardingCompletionJournalReadResult.Valid(journal);
    }

    public void Save(OnboardingCompletionJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var json = JsonSerializer.Serialize(
            journal,
            OnboardingJsonSerializerContext.Default.OnboardingCompletionJournal);
        preferences.Set(JournalPreferenceKey, json);
    }

    public bool SaveVerified(OnboardingCompletionJournal journal)
    {
        if (journal is null)
        {
            return false;
        }

        if (!OnboardingCompletionJournalPolicy.IsValid(journal, out _))
        {
            return false;
        }

        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(
                journal,
                OnboardingJsonSerializerContext.Default.OnboardingCompletionJournal);
        }
        catch
        {
            return false;
        }

        preferences.Set(JournalPreferenceKey, serialized);

        var readBackJson = preferences.Get(JournalPreferenceKey, (string?)null);
        if (string.IsNullOrWhiteSpace(readBackJson))
        {
            return false;
        }

        OnboardingCompletionJournal? readBackJournal;
        try
        {
            readBackJournal = JsonSerializer.Deserialize(
                readBackJson,
                OnboardingJsonSerializerContext.Default.OnboardingCompletionJournal);
        }
        catch
        {
            return false;
        }

        if (readBackJournal is null)
        {
            return false;
        }

        if (!OnboardingCompletionJournalPolicy.IsValid(readBackJournal, out _))
        {
            return false;
        }

        if (!journal.Equals(readBackJournal))
        {
            return false;
        }

        return true;
    }

    public void Clear()
    {
        preferences.Remove(JournalPreferenceKey);
    }
}
