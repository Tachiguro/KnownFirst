namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// Validates that upstream <c>LICENSE</c> text still designates CC BY-SA 4.0 (Attribution-
/// ShareAlike 4.0) before generation may proceed from it. This is the governing decision for the
/// pinned DuyguA/german-morph-dictionaries data: generation fails closed the moment the expected
/// upstream license marker is no longer present, rather than silently continuing under a
/// materially changed license.
/// </summary>
public static class UpstreamLicenseValidation
{
    private const string ExpectedMarker = "Attribution-ShareAlike 4.0";

    public static void EnsureValid(string licenseText)
    {
        ArgumentNullException.ThrowIfNull(licenseText);

        if (!licenseText.Contains(ExpectedMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Upstream LICENSE text no longer contains the expected '{ExpectedMarker}' marker. " +
                "Stopping: the governing data license may have materially changed.");
        }
    }
}
