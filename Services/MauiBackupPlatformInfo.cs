using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Diagnostics;
using Microsoft.Maui.Devices;

namespace KnownFirst.Services;

public sealed class MauiBackupPlatformInfo(
    IBuildIdentityService buildIdentityService) : IBackupPlatformInfo
{
    public BackupSourcePlatform SourcePlatform =>
        DeviceInfo.Current.Platform == DevicePlatform.Android
            ? BackupSourcePlatform.Android
            : BackupSourcePlatform.Windows;

    public string SourceAppVersion => buildIdentityService.Identity.Version;
}
