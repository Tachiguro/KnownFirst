namespace KnownFirst.Core.Settings;

public static class ThemePreferencePolicy
{
    public static ThemePreference Normalize(int value) =>
        Enum.IsDefined(typeof(ThemePreference), value)
            ? (ThemePreference)value
            : ThemePreference.System;
}
