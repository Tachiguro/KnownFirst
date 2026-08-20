using System.Text.RegularExpressions;

namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// Validates that an upstream commit argument is an exact, fully-qualified Git commit SHA —
/// never a branch name, tag, or abbreviated hash — so no generator entry point can silently
/// accept an unpinned "latest" reference. Accepts exactly 40 <b>lowercase</b> hexadecimal
/// characters only, matching the spelling Git itself emits (e.g. <c>git rev-parse</c>); an
/// otherwise-valid uppercase or mixed-case SHA is rejected rather than silently normalized —
/// callers are expected to pass the commit exactly as Git produced it.
/// </summary>
public static partial class UpstreamCommitValidation
{
    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex Pattern();

    public static void EnsureValid(string commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        if (!Pattern().IsMatch(commit))
        {
            throw new ArgumentException(
                $"'{commit}' is not a valid upstream commit: expected exactly 40 lowercase hexadecimal " +
                "characters (a full Git commit SHA), not a branch name, tag, or abbreviated hash.",
                nameof(commit));
        }
    }
}
