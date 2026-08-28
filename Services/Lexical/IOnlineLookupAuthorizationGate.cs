namespace KnownFirst.Services.Lexical;

public interface IOnlineLookupAuthorizationGate
{
    bool IsAuthorized { get; }

    CancellationToken CurrentEpochToken { get; }

    CancellationTokenSource CreateLinkedCancellationTokenSource(CancellationToken callerToken = default);

    void EnsureAuthorized();
}
