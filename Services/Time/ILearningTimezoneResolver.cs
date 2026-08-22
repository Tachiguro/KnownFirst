using KnownFirst.Core.Settings;

namespace KnownFirst.Services.Time;

public interface ILearningTimezoneResolver
{
    TimeZoneInfo ResolveEffectiveTimeZone(LearningTimezoneMode mode, string? explicitTimezoneId);

    string GetSystemTimeZoneId();

    TimeZoneInfo GetSystemTimeZone();
}
