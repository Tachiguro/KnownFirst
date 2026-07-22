using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

using KnownFirst.Core.Learning;

namespace KnownFirst.Services.Lexical.Wikipedia;

public sealed partial class WikipediaApiClient : IWikipediaApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IClock _clock;
    private readonly TimeSpan _requestTimeout;
    private const string UserAgent = "KnownFirst/1.0 (https://github.com/Tachiguro/KnownFirst; read-only Wikipedia lookup)";

    public WikipediaApiClient(HttpClient httpClient, IClock? clock = null, TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient;
        _clock = clock ?? new SystemClock();
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Request timeout must be greater than zero.");
        }
    }

    public async Task<WikipediaArticleResult> GetArticleAsync(
        WikipediaArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_requestTimeout);
        
        var queryParams = new List<string>
        {
            "action=query",
            "format=json",
            "formatversion=2",
            "redirects=1",
            "prop=info|extracts|pageprops|langlinks",
            "inprop=url",
            "exintro=1",
            "explaintext=1",
            "exchars=1200",
            "ppprop=disambiguation",
            $"titles={Uri.EscapeDataString(request.RequestedTitle)}"
        };

        if (!string.IsNullOrWhiteSpace(request.TargetLanguage))
        {
            queryParams.Add($"lllang={Uri.EscapeDataString(request.TargetLanguage)}");
            queryParams.Add("lllimit=1");
            queryParams.Add("llprop=url");
        }

        var uriBuilder = new UriBuilder($"https://{request.SourceLanguage}.wikipedia.org/w/api.php")
        {
            Query = string.Join("&", queryParams)
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            return CreateFailure(request, WikipediaArticleStatus.TimedOut, "timeout");
        }
        catch (HttpRequestException)
        {
            return CreateFailure(request, WikipediaArticleStatus.TransientFailure, "http-request-error");
        }
        catch (Exception)
        {
            return CreateFailure(request, WikipediaArticleStatus.TransientFailure, "network-error");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                TimeSpan? retryDelta = null;
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Delta.HasValue)
                    {
                        retryDelta = response.Headers.RetryAfter.Delta.Value;
                    }
                    else if (response.Headers.RetryAfter.Date.HasValue)
                    {
                        var delta = response.Headers.RetryAfter.Date.Value.UtcDateTime - _clock.UtcNow;
                        retryDelta = delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
                    }
                }
                return CreateFailure(request, WikipediaArticleStatus.RateLimited, "rate-limited", retryDelta);
            }

            if (response.StatusCode == HttpStatusCode.RequestTimeout || ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599))
            {
                return CreateFailure(request, WikipediaArticleStatus.TransientFailure, $"http-{(int)response.StatusCode}");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return CreateFailure(request, WikipediaArticleStatus.NotFound, "http-404");
            }

            if (!response.IsSuccessStatusCode)
            {
                return CreateFailure(request, WikipediaArticleStatus.PermanentFailure, $"http-{(int)response.StatusCode}");
            }

            WikipediaApiResponse? apiResponse;
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                apiResponse = await JsonSerializer.DeserializeAsync(stream, WikipediaJsonSerializerContext.Default.WikipediaApiResponse, cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return CreateFailure(request, WikipediaArticleStatus.TimedOut, "timeout");
            }
            catch (JsonException)
            {
                return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "json-error");
            }
            catch (Exception)
            {
                return CreateFailure(request, WikipediaArticleStatus.TransientFailure, "network-error-parsing");
            }

            if (apiResponse == null)
            {
                return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "empty-response");
            }

            if (apiResponse.Error != null)
            {
                var code = NormalizeApiErrorCode(apiResponse.Error.Code);
                if (code is null)
                {
                    return CreateFailure(request, WikipediaArticleStatus.PermanentFailure, "api-error-unknown");
                }

                if (code == "ratelimited" || code == "ratelimit")
                {
                    return CreateFailure(request, WikipediaArticleStatus.RateLimited, "rate-limited");
                }
                if (code == "maxlag" || code == "internal_api_error")
                {
                    return CreateFailure(request, WikipediaArticleStatus.TransientFailure, $"api-{code}");
                }
                return CreateFailure(request, WikipediaArticleStatus.PermanentFailure, $"api-{code}");
            }

            var query = apiResponse.Query;
            if (query == null || query.Pages == null || query.Pages.Count == 0)
            {
                return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "missing-query-payload");
            }

            if (query.Pages.Count > 1)
            {
                return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "multiple-pages");
            }

            var page = query.Pages[0];

            if (page.Missing == true)
            {
                return CreateFailure(request, WikipediaArticleStatus.NotFound, "page-missing");
            }

            if (string.IsNullOrWhiteSpace(page.Title))
            {
                return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "missing-page-title");
            }

            // Normalization & Redirects
            var canonicalTitle = request.RequestedTitle;
            if (query.Normalized != null)
            {
                foreach (var norm in query.Normalized)
                {
                    if (string.Equals(norm.From, canonicalTitle, StringComparison.OrdinalIgnoreCase) && norm.To != null)
                    {
                        canonicalTitle = norm.To;
                    }
                }
            }

            var isRedirect = false;
            string? redirectedFrom = null;

            if (query.Redirects != null && query.Redirects.Count > 0)
            {
                var redirectsList = query.Redirects.ToList();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (true)
                {
                    var match = redirectsList.FirstOrDefault(r => string.Equals(r.From, canonicalTitle, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                    {
                        break;
                    }

                    if (match.To == null)
                    {
                        return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "redirect-missing-to");
                    }

                    if (!visited.Add(match.From!))
                    {
                        return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "redirect-cycle");
                    }

                    if (!isRedirect)
                    {
                        redirectedFrom = match.From;
                        isRedirect = true;
                    }

                    canonicalTitle = match.To;
                }

                if (visited.Count != query.Redirects.Count)
                {
                    return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "redirect-disconnected");
                }
            }

            if (!string.Equals(page.Title, canonicalTitle, StringComparison.OrdinalIgnoreCase))
            {
                return CreateFailure(request, WikipediaArticleStatus.ParseFailure, "redirect-final-mismatch");
            }
            canonicalTitle = page.Title;

            if (page.PageProps?.Disambiguation != null)
            {
                return CreateFailure(request, WikipediaArticleStatus.Disambiguation, "disambiguation", canonicalTitle: canonicalTitle, isRedirect: isRedirect, redirectedFrom: redirectedFrom, page: page);
            }

            var extract = page.Extract;
            if (string.IsNullOrWhiteSpace(extract))
            {
                return CreateFailure(request, WikipediaArticleStatus.NoUsableContent, "empty-extract", canonicalTitle: canonicalTitle, isRedirect: isRedirect, redirectedFrom: redirectedFrom, page: page);
            }

            extract = NormalizeExtract(extract);
            if (string.IsNullOrWhiteSpace(extract))
            {
                return CreateFailure(request, WikipediaArticleStatus.NoUsableContent, "empty-extract-after-normalization", canonicalTitle: canonicalTitle, isRedirect: isRedirect, redirectedFrom: redirectedFrom, page: page);
            }

            if (extract.Length > 1200)
            {
                extract = extract.Substring(0, 1200);
            }

            string? targetTitleCandidate = null;
            string? targetUrlCandidate = null;

            if (page.LangLinks != null && page.LangLinks.Count > 0)
            {
                // we requested exactly one lang, so if there is one, it's the target
                var ll = page.LangLinks[0];
                if (ll.Lang == request.TargetLanguage)
                {
                    targetTitleCandidate = ll.Title;
                    targetUrlCandidate = ll.Url;
                }
            }

            return new WikipediaArticleResult(
                Status: WikipediaArticleStatus.Success,
                RequestedTitle: request.RequestedTitle,
                CanonicalTitle: canonicalTitle,
                Extract: extract,
                SourceLanguage: request.SourceLanguage,
                SourceProject: $"{request.SourceLanguage}.wikipedia.org",
                PageId: page.PageId ?? 0,
                RevisionId: page.LastRevId ?? 0,
                CanonicalUrl: page.CanonicalUrl ?? "",
                IsRedirect: isRedirect,
                RedirectedFrom: redirectedFrom,
                TargetLanguage: request.TargetLanguage,
                TargetTitleCandidate: targetTitleCandidate,
                TargetUrlCandidate: targetUrlCandidate,
                Attribution: "Wikipedia contributors; text available under the Creative Commons Attribution-ShareAlike license.",
                ErrorCode: null,
                RetryAfter: null
            );
        }
    }

    private static WikipediaArticleResult CreateFailure(
        WikipediaArticleRequest request,
        WikipediaArticleStatus status,
        string errorCode,
        TimeSpan? retryAfter = null,
        string? canonicalTitle = null,
        bool isRedirect = false,
        string? redirectedFrom = null,
        WikipediaApiPage? page = null)
    {
        return new WikipediaArticleResult(
            Status: status,
            RequestedTitle: request.RequestedTitle,
            CanonicalTitle: canonicalTitle ?? request.RequestedTitle,
            Extract: "",
            SourceLanguage: request.SourceLanguage,
            SourceProject: $"{request.SourceLanguage}.wikipedia.org",
            PageId: page?.PageId ?? 0,
            RevisionId: page?.LastRevId ?? 0,
            CanonicalUrl: page?.CanonicalUrl ?? "",
            IsRedirect: isRedirect,
            RedirectedFrom: redirectedFrom,
            TargetLanguage: request.TargetLanguage,
            TargetTitleCandidate: null,
            TargetUrlCandidate: null,
            Attribution: "Wikipedia contributors; text available under the Creative Commons Attribution-ShareAlike license.",
            ErrorCode: errorCode,
            RetryAfter: retryAfter
        );
    }

    private static string NormalizeExtract(string extract)
    {
        // replace any newline characters with a single space
        var noNewlines = WhitespaceRegex().Replace(extract, " ");
        // trim leading/trailing
        return noNewlines.Trim();
    }

    private static string? NormalizeApiErrorCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return code.Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
