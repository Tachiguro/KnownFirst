namespace KnownFirst.Core.Settings;

/// <summary>
/// Single source of truth for the Enhanced Term Recognition default. A missing
/// <c>enhanced_term_recognition_enabled</c> preference resolves to this value; an explicitly
/// persisted value always wins over it.
/// </summary>
public static class EnhancedTermRecognitionPolicy
{
    public const bool DefaultEnabled = true;
}
