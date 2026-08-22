using KnownFirst.Core.Settings;

namespace KnownFirst.Core.Learning;

public sealed record LearningDayConfiguration(
    LearningTimezoneMode TimezoneMode,
    string? ExplicitTimezoneId,
    int CutoffMinutes)
{
    public const int DefaultCutoffMinutes = 0; // 00:00 (midnight)

    public static LearningDayConfiguration Default => new(
        LearningTimezoneMode.System,
        null,
        DefaultCutoffMinutes);

    public static int NormalizeCutoffMinutes(int minutes) =>
        minutes is >= 0 and < 1440 ? minutes : DefaultCutoffMinutes;
}
