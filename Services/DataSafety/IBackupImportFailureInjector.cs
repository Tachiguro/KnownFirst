namespace KnownFirst.Services.DataSafety;

public interface IBackupImportFailureInjector
{
    void AfterMutation(int mutationCount);

    /// <summary>
    /// KF-MEANING-001 Slice 2 correction: named Schema-8 restore checkpoints, semantically stable
    /// regardless of how many raw mutations occur before them (unlike <see cref="AfterMutation"/>'s
    /// count, which shifts whenever an unrelated earlier step gains or loses a row). Default no-op so
    /// every existing <see cref="AfterMutation"/>-only injector (v1 and the pre-existing v2 tests)
    /// keeps compiling and behaving unchanged.
    /// </summary>
    void AtCheckpoint(string checkpointName) { }
}
