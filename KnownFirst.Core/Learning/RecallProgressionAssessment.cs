namespace KnownFirst.Core.Learning;

/// <summary>
/// Represents the distinct progression outcomes for a recall assessment:
/// Advance (Good, Easy), Hold (Hard), or Reset (Again).
/// </summary>
public enum RecallProgressionAssessment
{
    Reset = 0,
    Hold = 1,
    Advance = 2
}
