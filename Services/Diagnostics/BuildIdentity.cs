namespace KnownFirst.Services.Diagnostics;

public sealed record BuildIdentity(
    string Product,
    string Version,
    string BuildNumber,
    string PackageId,
    string Configuration,
    string CommitHash,
    string ShortCommitHash,
    string Branch,
    string OS,
    string OSVersion,
    string Device,
    string Runtime,
    string SessionId,
    bool IsDirty);
