using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KnownFirst.Services.Lexical;

public sealed class WiktionaryLookupProvider : IDictionaryLookupProvider
{
    public const string Name = "Wiktionary";
    public const int SchemaVersion = 3;
    public const string UserAgent =
        "KnownFirst/1.0 (https://github.com/Tachiguro/KnownFirst; read-only dictionary lookup)";
    public const string AttributionText =
        "Wiktionary contributors; text available under the Creative Commons Attribution-ShareAlike license.";

    private const int MaximumAttempts = 3;
    private readonly HttpClient _httpClient;
    private readonly WiktionaryHtmlParser _parser;
    private readonly IClock _clock;
    private readonly IAsyncDelay _delay;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _concurrencyGate = new(2, 2);

    public WiktionaryLookupProvider(
        HttpClient httpClient,
        WiktionaryHtmlParser parser,
        IClock clock,
        IAsyncDelay delay,
        TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient;
        _parser = parser;
        _clock = clock;
        _delay = delay;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
    }

    public string ProviderName => Name;

    public int ProviderSchemaVersion => SchemaVersion;

    public async Task<LexicalResult> LookupAsync(
        LexicalLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateLanguage(request.SourceLanguage);
        LexicalLookupLanguagePolicy.Validate(
            request.SourceLanguage,
            request.LookupMode,
            request.TargetLanguage);
        if (!string.Equals(request.Provider, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The request is for a different lexical provider.", nameof(request));
        }

        await _concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            return await SendWithRetryAsync(request, cancellationToken);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    public static Uri CreateRequestUri(LexicalLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resultLanguage = request.TargetLanguage ?? request.SourceLanguage;
        ValidateLanguage(resultLanguage);
        var host = resultLanguage.Equals("de", StringComparison.OrdinalIgnoreCase)
            ? "de.wiktionary.org"
            : "en.wiktionary.org";
        var query = string.Join('&',
            "action=parse",
            "format=json",
            "formatversion=2",
            "prop=text%7Crevid",
            "redirects=1",
            "disabletoc=1",
            $"uselang={Uri.EscapeDataString(resultLanguage.ToLowerInvariant())}",
            $"page={Uri.EscapeDataString(request.CanonicalLookupTerm)}");
        return new UriBuilder(Uri.UriSchemeHttps, host)
        {
            Path = "/w/api.php",
            Query = query
        }.Uri;
    }

    public string DescribeRequest(LexicalLookupRequest request) => CreateRequestUri(request).AbsoluteUri;

    private async Task<LexicalResult> SendWithRetryAsync(
        LexicalLookupRequest request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(_requestTimeout);
                using var message = new HttpRequestMessage(HttpMethod.Get, CreateRequestUri(request));
                message.Headers.UserAgent.ParseAdd(UserAgent);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt == MaximumAttempts - 1)
                    {
                        return Failure(request, LexicalLookupStatus.TransientFailure, "rate-limited");
                    }

                    await _delay.DelayAsync(GetRetryDelay(response.Headers.RetryAfter, attempt), cancellationToken);
                    continue;
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt == MaximumAttempts - 1)
                    {
                        return Failure(request, LexicalLookupStatus.TransientFailure, "transient-server-error");
                    }

                    await _delay.DelayAsync(GetBackoff(attempt), cancellationToken);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Failure(request, LexicalLookupStatus.NotFound, "missing-page");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Failure(
                        request,
                        LexicalLookupStatus.PermanentFailure,
                        $"http-{(int)response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync(timeoutSource.Token);
                return ParseResponse(request, json);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure(request, LexicalLookupStatus.TransientFailure, "timeout");
            }
            catch (HttpRequestException) when (attempt < MaximumAttempts - 1)
            {
                await _delay.DelayAsync(GetBackoff(attempt), cancellationToken);
            }
            catch (HttpRequestException)
            {
                return Failure(request, LexicalLookupStatus.TransientFailure, "network-unavailable");
            }
            catch (JsonException)
            {
                return Failure(request, LexicalLookupStatus.ParseFailure, "malformed-json");
            }
        }

        return Failure(request, LexicalLookupStatus.TransientFailure, "network-unavailable");
    }

    private LexicalResult ParseResponse(LexicalLookupRequest request, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString() ?? "api-error"
                : "api-error";
            return Failure(
                request,
                code.Contains("missing", StringComparison.OrdinalIgnoreCase)
                    ? LexicalLookupStatus.NotFound
                    : LexicalLookupStatus.PermanentFailure,
                code);
        }

        if (!root.TryGetProperty("parse", out var parsed)
            || !parsed.TryGetProperty("text", out var textElement))
        {
            return Failure(request, LexicalLookupStatus.ParseFailure, "missing-parse-payload");
        }

        var html = textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString()
            : textElement.TryGetProperty("*", out var legacyText)
                ? legacyText.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(html))
        {
            return Failure(request, LexicalLookupStatus.ParseFailure, "missing-html");
        }

        var parsedEntry = _parser.ParseEntry(html, request.SourceLanguage, request.ExplanationLanguage);
        var meanings = SelectMeanings(parsedEntry.DirectMeanings, request.LookupMode);
        if (meanings.Count == 0 && parsedEntry.FormRelations.Count == 0)
        {
            return Failure(request, LexicalLookupStatus.NotFound, "language-section-not-found");
        }

        var pageTitle = parsed.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString() ?? request.CanonicalLookupTerm
            : request.CanonicalLookupTerm;
        long? revisionId = parsed.TryGetProperty("revid", out var revisionElement)
            && revisionElement.TryGetInt64(out var parsedRevision)
                ? parsedRevision
                : null;
        var project = request.ExplanationLanguage.Equals("de", StringComparison.OrdinalIgnoreCase)
            ? "de.wiktionary.org"
            : "en.wiktionary.org";
        return new LexicalResult(
            LexicalLookupStatus.Success,
            request.NormalizedLemma,
            request.Term,
            request.TokenKind,
            request.SourceLanguage,
            request.ExplanationLanguage,
            null,
            meanings,
            Name,
            project,
            pageTitle,
            revisionId,
            AttributionText,
            _clock.UtcNow,
            FormRelations: parsedEntry.FormRelations,
            LookupMode: request.LookupMode,
            TargetLanguage: request.TargetLanguage);
    }

    private static IReadOnlyList<LexicalMeaning> SelectMeanings(
        IReadOnlyList<LexicalMeaning> meanings,
        LexicalLookupMode lookupMode) => lookupMode switch
        {
            LexicalLookupMode.Definition => meanings
                .Where(meaning => !string.IsNullOrWhiteSpace(meaning.Definition))
                .Select(meaning => meaning with { Translation = null })
                .ToArray(),
            LexicalLookupMode.Translation => meanings
                .Where(meaning => !string.IsNullOrWhiteSpace(meaning.Translation))
                .Select(meaning => meaning with { Definition = string.Empty })
                .ToArray(),
            LexicalLookupMode.DefinitionAndTranslation => meanings
                .Where(meaning => !string.IsNullOrWhiteSpace(meaning.Definition)
                    || !string.IsNullOrWhiteSpace(meaning.Translation))
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(lookupMode))
        };

    private TimeSpan GetRetryDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date.UtcDateTime - _clock.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return GetBackoff(attempt);
    }

    private static TimeSpan GetBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));

    private LexicalResult Failure(
        LexicalLookupRequest request,
        LexicalLookupStatus status,
        string errorCode) => new(
        status,
        request.NormalizedLemma,
        request.Term,
        request.TokenKind,
        request.SourceLanguage,
        request.ExplanationLanguage,
        null,
        [],
        Name,
        request.ExplanationLanguage.Equals("de", StringComparison.OrdinalIgnoreCase)
            ? "de.wiktionary.org"
            : "en.wiktionary.org",
        request.Term,
        null,
        AttributionText,
        _clock.UtcNow,
        ErrorCode: errorCode,
        LookupMode: request.LookupMode,
        TargetLanguage: request.TargetLanguage);

    private static void ValidateLanguage(string language)
    {
        if (language is not ("en" or "de"))
        {
            throw new ArgumentException("Only English and German are supported.", nameof(language));
        }
    }
}
