namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Narrow test seam for the two non-deterministic inputs to a safety-copy filename: the current UTC
/// timestamp and a unique, filesystem-safe short identifier. Production always uses
/// <see cref="SystemMergeSafetyCopyIdentityProvider"/>; tests supply a fixed implementation so
/// filenames and metadata timestamps are reproducible.
/// </summary>
public interface IMergeSafetyCopyIdentityProvider
{
    DateTime UtcNow { get; }

    string NewShortId();
}

public sealed class SystemMergeSafetyCopyIdentityProvider : IMergeSafetyCopyIdentityProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public string NewShortId() => Guid.NewGuid().ToString("N")[..12];
}
