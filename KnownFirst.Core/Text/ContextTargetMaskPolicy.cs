using System.Globalization;

namespace KnownFirst.Core.Text;

/// <summary>
/// Provides Unicode-safe masking for context target spans based on text element (grapheme cluster) count.
/// </summary>
public static class ContextTargetMaskPolicy
{
    private const char DefaultMaskCharacter = '_';

    /// <summary>
    /// Computes a mask string containing one mask character per Unicode text element (grapheme cluster) in the target.
    /// </summary>
    /// <param name="target">The target string to mask.</param>
    /// <param name="maskCharacter">The masking character, defaulting to '_'.</param>
    /// <returns>A string with the mask character repeated for each text element, or an empty string if target is null or empty.</returns>
    public static string CreateMask(string? target, char maskCharacter = DefaultMaskCharacter)
    {
        if (string.IsNullOrEmpty(target))
        {
            return string.Empty;
        }

        var textElementCount = new StringInfo(target).LengthInTextElements;
        return new string(maskCharacter, textElementCount);
    }
}
