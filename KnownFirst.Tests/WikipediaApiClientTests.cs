using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using KnownFirst.Services.Lexical.Wikipedia;

namespace KnownFirst.Tests;

[TestClass]
public class WikipediaApiClientTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? SendAsyncFunc { get; set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (SendAsyncFunc != null)
            {
                return SendAsyncFunc(request, cancellationToken);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static HttpResponseMessage CreateJsonResponse(string fixtureName, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "Wikipedia", fixtureName);
        var content = File.ReadAllText(path);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };
    }

    [TestMethod]
    public async Task GetArticleAsync_ExactSuccess_ReturnsSuccess()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("exact-success.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Exact Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.AreEqual("Exact Title", result.RequestedTitle);
        Assert.AreEqual("Exact Title", result.CanonicalTitle);
        Assert.AreEqual("A synthetic security system collects and evaluates event records.", result.Extract);
        Assert.AreEqual("en", result.SourceLanguage);
        Assert.AreEqual("en.wikipedia.org", result.SourceProject);
        Assert.AreEqual(1001, result.PageId);
        Assert.AreEqual(2001, result.RevisionId);
        Assert.AreEqual("https://en.wikipedia.org/wiki/Exact_Title", result.CanonicalUrl);
        Assert.IsFalse(result.IsRedirect);
        Assert.AreEqual("Wikipedia contributors; text available under the Creative Commons Attribution-ShareAlike license.", result.Attribution);
        Assert.IsNull(result.ErrorCode);
        Assert.IsNull(result.RetryAfter);
    }

    [TestMethod]
    public async Task GetArticleAsync_NormalizedTitle_UpdatesCanonicalTitle()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("normalized-title.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "normalized title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.AreEqual("Normalized Title", result.CanonicalTitle);
    }

    [TestMethod]
    public async Task GetArticleAsync_RedirectSuccess_SetsRedirectFlag()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("redirect-success.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Old Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.IsTrue(result.IsRedirect);
        Assert.AreEqual("Old Title", result.RedirectedFrom);
        Assert.AreEqual("New Title", result.CanonicalTitle);
    }

    [TestMethod]
    public async Task GetArticleAsync_MissingPage_ReturnsNotFound()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("missing-page.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Missing Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.NotFound, result.Status);
        Assert.AreEqual("page-missing", result.ErrorCode);
    }

    [TestMethod]
    public async Task GetArticleAsync_Disambiguation_ReturnsDisambiguation()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("disambiguation.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Disambiguation Page");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Disambiguation, result.Status);
        Assert.AreEqual("", result.Extract);
        Assert.AreEqual("disambiguation", result.ErrorCode);
        Assert.AreEqual("Disambiguation Page", result.CanonicalTitle);
        Assert.AreEqual("en", result.SourceLanguage);
        Assert.AreEqual("en.wikipedia.org", result.SourceProject);
        Assert.AreEqual(1004, result.PageId);
        Assert.AreEqual(2004, result.RevisionId);
    }

    [TestMethod]
    public async Task GetArticleAsync_EmptyExtract_ReturnsNoUsableContent()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("empty-extract.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Empty Extract");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.NoUsableContent, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_LangLinkPresent_SetsTargetLanguageFields()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("langlink-present.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Source Title", "de");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.AreEqual("Ziel Titel", result.TargetTitleCandidate);
        Assert.AreEqual("https://de.wikipedia.org/wiki/Ziel_Titel", result.TargetUrlCandidate);
    }

    [TestMethod]
    public async Task GetArticleAsync_LangLinkAbsent_TargetLanguageFieldsAreNull()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("langlink-absent.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Source Title No Lang", "de");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.IsNull(result.TargetTitleCandidate);
        Assert.IsNull(result.TargetUrlCandidate);
    }

    [TestMethod]
    public async Task GetArticleAsync_ApiTransientError_ReturnsTransientFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("api-transient-error.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.TransientFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_ApiPermanentError_ReturnsPermanentFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("api-permanent-error.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.PermanentFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_ApiErrorWithoutCode_ReturnsPermanentUnknownApiError()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("api-error-without-code.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.PermanentFailure, result.Status);
        Assert.AreEqual("api-error-unknown", result.ErrorCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorCode));
        Assert.AreNotEqual("api-", result.ErrorCode);
    }

    [TestMethod]
    public async Task GetArticleAsync_PageWithoutTitle_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("missing-page-title.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Title Missing");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
        Assert.AreEqual("missing-page-title", result.ErrorCode);
        Assert.AreEqual("", result.Extract);
    }

    [TestMethod]
    public async Task GetArticleAsync_MalformedJson_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("malformed.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_MissingQueryPayload_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("missing-query-payload.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_Http429WithDelta_ReturnsRateLimitedWithDelta()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return Task.FromResult(response);
            }
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.RateLimited, result.Status);
        Assert.AreEqual(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [TestMethod]
    public async Task GetArticleAsync_Http429WithFutureDate_ReturnsRateLimitedWithCalculatedDelta()
    {
        var fakeClock = new FakeClock(new DateTime(2026, 01, 01, 12, 0, 0, DateTimeKind.Utc));
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(new DateTimeOffset(2026, 01, 01, 12, 0, 30, TimeSpan.Zero));
                return Task.FromResult(response);
            }
        };
        var client = new WikipediaApiClient(new HttpClient(handler), fakeClock);
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.RateLimited, result.Status);
        Assert.AreEqual(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [TestMethod]
    public async Task GetArticleAsync_Http429WithPastDate_ReturnsRateLimitedWithZeroDelta()
    {
        var fakeClock = new FakeClock(new DateTime(2026, 01, 01, 12, 0, 0, DateTimeKind.Utc));
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                // 30 seconds in the past
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(new DateTimeOffset(2026, 01, 01, 11, 59, 30, TimeSpan.Zero));
                return Task.FromResult(response);
            }
        };
        var client = new WikipediaApiClient(new HttpClient(handler), fakeClock);
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.RateLimited, result.Status);
        Assert.AreEqual(TimeSpan.Zero, result.RetryAfter);
    }

    [TestMethod]
    public async Task GetArticleAsync_Http429WithoutRetryAfter_ReturnsRateLimitedWithNullDelta()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                return Task.FromResult(response);
            }
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any Title");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.RateLimited, result.Status);
        Assert.IsNull(result.RetryAfter);
    }

    [TestMethod]
    public void WikipediaArticleRequest_InvalidSourceLanguage_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WikipediaArticleRequest("fr", "Title"));
    }

    [TestMethod]
    public void WikipediaArticleRequest_EmptyTitle_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WikipediaArticleRequest("en", "   "));
    }

    [TestMethod]
    public void WikipediaArticleRequest_InvalidTargetLanguage_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WikipediaArticleRequest("en", "Title", "fr"));
    }

    [TestMethod]
    public void WikipediaArticleRequest_SameSourceAndTarget_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WikipediaArticleRequest("en", "Title", "en"));
    }

    [TestMethod]
    public void WikipediaArticleRequest_NormalizesTitle()
    {
        // "Title" with a trailing space and an e-acute that can be combined or separated
        // NFC of "e" + "\u0301" is "\u00E9"
        var request = new WikipediaArticleRequest("en", " e\u0301 ");
        Assert.AreEqual("\u00E9", request.RequestedTitle);
    }
    [TestMethod]
    public async Task GetArticleAsync_RedirectChain_SetsRedirectFlagAndCanonicalTitle()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("redirect-chain.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.IsTrue(result.IsRedirect);
        Assert.AreEqual("A", result.RedirectedFrom);
        Assert.AreEqual("C", result.CanonicalTitle);
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetArticleAsync_ApiRateLimitedError_ReturnsRateLimited()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("api-rate-limited-error.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.RateLimited, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_EmptyPages_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("empty-pages.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_MultiplePages_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("multiple-pages.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_WrongFieldType_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("wrong-field-type.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_MissingExtract_ReturnsNoUsableContent()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("missing-extract.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.NoUsableContent, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_WrongLanglinkLanguage_IgnoresLanglink()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("wrong-langlink-language.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A", "de");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.IsNull(result.TargetTitleCandidate);
        Assert.IsNull(result.TargetUrlCandidate);
    }

    [TestMethod]
    public async Task GetArticleAsync_WhitespaceNormalization_NormalizesExtract()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("whitespace-normalization.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.AreEqual("Line 1 Line 2 Line 3", result.Extract);
    }

    [TestMethod]
    public async Task GetArticleAsync_LongExtract_TruncatesTo1200Chars()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("long-extract.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.AreEqual(1200, result.Extract.Length);
    }

    [TestMethod]
    public async Task GetArticleAsync_RedirectDisconnected_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("redirect-disconnected.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_RedirectCycle_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("redirect-cycle.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_RedirectFinalMismatch_ReturnsParseFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("redirect-final-mismatch.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "A");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.ParseFailure, result.Status);
    }
    [TestMethod]
    [DataRow("en", "Space Title", null, "https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=info%7Cextracts%7Cpageprops%7Clanglinks&inprop=url&exintro=1&explaintext=1&exchars=1200&ppprop=disambiguation&titles=Space%20Title")]
    [DataRow("de", "Title-With-Dash", "en", "https://de.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=info%7Cextracts%7Cpageprops%7Clanglinks&inprop=url&exintro=1&explaintext=1&exchars=1200&ppprop=disambiguation&titles=Title-With-Dash&lllang=en&lllimit=1&llprop=url")]
    [DataRow("de", "Umlautäöü", null, "https://de.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=info%7Cextracts%7Cpageprops%7Clanglinks&inprop=url&exintro=1&explaintext=1&exchars=1200&ppprop=disambiguation&titles=Umlaut%C3%A4%C3%B6%C3%BC")]
    [DataRow("en", "漢字", null, "https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=info%7Cextracts%7Cpageprops%7Clanglinks&inprop=url&exintro=1&explaintext=1&exchars=1200&ppprop=disambiguation&titles=%E6%BC%A2%E5%AD%97")]
    public async Task GetArticleAsync_ConstructsValidRequest(string sourceLanguage, string title, string? targetLanguage, string expectedUrl)
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => 
            {
                capturedRequest = req;
                return Task.FromResult(CreateJsonResponse("exact-success.json"));
            }
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest(sourceLanguage, title, targetLanguage);

        await client.GetArticleAsync(request);

        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual(HttpMethod.Get, capturedRequest.Method);
        Assert.AreEqual(expectedUrl, capturedRequest.RequestUri!.AbsoluteUri);
        Assert.AreEqual("KnownFirst/1.0 (https://github.com/Tachiguro/KnownFirst; read-only Wikipedia lookup)", capturedRequest.Headers.UserAgent.ToString());
        Assert.IsTrue(capturedRequest.Headers.Accept.Any(a => a.MediaType == "application/json"));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.NotFound, WikipediaArticleStatus.NotFound)]
    [DataRow(HttpStatusCode.RequestTimeout, WikipediaArticleStatus.TransientFailure)]
    [DataRow(HttpStatusCode.InternalServerError, WikipediaArticleStatus.TransientFailure)]
    [DataRow(HttpStatusCode.ServiceUnavailable, WikipediaArticleStatus.TransientFailure)]
    [DataRow(HttpStatusCode.BadRequest, WikipediaArticleStatus.PermanentFailure)]
    [DataRow(HttpStatusCode.Unauthorized, WikipediaArticleStatus.PermanentFailure)]
    [DataRow(HttpStatusCode.Forbidden, WikipediaArticleStatus.PermanentFailure)]
    [DataRow(HttpStatusCode.BadGateway, WikipediaArticleStatus.TransientFailure)]
    [DataRow(HttpStatusCode.GatewayTimeout, WikipediaArticleStatus.TransientFailure)]
    public async Task GetArticleAsync_HttpStatusCodes_ReturnExpectedStatus(HttpStatusCode statusCode, WikipediaArticleStatus expectedStatus)
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(new HttpResponseMessage(statusCode))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(expectedStatus, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_HttpRequestException_ReturnsTransientFailure()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => throw new HttpRequestException()
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.TransientFailure, result.Status);
    }

    [TestMethod]
    public async Task GetArticleAsync_SuccessWithLanglink_SendsExactlyOneRequest()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => Task.FromResult(CreateJsonResponse("langlink-present.json"))
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Source Title", "de");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.Success, result.Status);
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetArticleAsync_InternalTimeout_ReturnsTimedOut()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = async (req, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("Unreachable after timeout cancellation.");
            }
        };
        var client = new WikipediaApiClient(new HttpClient(handler), requestTimeout: TimeSpan.FromMilliseconds(20));
        var request = new WikipediaArticleRequest("en", "Any");

        var result = await client.GetArticleAsync(request);

        Assert.AreEqual(WikipediaArticleStatus.TimedOut, result.Status);
        Assert.AreEqual("timeout", result.ErrorCode);
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public void WikipediaApiClient_NonPositiveRequestTimeout_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new WikipediaApiClient(new HttpClient(new FakeHttpMessageHandler()), requestTimeout: TimeSpan.Zero));
    }

    [TestMethod]
    public void WikipediaArticleResultContract_DoesNotExposeTranslation()
    {
        var members = typeof(WikipediaArticleResult)
            .GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(member => member.MemberType is System.Reflection.MemberTypes.Property or System.Reflection.MemberTypes.Field);

        Assert.IsFalse(members.Any(member => string.Equals(member.Name, "Translation", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task GetArticleAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler
        {
            SendAsyncFunc = (req, ct) => throw new TaskCanceledException()
        };
        var client = new WikipediaApiClient(new HttpClient(handler));
        var request = new WikipediaArticleRequest("en", "Any");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await client.GetArticleAsync(request, cts.Token);
            Assert.Fail("Expected OperationCanceledException was not thrown.");
        }
        catch (OperationCanceledException)
        {
            // Success
        }
    }
}
