using KnownFirst.Services.Study;

namespace KnownFirst.Services.Isolation;

/// <summary>
/// Debug/GUI-test-only fault-injection resolver (KF-MEANING-002 GUI hardening, Part 3, Scenario
/// PreparationAcceptFailureRecovery). Reuses the existing <see cref="IPreparationFaultInjector"/> seam
/// that <see cref="PreparationService"/> already exposes for its own unit tests — this class only decides
/// *whether* one should be wired into production DI, and fails exactly like <see cref="GuiTestProfile"/>:
/// an env var that is set but the isolated GUI test profile is not active throws rather than silently
/// activating (or silently not activating) fault injection.
/// </summary>
public static class GuiTestFaultInjection
{
    public const string EnvironmentVariableName = "KNOWNFIRST_GUI_TEST_FAULT_CHECKPOINT";

    /// <summary>
    /// Reads the requested checkpoint name, or null when fault injection is not requested. Fails closed
    /// when the env var is set but <see cref="GuiTestProfile.IsActive"/> is false: fault injection must
    /// never be reachable outside the isolated GUI-test profile.
    /// </summary>
    public static string? Resolve()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!GuiTestProfile.IsActive)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set, but the isolated GUI test profile is not active. " +
                "Failing closed instead of risking fault injection against a non-test profile.");
        }

        return raw.Trim();
    }

    public static bool IsActive => Resolve() is not null;

    /// <summary>
    /// A fault injector that throws exactly once, the first time <see cref="AtCheckpoint"/> is called with
    /// the requested checkpoint name, and never on any other checkpoint or on subsequent calls to the same
    /// one (so a retry attempted by the scenario after disabling the fault can succeed).
    /// </summary>
    public sealed class SingleShotInjector(string checkpointName) : IPreparationFaultInjector
    {
        private bool _fired;

        public void AtCheckpoint(string observedCheckpoint)
        {
            if (_fired || !string.Equals(observedCheckpoint, checkpointName, StringComparison.Ordinal))
            {
                return;
            }

            _fired = true;
            throw new InvalidOperationException(
                $"GUI-test fault injection: deterministic failure at checkpoint '{observedCheckpoint}' " +
                $"(requested via {EnvironmentVariableName}).");
        }
    }
}
