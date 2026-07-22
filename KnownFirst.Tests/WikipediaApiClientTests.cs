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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
        Assert.AreEqual("Exact Title", result.CanonicalTitle);
        Assert.AreEqual("A synthetic security system collects and evaluates event records.", result.Extract);
        Assert.AreEqual(1001, result.PageId);
        Assert.IsFalse(result.IsRedirect);
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
    public async Task GetArticleAsync_Http429_ReturnsRateLimited()
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
        Assert.AreEqual("C", result.CanonicalTitle);
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
}
