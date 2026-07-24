namespace KnownFirst.Services.DataSafety;

public interface IBackupImportFailureInjector
{
    void AfterMutation(int mutationCount);
}
