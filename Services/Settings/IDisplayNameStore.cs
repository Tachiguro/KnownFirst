namespace KnownFirst.Services.Settings;

/// <summary>
/// Durable storage for the optional local Display Name. Like the theme, language, onboarding, and
/// What's New markers, it is device-local application state and lives in the preference layer —
/// never in the SQLite user database, which would make it part of the portable archive and backup
/// contracts.
/// <para>
/// It deliberately has its own store rather than joining <c>IAppSettingsService</c>: that service's
/// <c>Reset()</c> removes every key it owns, and it is exactly what the non-destructive "Restore
/// default settings" action calls. Keeping the Display Name outside it means the name survives that
/// action by construction, with no special-case preservation logic in either reset flow. The
/// destructive full reset still removes it, because that flow clears the whole preference store.
/// </para>
/// </summary>
public interface IDisplayNameStore
{
    /// <summary>
    /// The stored Display Name, or <see langword="null"/> when the user has not set one.
    /// </summary>
    string? GetDisplayName();

    /// <summary>
    /// Stores the normalized Display Name. Input that normalizes to absent — <see langword="null"/>,
    /// empty, or whitespace-only — removes the name instead of storing a blank value.
    /// </summary>
    void SetDisplayName(string? displayName);
}
