namespace KnownFirst.Services.Lexical;

public sealed class OnlineLookupAuthorizationHandler : DelegatingHandler
{
    private readonly IOnlineLookupAuthorizationGate _authorizationGate;

    public OnlineLookupAuthorizationHandler(
        IOnlineLookupAuthorizationGate authorizationGate,
        HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
        _authorizationGate = authorizationGate ?? throw new ArgumentNullException(nameof(authorizationGate));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _authorizationGate.EnsureAuthorized();

        using var linkedCts = _authorizationGate.CreateLinkedCancellationTokenSource(cancellationToken);
        return await base.SendAsync(request, linkedCts.Token);
    }

    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _authorizationGate.EnsureAuthorized();

        using var linkedCts = _authorizationGate.CreateLinkedCancellationTokenSource(cancellationToken);
        return base.Send(request, linkedCts.Token);
    }
}
