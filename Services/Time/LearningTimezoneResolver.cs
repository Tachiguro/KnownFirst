using KnownFirst.Core.Settings;

namespace KnownFirst.Services.Time;

public sealed class LearningTimezoneResolver : ILearningTimezoneResolver
{
    public TimeZoneInfo ResolveEffectiveTimeZone(LearningTimezoneMode mode, string? explicitTimezoneId)
    {
        if (mode == LearningTimezoneMode.Explicit && !string.IsNullOrWhiteSpace(explicitTimezoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(explicitTimezoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall back to System timezone if explicit ID cannot be found on current OS
            }
            catch (InvalidTimeZoneException)
            {
                // Fall back to System timezone if data is corrupt
            }
        }

        return GetSystemTimeZone();
    }

    public string GetSystemTimeZoneId() => TimeZoneInfo.Local.Id;

    public TimeZoneInfo GetSystemTimeZone() => TimeZoneInfo.Local;
}
