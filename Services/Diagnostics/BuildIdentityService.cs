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
        var displayConfig = Identity.Configuration switch
        {
            "BetaDiagnostic" => "Diagnostic",
            var config => config
        };

        var productName = Identity.Product;
        if (productName.EndsWith(" Debug", StringComparison.OrdinalIgnoreCase))
        {
            productName = productName[..^6];
        }
        else if (productName.EndsWith(" Diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            productName = productName[..^11];
        }

        var isPrerelease = Identity.Version.Contains('-', StringComparison.Ordinal);
        var includeCommit = Identity.Configuration is "Debug" or "BetaDiagnostic" || isPrerelease;

        var commitPart = includeCommit ? $" \u00B7 Commit {Identity.ShortCommitHash}" : string.Empty;
        var dirtyPart = includeCommit && Identity.IsDirty ? " \u00B7 DIRTY" : string.Empty;

        return $"{productName} \u00B7 {Identity.Version} \u00B7 {displayConfig} \u00B7 Build {Identity.BuildNumber}{commitPart}{dirtyPart}";
    }

    private static BuildIdentity CreateIdentity()
    {
        var assembly = typeof(BuildIdentityService).Assembly;
        AppInfoData? appInfo = null;
        try
        {
#if ANDROID || WINDOWS
            appInfo = new AppInfoData(
                Microsoft.Maui.ApplicationModel.AppInfo.Current.Name,
                Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString,
                Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString,
                Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName);
#endif
        }
        catch
        {
            appInfo = null;
        }

        DeviceInfoData? deviceInfo = null;
        try
        {
#if ANDROID || WINDOWS
            deviceInfo = new DeviceInfoData(
                Microsoft.Maui.Devices.DeviceInfo.Current.Platform.ToString(),
                Microsoft.Maui.Devices.DeviceInfo.Current.VersionString,
                Microsoft.Maui.Devices.DeviceInfo.Current.Model);
#endif
        }
        catch
        {
            deviceInfo = null;
        }

        return ResolveIdentity(assembly, appInfo, deviceInfo);
    }

    internal static BuildIdentity ResolveIdentity(
        Assembly assembly,
        AppInfoData? appInfo = null,
        DeviceInfoData? deviceInfo = null,
        IDictionary<string, string>? metadataOverrides = null)
    {
        var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        string? metadataProduct = metadataOverrides?.TryGetValue("KnownFirstProductName", out var p) == true ? p : null;
        string? metadataVersion = metadataOverrides?.TryGetValue("KnownFirstProductVersion", out var v) == true ? v : null;
        string? metadataBuild = metadataOverrides?.TryGetValue("KnownFirstBuildNumber", out var b) == true ? b : null;
        string commitHash = metadataOverrides?.TryGetValue("GitCommitHash", out var ch) == true ? ch : "unknown";
        string shortCommitHash = metadataOverrides?.TryGetValue("GitShortCommitHash", out var sch) == true ? sch : "unknown";
        string branch = metadataOverrides?.TryGetValue("GitBranchName", out var br) == true ? br : "unknown";
        bool isDirty = metadataOverrides?.TryGetValue("GitIsDirty", out var dStr) == true && bool.TryParse(dStr, out var dParsed) ? dParsed : false;

        foreach (var attribute in attributes)
        {
            if (metadataProduct is null && attribute.Key == "KnownFirstProductName" && !string.IsNullOrWhiteSpace(attribute.Value))
                metadataProduct = attribute.Value;
            if (metadataVersion is null && attribute.Key == "KnownFirstProductVersion" && !string.IsNullOrWhiteSpace(attribute.Value))
                metadataVersion = attribute.Value;
            if (metadataBuild is null && attribute.Key == "KnownFirstBuildNumber" && !string.IsNullOrWhiteSpace(attribute.Value))
                metadataBuild = attribute.Value;
            if (commitHash == "unknown" && attribute.Key == "GitCommitHash") commitHash = attribute.Value ?? "unknown";
            if (shortCommitHash == "unknown" && attribute.Key == "GitShortCommitHash") shortCommitHash = attribute.Value ?? "unknown";
            if (branch == "unknown" && attribute.Key == "GitBranchName") branch = attribute.Value ?? "unknown";
            if (!isDirty && attribute.Key == "GitIsDirty" && bool.TryParse(attribute.Value, out var dirtyParsed))
            {
                isDirty = dirtyParsed;
            }
        }

        string product = metadataProduct
            ?? appInfo?.Name
            ?? "KnownFirst";

        string version = metadataVersion
            ?? appInfo?.VersionString
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        string buildNumber = appInfo?.BuildString
            ?? metadataBuild
            ?? "unknown";

        string packageId = appInfo?.PackageName ?? "unknown";

        string os = deviceInfo?.Platform ?? RuntimeInformation.OSDescription;
        string osVersion = deviceInfo?.VersionString ?? Environment.OSVersion.VersionString;
        string device = deviceInfo?.Model ?? "unknown";

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

internal sealed record AppInfoData(string Name, string VersionString, string BuildString, string PackageName);
internal sealed record DeviceInfoData(string Platform, string VersionString, string Model);
