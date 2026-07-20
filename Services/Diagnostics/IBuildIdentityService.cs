namespace KnownFirst.Services.Diagnostics;

public interface IBuildIdentityService
{
    BuildIdentity Identity { get; }

    string FormatHeader();
    string GetFormattedBuildIdentity();
}
