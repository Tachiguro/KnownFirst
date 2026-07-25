using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public interface IBackupPlatformInfo
{
    BackupSourcePlatform SourcePlatform { get; }
    string SourceAppVersion { get; }
}
