namespace KnownFirst.Core.Preparation;

public static class SourceReferencePolicy
{
    private const string CreativeCommonsShareAlikeName =
        "Creative Commons Attribution-ShareAlike 4.0 International";

    private static readonly Uri CreativeCommonsShareAlikeUri =
        new("https://creativecommons.org/licenses/by-sa/4.0/");

    public static Uri? CreatePageUri(string sourceProject, string pageTitle)
    {
        if (string.IsNullOrWhiteSpace(sourceProject) || string.IsNullOrWhiteSpace(pageTitle))
        {
            return null;
        }

        if (sourceProject.Contains('/')
            || sourceProject.Contains(':')
            || sourceProject.Contains('@')
            || sourceProject.Contains('?')
            || sourceProject.Contains('#')
            || sourceProject.Any(char.IsWhiteSpace))
        {
            return null;
        }

        if (Uri.CheckHostName(sourceProject) != UriHostNameType.Dns)
        {
            return null;
        }

        string host = sourceProject.Trim();

        string? prefix = null;
        if (host.EndsWith(".wiktionary.org", StringComparison.OrdinalIgnoreCase))
        {
            prefix = host.Substring(0, host.Length - ".wiktionary.org".Length);
        }
        else if (host.EndsWith(".wikipedia.org", StringComparison.OrdinalIgnoreCase))
        {
            prefix = host.Substring(0, host.Length - ".wikipedia.org".Length);
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        if (prefix.Contains('.') || !prefix.All(c => char.IsLetterOrDigit(c) || c == '-'))
        {
            return null;
        }

        var escapedTitle = Uri.EscapeDataString(pageTitle.Replace(' ', '_'));
        return new Uri($"https://{host.ToLowerInvariant()}/wiki/{escapedTitle}");
    }

    public static string? GetLicenseReference(string attribution)
    {
        if (string.IsNullOrWhiteSpace(attribution))
        {
            return null;
        }

        return IsCreativeCommonsShareAlike(attribution)
            ? CreativeCommonsShareAlikeName
            : null;
    }

    public static Uri? GetLicenseUri(string attribution)
    {
        if (string.IsNullOrWhiteSpace(attribution))
        {
            return null;
        }

        return IsCreativeCommonsShareAlike(attribution)
            ? CreativeCommonsShareAlikeUri
            : null;
    }

    private static bool IsCreativeCommonsShareAlike(string attribution)
    {
        return attribution.Contains("Creative Commons Attribution-ShareAlike", StringComparison.OrdinalIgnoreCase)
            || attribution.Contains("CC BY-SA", StringComparison.OrdinalIgnoreCase);
    }
}
