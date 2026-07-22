using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Lexical.Wikipedia;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Lexical.Wikipedia;

[TestClass]
public class WikipediaLookupProviderTests
{
    private FakeWikipediaApiClient _apiClient = null!;
    private StaticClock _clock = null!;
    private WikipediaLookupProvider _provider = null!;

    [TestInitialize]
    public void Initialize()
    {
        _apiClient = new FakeWikipediaApiClient();
        _clock = new StaticClock(new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc));
        _provider = new WikipediaLookupProvider(_apiClient, _clock);
    }

    [TestMethod]
    public void ProviderName_IsWikipedia()
    {
        Assert.AreEqual("Wikipedia", _provider.ProviderName);
        Assert.AreEqual("Wikipedia", WikipediaLookupProvider.Name);
    }

    [TestMethod]
    public void ProviderSchemaVersion_Is1()
    {
        Assert.AreEqual(1, _provider.ProviderSchemaVersion);
        Assert.AreEqual(1, WikipediaLookupProvider.SchemaVersion);
    }

    [TestMethod]
    public void Interfaces_ImplementedCorrectly()
    {
        Assert.IsInstanceOfType(_provider, typeof(ILexicalLookupProvider));
        Assert.IsNotInstanceOfType(_provider, typeof(IDictionaryLookupProvider));
    }

    [TestMethod]
    public async Task LookupAsync_WrongProvider_ThrowsArgumentException()
    {
        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "test",
            TokenKind.Word,
            "Wiktionary");

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _provider.LookupAsync(request));
    }

    [TestMethod]
    public async Task LookupAsync_Definition_Success()
    {
        _apiClient.ResultToReturn = new WikipediaArticleResult(
            WikipediaArticleStatus.Success,
            "test",
            "Test_Page",
            "This is a test definition.",
            "en",
            "en.wikipedia.org",
            123,
            456,
            "https://en.wikipedia.org/wiki/Test_Page",
            false,
            null,
            null,
            null,
            null,
            "Wikimedia Foundation",
            null,
            null);

        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "test",
            TokenKind.Word,
            "Wikipedia");

        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.HasCount(1, result.Meanings);
        var meaning = result.Meanings[0];
        Assert.AreEqual("This is a test definition.", meaning.Definition);
        Assert.IsNull(meaning.Translation);
        Assert.AreEqual(0, result.RedirectDepth);
        Assert.IsFalse(result.IsFromCache);
        Assert.AreEqual(_clock.UtcNow, result.LookupAtUtc);
        Assert.AreEqual("wp_en.wikipedia.org_123", meaning.MeaningId);
        Assert.AreEqual("Wikipedia", result.ProviderName);
        Assert.AreEqual("Test_Page", result.PageTitle);
        Assert.AreEqual(456, result.RevisionId);
        Assert.AreEqual("Wikimedia Foundation", result.Attribution);
        Assert.AreEqual(LexicalLookupMode.Definition, result.LookupMode);
        Assert.IsNull(result.TargetLanguage);
        Assert.AreEqual("en", result.SourceLanguage);
        Assert.AreEqual("en", result.ExplanationLanguage);

        Assert.AreEqual(1, _apiClient.CallCount);
        Assert.IsNull(_apiClient.LastRequest!.TargetLanguage);
    }

    [TestMethod]
    public async Task LookupAsync_DefinitionAndTranslation_Success_NoTranslationPopulated()
    {
        _apiClient.ResultToReturn = new WikipediaArticleResult(
            WikipediaArticleStatus.Success,
            "test",
            "Test_Page",
            "This is a test definition.",
            "en",
            "en.wikipedia.org",
            123,
            456,
            "https://en.wikipedia.org/wiki/Test_Page",
            false,
            null,
            "de",
            "Test_Seite",
            "https://de.wikipedia.org/wiki/Test_Seite",
            "Wikimedia Foundation",
            null,
            null);

        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.DefinitionAndTranslation,
            "de",
            "test",
            TokenKind.Word,
            "Wikipedia");

        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.HasCount(1, result.Meanings);
        var meaning = result.Meanings[0];
        Assert.AreEqual("This is a test definition.", meaning.Definition);
        Assert.IsNull(meaning.Translation, "TargetTitleCandidate should not be saved as translation.");
        Assert.AreEqual(LexicalLookupMode.DefinitionAndTranslation, result.LookupMode);
        Assert.AreEqual("de", result.TargetLanguage);

        Assert.AreEqual(1, _apiClient.CallCount);
        Assert.AreEqual("de", _apiClient.LastRequest!.TargetLanguage);
    }

    [TestMethod]
    public async Task LookupAsync_TranslationOnly_DoesNotCallApi_ReturnsNotFound()
    {
        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Translation,
            "de",
            "test",
            TokenKind.Word,
            "Wikipedia");

        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("translation-not-supported", result.ErrorCode);
        Assert.IsEmpty(result.Meanings);
        Assert.AreEqual(0, _apiClient.CallCount);
        Assert.AreEqual(string.Empty, result.SourceProject);
        Assert.AreEqual(string.Empty, result.Attribution);
        Assert.AreEqual(string.Empty, result.PageTitle);
        Assert.IsNull(result.RevisionId);
    }

    [TestMethod]
    public async Task LookupAsync_SuccessWithEmptyExtract_ReturnsNotFound()
    {
        _apiClient.ResultToReturn = new WikipediaArticleResult(
            WikipediaArticleStatus.Success,
            "test",
            "Test_Page",
            "   ",
            "en",
            "en.wikipedia.org",
            123,
            456,
            "https://en.wikipedia.org/wiki/Test_Page",
            false,
            null,
            null,
            null,
            null,
            "Wikimedia Foundation",
            null,
            null);

        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "test",
            TokenKind.Word,
            "Wikipedia");

        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("no-usable-content", result.ErrorCode);
        Assert.IsEmpty(result.Meanings);
        Assert.AreEqual("en.wikipedia.org", result.SourceProject);
        Assert.AreEqual("Wikimedia Foundation", result.Attribution);
    }

    [TestMethod]
    public async Task LookupAsync_Disambiguation_MappedToNotFound()
    {
        _apiClient.ResultToReturn = new WikipediaArticleResult(
            WikipediaArticleStatus.Disambiguation,
            "test",
            "Test_Page",
            "",
            "en",
            "en.wikipedia.org",
            123,
            456,
            "url",
            false,
            null,
            null,
            null,
            null,
            "Wikimedia Foundation",
            null,
            null);

        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");
        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("disambiguation", result.ErrorCode);
        Assert.IsEmpty(result.Meanings);
    }

    [TestMethod]
    [DataRow(WikipediaArticleStatus.NotFound, LexicalLookupStatus.NotFound, "wikipedia-not-found")]
    [DataRow(WikipediaArticleStatus.NoUsableContent, LexicalLookupStatus.NotFound, "no-usable-content")]
    [DataRow(WikipediaArticleStatus.RateLimited, LexicalLookupStatus.TransientFailure, "rate-limited")]
    [DataRow(WikipediaArticleStatus.TimedOut, LexicalLookupStatus.TransientFailure, "timeout")]
    [DataRow(WikipediaArticleStatus.TransientFailure, LexicalLookupStatus.TransientFailure, "wikipedia-transient-failure")]
    [DataRow(WikipediaArticleStatus.PermanentFailure, LexicalLookupStatus.PermanentFailure, "wikipedia-permanent-failure")]
    [DataRow(WikipediaArticleStatus.ParseFailure, LexicalLookupStatus.ParseFailure, "wikipedia-parse-failure")]
    public async Task LookupAsync_ErrorStatus_MappedCorrectly(WikipediaArticleStatus apiStatus, LexicalLookupStatus expectedStatus, string expectedDefaultErrorCode)
    {
        _apiClient.ResultToReturn = new WikipediaArticleResult(
            apiStatus,
            "test",
            "Test_Page",
            "",
            "en",
            "en.wikipedia.org",
            123,
            456,
            "url",
            false,
            null,
            null,
            null,
            null,
            "Wikimedia Foundation",
            null,
            null);

        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");
        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(expectedStatus, result.Status);
        Assert.AreEqual(expectedDefaultErrorCode, result.ErrorCode);
        Assert.IsEmpty(result.Meanings);
        Assert.AreEqual("en.wikipedia.org", result.SourceProject);
        Assert.AreEqual("Wikimedia Foundation", result.Attribution);
    }
    
    [TestMethod]
    public async Task LookupAsync_ErrorCodePreservedIfPresent()
    {
        _apiClient.ResultToReturn = new WikipediaArticleResult(
            WikipediaArticleStatus.PermanentFailure,
            "test",
            "Test_Page",
            "",
            "en",
            "en.wikipedia.org",
            123,
            456,
            "url",
            false,
            null,
            null,
            null,
            null,
            "Wikimedia Foundation",
            "specific-api-error",
            null);

        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");
        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("specific-api-error", result.ErrorCode);
    }

    [TestMethod]
    public async Task LookupAsync_Cancellation_Throws()
    {
        _apiClient.ExceptionToThrow = new OperationCanceledException();
        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            _provider.LookupAsync(request, new CancellationToken(true)));
    }

    [TestMethod]
    public async Task LookupAsync_UnexpectedException_ReturnsProviderError()
    {
        _apiClient.ExceptionToThrow = new InvalidOperationException("boom");
        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");

        var result = await _provider.LookupAsync(request);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("provider-error", result.ErrorCode);
        Assert.IsEmpty(result.Meanings);
        Assert.AreEqual(string.Empty, result.SourceProject);
        Assert.AreEqual(string.Empty, result.Attribution);
    }

    [TestMethod]
    public void DescribeRequest_DoesNotContainFullTerm()
    {
        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "supercalifragilisticexpialidocious", TokenKind.Word, "Wikipedia");
        var description = _provider.DescribeRequest(request);

        Assert.DoesNotContain("supercalifragilisticexpialidocious", description);
        Assert.Contains("title-length=34", description);
    }

    [TestMethod]
    public async Task MeaningId_IsDeterministicAndDifferentForDifferentPages()
    {
        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");

        _apiClient.ResultToReturn = CreateSuccessResult("Page A", 100, 200);
        var resultA1 = await _provider.LookupAsync(request);
        var resultA2 = await _provider.LookupAsync(request);

        _apiClient.ResultToReturn = CreateSuccessResult("Page B", 101, 200);
        var resultB = await _provider.LookupAsync(request);

        _apiClient.ResultToReturn = CreateSuccessResult("Page A", 100, 201); // different revision
        var resultA3 = await _provider.LookupAsync(request);

        Assert.AreEqual(resultA1.Meanings[0].MeaningId, resultA2.Meanings[0].MeaningId);
        Assert.AreNotEqual(resultA1.Meanings[0].MeaningId, resultB.Meanings[0].MeaningId);
        Assert.AreEqual(resultA1.Meanings[0].MeaningId, resultA3.Meanings[0].MeaningId); // RevisionId must not change MeaningId
    }

    [TestMethod]
    public async Task MeaningId_FallbackWhenPageIdIsZero()
    {
        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");

        _apiClient.ResultToReturn = CreateSuccessResult("Page C", 0, 0);
        var result1 = await _provider.LookupAsync(request);
        var result2 = await _provider.LookupAsync(request);

        Assert.IsNotNull(result1.Meanings[0].MeaningId);
        Assert.StartsWith("wp_", result1.Meanings[0].MeaningId);
        Assert.AreEqual(19, result1.Meanings[0].MeaningId.Length); // "wp_" + 16 chars hex
        Assert.AreEqual(result1.Meanings[0].MeaningId, result2.Meanings[0].MeaningId);
    }

    private WikipediaArticleResult CreateSuccessResult(string canonicalTitle, long pageId, long revisionId)
    {
        return new WikipediaArticleResult(
            WikipediaArticleStatus.Success,
            "test",
            canonicalTitle,
            "extract",
            "en",
            "en.wikipedia.org",
            pageId,
            revisionId,
            "url",
            false,
            null,
            null,
            null,
            null,
            "Wikimedia Foundation",
            null,
            null);
    }
}

public class FakeWikipediaApiClient : IWikipediaApiClient
{
    public int CallCount { get; private set; }
    public WikipediaArticleRequest? LastRequest { get; private set; }

    public WikipediaArticleResult ResultToReturn { get; set; } = null!;
    public Exception? ExceptionToThrow { get; set; }

    public Task<WikipediaArticleResult> GetArticleAsync(WikipediaArticleRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;

        cancellationToken.ThrowIfCancellationRequested();

        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(ResultToReturn);
    }
}

public class StaticClock : IClock
{
    public StaticClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; }
}
