namespace KnownFirst.Core.Preparation;

public static class SourceReferencePolicy
{
    private const string CreativeCommonsShareAlikeName =
        "Creative Commons Attribution-ShareAlike";

    public static Uri? CreatePageUri(string sourceProject, string pageTitle)
    {
        if (string.IsNullOrWhiteSpace(sourceProject)
            || string.IsNullOrWhiteSpace(pageTitle)
            || !sourceProject.EndsWith(".wiktionary.org", StringComparison.OrdinalIgnoreCase)
            || Uri.CheckHostName(sourceProject) == UriHostNameType.Unknown)
        {
            return null;
        }

        var escapedTitle = Uri.EscapeDataString(pageTitle.Replace(' ', '_'));
        return new Uri($"https://{sourceProject}/wiki/{escapedTitle}");
    }

    public static string? GetLicenseReference(string attribution) =>
        attribution.Contains(CreativeCommonsShareAlikeName, StringComparison.OrdinalIgnoreCase)
            ? CreativeCommonsShareAlikeName
            : null;
}
