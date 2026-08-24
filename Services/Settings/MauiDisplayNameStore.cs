using KnownFirst.Core.Settings;
using Microsoft.Maui.Storage;

namespace KnownFirst.Services.Settings;

/// <summary>
/// Preference-backed Display Name, following the same minimal single-key shape as
/// <c>MauiOnboardingStateStore</c> and <c>MauiWhatsNewPreferenceStore</c>. The concrete preference
/// key stays in this application-layer store; <c>KnownFirst.Core</c> owns only the normalization
/// policy.
/// </summary>
public sealed class MauiDisplayNameStore(IPreferences preferences) : IDisplayNameStore
{
    internal const string DisplayNamePreferenceKey = "display_name";

    public string? GetDisplayName() =>
        DisplayNamePolicy.Normalize(preferences.Get(DisplayNamePreferenceKey, (string?)null));

    public void SetDisplayName(string? displayName)
    {
        var normalized = DisplayNamePolicy.Normalize(displayName);
        if (normalized is null)
        {
            // Removed rather than stored blank, so "no name" has exactly one representation in the
            // preference store and a later read can never see an empty-but-present value.
            preferences.Remove(DisplayNamePreferenceKey);
            return;
        }

        preferences.Set(DisplayNamePreferenceKey, normalized);
    }
}
