using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace KnownFirst.Services.Diagnostics;

public sealed class BuildIdentityService : IBuildIdentityService
{
    public BuildIdentityService()
    {
        Identity = CreateIdentity();
    }

    internal BuildIdentityService(BuildIdentity identity)
    {
        Identity = identity;
    }

    public BuildIdentity Identity { get; }

    public string FormatHeader()
    {
        var builder = new StringBuilder();
        builder.AppendLine("KnownFirst Build");
        builder.AppendLine($"Version: {Identity.Version}");
        builder.AppendLine($"Build: {Identity.BuildNumber}");
        builder.AppendLine($"Configuration: {Identity.Configuration}");
        builder.AppendLine($"Package: {Identity.PackageId}");
        
        var showCommit = Identity.Configuration is "Debug" or "BetaDiagnostic";
        builder.AppendLine($"Commit: {(showCommit ? Identity.CommitHash : "not included")}");
        builder.AppendLine($"Branch: {(showCommit ? Identity.Branch : "not included")}");
        builder.AppendLine($"Dirty: {(showCommit ? Identity.IsDirty.ToString() : "not included")}");
        
        builder.AppendLine($"OS: {Identity.OS}");
        builder.AppendLine($"Device: {Identity.Device}");
        builder.AppendLine($"Runtime: {Identity.Runtime}");
        builder.AppendLine($"Session: {Identity.SessionId}");
        
        return builder.ToString();
    }

    public string GetFormattedBuildIdentity()
    {
        if (Identity.Configuration == "Release")
        {
            return $"{Identity.Product} {Identity.Version} \u00B7 Build {Identity.BuildNumber}";
        }

        var suffix = Identity.IsDirty ? " \u00B7 DIRTY" : string.Empty;
        var configName = Identity.Configuration == "BetaDiagnostic" ? "Diagnostic" : "Debug";
        var productPrefix = Identity.Product.EndsWith(configName) ? Identity.Product : $"{Identity.Product} {configName}";
        
        return $"{productPrefix} {Identity.Version} \u00B7 Build {Identity.BuildNumber} \u00B7 Commit {Identity.ShortCommitHash}{suffix}";
    }

    private static BuildIdentity CreateIdentity()
    {
        var assembly = typeof(BuildIdentityService).Assembly;
        var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var commitHash = "unknown";
        var shortCommitHash = "unknown";
        var branch = "unknown";
        var isDirty = false;
        
        foreach (var attribute in attributes)
        {
            if (attribute.Key == "GitCommitHash") commitHash = attribute.Value ?? "unknown";
            if (attribute.Key == "GitShortCommitHash") shortCommitHash = attribute.Value ?? "unknown";
            if (attribute.Key == "GitBranchName") branch = attribute.Value ?? "unknown";
            if (attribute.Key == "GitIsDirty" && bool.TryParse(attribute.Value, out var dirtyParsed))
            {
                isDirty = dirtyParsed;
            }
        }

        string product = "KnownFirst";
        string version = "unknown";
        string buildNumber = "unknown";
        string packageId = "unknown";
        
        try
        {
#if ANDROID || WINDOWS
            product = Microsoft.Maui.ApplicationModel.AppInfo.Current.Name;
            version = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
            buildNumber = Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString;
            packageId = Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName;
#endif
        }
        catch
        {
            var name = assembly.GetName();
            version = name.Version?.ToString() ?? "unknown";
        }

        string os = RuntimeInformation.OSDescription;
        string osVersion = Environment.OSVersion.VersionString;
        string device = "unknown";
        
        try
        {
#if ANDROID || WINDOWS
            os = Microsoft.Maui.Devices.DeviceInfo.Current.Platform.ToString();
            osVersion = Microsoft.Maui.Devices.DeviceInfo.Current.VersionString;
            device = Microsoft.Maui.Devices.DeviceInfo.Current.Model;
#endif
        }
        catch
        {
            os = Environment.OSVersion.Platform.ToString();
        }

        string configuration = "Release";
#if KNOWNFIRST_DIAGNOSTICS
        configuration = "BetaDiagnostic";
#elif DEBUG
        configuration = "Debug";
#endif

        string runtime = RuntimeInformation.FrameworkDescription;
        string sessionId = Guid.NewGuid().ToString("N");

        return new BuildIdentity(
            product,
            version,
            buildNumber,
            packageId,
            configuration,
            commitHash,
            shortCommitHash,
            branch,
            os,
            osVersion,
            device,
            runtime,
            sessionId,
            isDirty);
    }
}
