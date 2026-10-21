using System.Globalization;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using Microsoft.Extensions.DependencyInjection;
using SQLite;
using static KnownFirst.Tests.DatabaseSchema13ProductionCutoverTests;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class VocabularySearchServiceTests
{
    private static readonly DateTime BaseTime = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task EmptyDataset_ActiveAndEmptyQueries_ReturnBoundedEmptyResults()
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();
        var countingDb = new SnapshotCountingDatabase(innerDb);

        IVocabularySearchService service = new VocabularySearchService(countingDb);

        var activePage = await service.SearchAsync("en", "network");
        Assert.AreEqual(VocabularySearchStatus.Active, activePage.Status);
        Assert.AreEqual(VocabularySearchQueryStatus.Active, activePage.QueryStatus);
        Assert.IsTrue(activePage.IsValid);
        Assert.IsTrue(activePage.IsSearchActive);
        Assert.IsFalse(activePage.UseRecentOverview);
        Assert.AreEqual("en", activePage.SourceLanguage);
        Assert.AreEqual("network", activePage.NormalizedQuery);
        Assert.AreEqual(IVocabularySearchService.DefaultPageSize, activePage.PageSize);
        Assert.IsEmpty(activePage.Items);
        Assert.IsFalse(activePage.HasMore);
        Assert.IsNull(activePage.NextCursor);
        Assert.AreEqual(1, countingDb.SnapshotCount);

        var emptyQueryPage = await service.SearchAsync("en", "   ");
        Assert.AreEqual(VocabularySearchStatus.EmptyQuery, emptyQueryPage.Status);
        Assert.AreEqual(VocabularySearchQueryStatus.EmptyQuery, emptyQueryPage.QueryStatus);
        Assert.IsTrue(emptyQueryPage.IsValid);
        Assert.IsFalse(emptyQueryPage.IsSearchActive);
        Assert.IsTrue(emptyQueryPage.UseRecentOverview);
        Assert.AreEqual("en", emptyQueryPage.SourceLanguage);
        Assert.AreEqual(string.Empty, emptyQueryPage.NormalizedQuery);
        Assert.AreEqual(IVocabularySearchService.DefaultPageSize, emptyQueryPage.PageSize);
        Assert.IsEmpty(emptyQueryPage.Items);
        Assert.IsFalse(emptyQueryPage.HasMore);
        Assert.IsNull(emptyQueryPage.NextCursor);
        Assert.AreEqual(1, countingDb.SnapshotCount, "Empty query must not execute a database snapshot.");
    }

    [TestMethod]
    public async Task EnglishOnlyMatches_ReturnsMatchingEnglishTermsInDeterministicOrder()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int idNetwork = 0, idNet = 0, idNetworking = 0;
        await database.RunInTransactionAsync(connection =>
        {
            idNetworking = InsertWord(connection, "networking", "W:networking", "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime.AddMinutes(1));
            idNet = InsertWord(connection, "net", "W:net", "en", TokenKind.Word, WordStatus.Known, BaseTime.AddMinutes(2));
            idNetwork = InsertWord(connection, "Network", "W:network", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "bank", "W:bank", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            return true;
        });

        var service = new VocabularySearchService(database);
        var page = await service.SearchAsync("en", "NET");

        Assert.AreEqual(VocabularySearchStatus.Active, page.Status);
        Assert.IsTrue(page.IsSearchActive);
        Assert.AreEqual("en", page.SourceLanguage);
        Assert.AreEqual("NET", page.NormalizedQuery);
        Assert.IsFalse(page.HasMore);
        Assert.IsNull(page.NextCursor);

        CollectionAssert.AreEqual(
            new[] { idNet, idNetwork, idNetworking },
            page.Items.Select(item => item.WordId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "W:net", "W:network", "W:networking" },
            page.Items.Select(item => item.NormalizedTerm).ToArray());
        CollectionAssert.AreEqual(
            new[] { "net", "Network", "networking" },
            page.Items.Select(item => item.CanonicalTerm).ToArray());
    }

    [TestMethod]
    public async Task GermanOnlyMatches_ReturnsMatchingGermanTermsInDeterministicOrder()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int idHaus = 0, idHaustier = 0, idHaustuer = 0;
        await database.RunInTransactionAsync(connection =>
        {
            idHaustuer = InsertWord(connection, "Haustür", "W:haustür", "de", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime.AddMinutes(1));
            idHaus = InsertWord(connection, "Haus", "W:haus", "de", TokenKind.Word, WordStatus.Known, BaseTime.AddMinutes(2));
            idHaustier = InsertWord(connection, "Haustier", "W:haustier", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "Baum", "W:baum", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            return true;
        });

        var service = new VocabularySearchService(database);
        var page = await service.SearchAsync("de", "Haus");

        Assert.AreEqual(VocabularySearchStatus.Active, page.Status);
        Assert.AreEqual("de", page.SourceLanguage);
        CollectionAssert.AreEqual(
            new[] { idHaus, idHaustier, idHaustuer },
            page.Items.Select(item => item.WordId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "W:haus", "W:haustier", "W:haustür" },
            page.Items.Select(item => item.NormalizedTerm).ToArray());
    }

    [TestMethod]
    public async Task NoCrossLanguageLeakage_AndExplicitSourceLanguageRequiredIndependentlyOfUiCulture()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int enBankId = 0, deBankId = 0;
        await database.RunInTransactionAsync(connection =>
        {
            enBankId = InsertWord(connection, "bank", "W:bank", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            deBankId = InsertWord(connection, "Bank", "W:bank", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "bank", "W:bank", "ru", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            return true;
        });

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");

            var service = new VocabularySearchService(database);

            var enPage = await service.SearchAsync(" EN ", "bank");
            Assert.AreEqual(VocabularySearchStatus.Active, enPage.Status);
            Assert.HasCount(1, enPage.Items);
            Assert.AreEqual(enBankId, enPage.Items[0].WordId);
            Assert.AreEqual("en", enPage.Items[0].SourceLanguage);

            var dePage = await service.SearchAsync(" DE ", "bank");
            Assert.AreEqual(VocabularySearchStatus.Active, dePage.Status);
            Assert.HasCount(1, dePage.Items);
            Assert.AreEqual(deBankId, dePage.Items[0].WordId);
            Assert.AreEqual("de", dePage.Items[0].SourceLanguage);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public async Task WordVersusAcronymIdentityCollisions_PreservesDistinctionBetweenItAndIT()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int wordItId = 0, acronymItId = 0, wordUsId = 0, acronymUsId = 0;
        await database.RunInTransactionAsync(connection =>
        {
            wordItId = InsertWord(connection, "it", "W:it", "en", TokenKind.Word, WordStatus.Known, BaseTime.AddMinutes(1));
            acronymItId = InsertWord(connection, "IT", "A:IT", "en", TokenKind.Acronym, WordStatus.UnknownBacklog, BaseTime.AddMinutes(2));
            wordUsId = InsertWord(connection, "us", "W:us", "en", TokenKind.Word, WordStatus.Known, BaseTime.AddMinutes(3));
            acronymUsId = InsertWord(connection, "US", "A:US", "en", TokenKind.Acronym, WordStatus.UnknownBacklog, BaseTime.AddMinutes(4));
            return true;
        });

        var service = new VocabularySearchService(database);

        // Lowercase "it" searches W:it..W:iu and A:it..A:iu -> matches W:it only, not A:IT
        var lowerItPage = await service.SearchAsync("en", "it");
        Assert.HasCount(1, lowerItPage.Items);
        Assert.AreEqual(wordItId, lowerItPage.Items[0].WordId);
        Assert.AreEqual("W:it", lowerItPage.Items[0].NormalizedTerm);
        Assert.AreEqual(TokenKind.Word, lowerItPage.Items[0].TokenKind);

        // Uppercase "IT" searches A:IT..A:IU and W:it..W:iu -> matches both A:IT and W:it in global UTF-8 order
        var upperItPage = await service.SearchAsync("en", "IT");
        Assert.HasCount(2, upperItPage.Items);
        Assert.AreEqual(acronymItId, upperItPage.Items[0].WordId);
        Assert.AreEqual("A:IT", upperItPage.Items[0].NormalizedTerm);
        Assert.AreEqual(TokenKind.Acronym, upperItPage.Items[0].TokenKind);
        Assert.AreEqual(wordItId, upperItPage.Items[1].WordId);
        Assert.AreEqual("W:it", upperItPage.Items[1].NormalizedTerm);
        Assert.AreEqual(TokenKind.Word, upperItPage.Items[1].TokenKind);

        // Same for "us" vs "US"
        var lowerUsPage = await service.SearchAsync("en", "us");
        CollectionAssert.AreEqual(new[] { wordUsId }, lowerUsPage.Items.Select(x => x.WordId).ToArray());

        var upperUsPage = await service.SearchAsync("en", "US");
        CollectionAssert.AreEqual(new[] { acronymUsId, wordUsId }, upperUsPage.Items.Select(x => x.WordId).ToArray());
    }

    [TestMethod]
    public async Task CaseSensitiveAcronymAbbreviationAndTechnicalTermLookup_RespectsTokenKindCaseSemantics()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "SHA", "A:SHA", "en", TokenKind.Acronym, WordStatus.UnknownBacklog, BaseTime.AddMinutes(1));
            InsertWord(connection, "Dr.", "B:Dr.", "en", TokenKind.Abbreviation, WordStatus.UnknownBacklog, BaseTime.AddMinutes(2));
            InsertWord(connection, "OAuth2", "T:OAuth2", "en", TokenKind.TechnicalTerm, WordStatus.UnknownBacklog, BaseTime.AddMinutes(3));
            return true;
        });

        var service = new VocabularySearchService(database);

        var upperSha = await service.SearchAsync("en", "SHA");
        CollectionAssert.AreEqual(new[] { "A:SHA" }, upperSha.Items.Select(x => x.NormalizedTerm).ToArray());

        var lowerSha = await service.SearchAsync("en", "sha");
        Assert.IsEmpty(lowerSha.Items);

        var exactDr = await service.SearchAsync("en", "Dr.");
        CollectionAssert.AreEqual(new[] { "B:Dr." }, exactDr.Items.Select(x => x.NormalizedTerm).ToArray());

        var lowerDr = await service.SearchAsync("en", "dr.");
        Assert.IsEmpty(lowerDr.Items);

        var exactOAuth = await service.SearchAsync("en", "OAuth");
        CollectionAssert.AreEqual(new[] { "T:OAuth2" }, exactOAuth.Items.Select(x => x.NormalizedTerm).ToArray());

        var lowerOAuth = await service.SearchAsync("en", "oauth");
        Assert.IsEmpty(lowerOAuth.Items);
    }

    [TestMethod]
    public async Task NfcComposedAndDecomposedQueries_ReturnIdenticalResultsAndCompatibleCursors()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "caf\u00E9", "W:caf\u00E9", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "caf\u00E9s", "W:caf\u00E9s", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "H\u00E4user", "W:h\u00E4user", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            return true;
        });

        var service = new VocabularySearchService(database);

        var composedPage = await service.SearchAsync("en", "caf\u00E9", pageSize: 1);
        var decomposedPage = await service.SearchAsync("en", "cafe\u0301", pageSize: 1);

        Assert.AreEqual("caf\u00E9", composedPage.NormalizedQuery);
        Assert.AreEqual(composedPage.NormalizedQuery, decomposedPage.NormalizedQuery);
        Assert.HasCount(1, composedPage.Items);
        Assert.AreEqual(composedPage.Items[0].WordId, decomposedPage.Items[0].WordId);
        Assert.IsTrue(composedPage.HasMore);
        Assert.IsNotNull(composedPage.NextCursor);

        // Continuation cursor from NFC query must be accepted when continuing with NFD raw input
        var page2FromDecomposed = await service.SearchAsync("en", "cafe\u0301", pageSize: 1, cursor: composedPage.NextCursor);
        Assert.AreEqual(VocabularySearchStatus.Active, page2FromDecomposed.Status);
        Assert.HasCount(1, page2FromDecomposed.Items);
        Assert.AreEqual("W:caf\u00E9s", page2FromDecomposed.Items[0].NormalizedTerm);
        Assert.IsFalse(page2FromDecomposed.HasMore);

        var deComposed = await service.SearchAsync("de", "H\u00E4user");
        var deDecomposed = await service.SearchAsync("de", "Ha\u0308user");
        CollectionAssert.AreEqual(
            deComposed.Items.Select(x => x.WordId).ToArray(),
            deDecomposed.Items.Select(x => x.WordId).ToArray());
    }

    [TestMethod]
    public async Task GermanUmlautsEszettAndAccentedStrings_PreservesDiacriticsWithoutFolding()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "Äpfel", "W:äpfel", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "Apfel", "W:apfel", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "Straße", "W:straße", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "Straßenbahn", "W:straßenbahn", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            InsertWord(connection, "Strasse", "W:strasse", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(5));
            InsertWord(connection, "café", "W:café", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(6));
            InsertWord(connection, "cafe", "W:cafe", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(7));
            return true;
        });

        var service = new VocabularySearchService(database);

        var umlautResult = await service.SearchAsync("de", "Äpf");
        CollectionAssert.AreEqual(new[] { "W:äpfel" }, umlautResult.Items.Select(x => x.NormalizedTerm).ToArray());

        var eszettResult = await service.SearchAsync("de", "Straß");
        CollectionAssert.AreEqual(
            new[] { "W:straße", "W:straßenbahn" },
            eszettResult.Items.Select(x => x.NormalizedTerm).ToArray());

        var cafeResult = await service.SearchAsync("en", "café");
        CollectionAssert.AreEqual(new[] { "W:café" }, cafeResult.Items.Select(x => x.NormalizedTerm).ToArray());
    }

    [TestMethod]
    public async Task NonBmpUnicodeIdentityRanges_AndUtf8BinaryOrderingVersusUtf16Ordinal()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "\U00010348", "W:\U00010348", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "\U00010348alpha", "W:\U00010348alpha", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "\U00010349", "W:\U00010349", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));

            InsertWord(connection, "x\U000103FFz", "T:x\U000103FFz", "en", TokenKind.TechnicalTerm, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            InsertWord(connection, "x\U00010400", "T:x\U00010400", "en", TokenKind.TechnicalTerm, WordStatus.Unreviewed, BaseTime.AddMinutes(5));

            // In UTF-16 ordinal order, \U00010000 (high surrogate 0xD800) < \uE000 (0xE000).
            // In SQLite BINARY UTF-8 order, \uE000 (0xEE 0x80 0x80) < \U00010000 (0xF0 0x90 0x80 0x80).
            InsertWord(connection, "ord\U00010000", "W:ord\U00010000", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(6));
            InsertWord(connection, "ord\uE000", "W:ord\uE000", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(7));
            return true;
        });

        var service = new VocabularySearchService(database);

        var hwairPage = await service.SearchAsync("en", "\U00010348");
        CollectionAssert.AreEqual(
            new[] { "W:\U00010348", "W:\U00010348alpha" },
            hwairPage.Items.Select(x => x.NormalizedTerm).ToArray());

        var surrogateStepPage = await service.SearchAsync("en", "x\U000103FF");
        CollectionAssert.AreEqual(
            new[] { "T:x\U000103FFz" },
            surrogateStepPage.Items.Select(x => x.NormalizedTerm).ToArray());

        var ordPage1 = await service.SearchAsync("en", "ord", pageSize: 1);
        Assert.HasCount(1, ordPage1.Items);
        Assert.AreEqual("W:ord\uE000", ordPage1.Items[0].NormalizedTerm);
        Assert.IsTrue(ordPage1.HasMore);

        var ordPage2 = await service.SearchAsync("en", "ord", pageSize: 1, cursor: ordPage1.NextCursor);
        Assert.HasCount(1, ordPage2.Items);
        Assert.AreEqual("W:ord\U00010000", ordPage2.Items[0].NormalizedTerm);
        Assert.IsFalse(ordPage2.HasMore);
    }

    [TestMethod]
    public async Task LiteralPercentAndUnderscore_TreatedAsLiteralPrefixesNotSqlWildcards()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "100%", "W:100%", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "100%ers", "W:100%ers", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "1000", "W:1000", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "100a", "W:100a", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(4));

            InsertWord(connection, "foo_", "W:foo_", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(5));
            InsertWord(connection, "foo_bar", "W:foo_bar", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(6));
            InsertWord(connection, "fooa", "W:fooa", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(7));
            InsertWord(connection, "foo1", "W:foo1", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(8));
            return true;
        });

        var service = new VocabularySearchService(database);

        var percentPage = await service.SearchAsync("en", "100%");
        CollectionAssert.AreEqual(
            new[] { "W:100%", "W:100%ers" },
            percentPage.Items.Select(x => x.NormalizedTerm).ToArray());

        var underscorePage = await service.SearchAsync("en", "foo_");
        CollectionAssert.AreEqual(
            new[] { "W:foo_", "W:foo_bar" },
            underscorePage.Items.Select(x => x.NormalizedTerm).ToArray());
    }

    [TestMethod]
    public async Task PrefixMatchVersusSubstringExclusion_AndNoBroadStemming()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "network", "W:network", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "networking", "W:networking", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "subnetwork", "W:subnetwork", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "internetwork", "W:internetwork", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            InsertWord(connection, "bank", "W:bank", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(5));
            InsertWord(connection, "banks", "W:banks", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(6));
            InsertWord(connection, "embankment", "W:embankment", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(7));
            return true;
        });

        var service = new VocabularySearchService(database);

        var netPage = await service.SearchAsync("en", "net");
        CollectionAssert.AreEqual(
            new[] { "W:network", "W:networking" },
            netPage.Items.Select(x => x.NormalizedTerm).ToArray());

        var banksPage = await service.SearchAsync("en", "banks");
        CollectionAssert.AreEqual(
            new[] { "W:banks" },
            banksPage.Items.Select(x => x.NormalizedTerm).ToArray());
    }

    [TestMethod]
    public async Task EnglishPronounAlias_IMeMy_IncludesCanonicalWordIOnlyInEnglish()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "I", "W:i", "en", TokenKind.Word, WordStatus.Known, BaseTime.AddMinutes(1));
            InsertWord(connection, "ice", "W:ice", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "me", "W:me", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "mean", "W:mean", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            InsertWord(connection, "my", "W:my", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(5));
            InsertWord(connection, "myth", "W:myth", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(6));
            InsertWord(connection, "mine", "W:mine", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(7));

            InsertWord(connection, "I", "W:i", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(8));
            InsertWord(connection, "Meer", "W:meer", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(9));
            InsertWord(connection, "Mythos", "W:mythos", "de", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(10));
            return true;
        });

        var service = new VocabularySearchService(database);

        var enI = await service.SearchAsync("en", "I");
        CollectionAssert.AreEqual(new[] { "W:i", "W:ice" }, enI.Items.Select(x => x.NormalizedTerm).ToArray());

        var enMe = await service.SearchAsync("en", "me");
        CollectionAssert.AreEqual(new[] { "W:i", "W:me", "W:mean" }, enMe.Items.Select(x => x.NormalizedTerm).ToArray());

        var enMy = await service.SearchAsync("en", "My");
        CollectionAssert.AreEqual(new[] { "W:i", "W:my", "W:myth" }, enMy.Items.Select(x => x.NormalizedTerm).ToArray());

        var enMine = await service.SearchAsync("en", "mine");
        CollectionAssert.AreEqual(new[] { "W:mine" }, enMine.Items.Select(x => x.NormalizedTerm).ToArray());

        // German "me" and "my" must NOT include "W:i"
        var deMe = await service.SearchAsync("de", "me");
        CollectionAssert.AreEqual(new[] { "W:meer" }, deMe.Items.Select(x => x.NormalizedTerm).ToArray());

        var deMy = await service.SearchAsync("de", "my");
        CollectionAssert.AreEqual(new[] { "W:mythos" }, deMy.Items.Select(x => x.NormalizedTerm).ToArray());
    }

    [TestMethod]
    public async Task EnglishPronounAlias_OrderingAndUniquenessAcrossPages_NeverDuplicatesWColonI()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "me-acronym", "A:me", "en", TokenKind.Acronym, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "me.", "B:me.", "en", TokenKind.Abbreviation, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "me-tech-1", "T:me-1", "en", TokenKind.TechnicalTerm, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "me-tech-2", "T:me-2", "en", TokenKind.TechnicalTerm, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            InsertWord(connection, "I", "W:i", "en", TokenKind.Word, WordStatus.Known, BaseTime.AddMinutes(5));

            for (var i = 1; i <= 10; i++)
            {
                InsertWord(
                    connection,
                    $"me-word-{i:D2}",
                    $"W:me-{i:D2}",
                    "en",
                    TokenKind.Word,
                    WordStatus.UnknownBacklog,
                    BaseTime.AddMinutes(5 + i));
            }

            return true;
        });

        var service = new VocabularySearchService(database);
        var collected = new List<VocabularySearchItem>();
        VocabularySearchCursor? cursor = null;

        while (true)
        {
            var page = await service.SearchAsync("en", "me", pageSize: 3, cursor: cursor);
            Assert.AreEqual(VocabularySearchStatus.Active, page.Status);
            Assert.IsNotEmpty(page.Items);
            Assert.IsLessThanOrEqualTo(3, page.Items.Count);
            collected.AddRange(page.Items);

            if (!page.HasMore)
            {
                Assert.IsNull(page.NextCursor);
                break;
            }

            Assert.IsNotNull(page.NextCursor);
            cursor = page.NextCursor;
        }

        // 1 A: + 1 B: + 2 T: + 1 W:i + 10 W:me-.. = 15 items total
        Assert.HasCount(15, collected);
        CollectionAssert.AllItemsAreUnique(collected.Select(x => x.WordId).ToArray());
        Assert.AreEqual(1, collected.Count(x => x.NormalizedTerm == "W:i"));

        var expectedOrder = new[]
        {
            "A:me",
            "B:me.",
            "T:me-1",
            "T:me-2",
            "W:i",
            "W:me-01",
            "W:me-02",
            "W:me-03",
            "W:me-04",
            "W:me-05",
            "W:me-06",
            "W:me-07",
            "W:me-08",
            "W:me-09",
            "W:me-10"
        };
        CollectionAssert.AreEqual(expectedOrder, collected.Select(x => x.NormalizedTerm).ToArray());
    }

    [TestMethod]
    public async Task PaginationOverMoreThanOneHundredRows_NoMissingOrDuplicatedRowsAcrossPages()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var expectedTerms = new List<string>();
        await database.RunInTransactionAsync(connection =>
        {
            for (var i = 1; i <= 15; i++)
            {
                var norm = $"A:term-{i:D3}";
                expectedTerms.Add(norm);
                InsertWord(connection, $"term-A-{i:D3}", norm, "en", TokenKind.Acronym, WordStatus.UnknownBacklog, BaseTime.AddMinutes(i));
            }

            for (var i = 1; i <= 15; i++)
            {
                var norm = $"B:term-{i:D3}";
                expectedTerms.Add(norm);
                InsertWord(connection, $"term-B-{i:D3}", norm, "en", TokenKind.Abbreviation, WordStatus.UnknownBacklog, BaseTime.AddMinutes(20 + i));
            }

            for (var i = 1; i <= 25; i++)
            {
                var norm = $"T:term-{i:D3}";
                expectedTerms.Add(norm);
                InsertWord(connection, $"term-T-{i:D3}", norm, "en", TokenKind.TechnicalTerm, WordStatus.UnknownBacklog, BaseTime.AddMinutes(40 + i));
            }

            for (var i = 1; i <= 80; i++)
            {
                var norm = $"W:term-{i:D3}";
                expectedTerms.Add(norm);
                InsertWord(connection, $"term-W-{i:D3}", norm, "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime.AddMinutes(70 + i));
            }

            // Non-matching noise rows
            InsertWord(connection, "other", "W:other", "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime);
            InsertWord(connection, "term-de", "W:term-de", "de", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime);
            return true;
        });

        expectedTerms.Sort(VocabularySearchKeyPolicy.CompareUtf8Ordinal);
        Assert.HasCount(135, expectedTerms);

        var service = new VocabularySearchService(database);
        var allItems = new List<VocabularySearchItem>();
        var pageCount = 0;
        VocabularySearchCursor? cursor = null;

        while (true)
        {
            var page = await service.SearchAsync("en", "term", cursor: cursor);
            pageCount++;
            Assert.AreEqual(VocabularySearchStatus.Active, page.Status);
            Assert.AreEqual(25, page.PageSize);
            allItems.AddRange(page.Items);

            if (!page.HasMore)
            {
                Assert.HasCount(10, page.Items);
                Assert.IsNull(page.NextCursor);
                break;
            }

            Assert.HasCount(25, page.Items);
            Assert.IsNotNull(page.NextCursor);
            cursor = page.NextCursor;
        }

        Assert.AreEqual(6, pageCount);
        Assert.HasCount(135, allItems);
        CollectionAssert.AllItemsAreUnique(allItems.Select(x => x.WordId).ToArray());
        CollectionAssert.AreEqual(expectedTerms, allItems.Select(x => x.NormalizedTerm).ToArray());
    }

    [TestMethod]
    public async Task GlobalOrderingAcrossFourTokenKinds_RespectsUtf8BinaryOrderAndWordIdTieBreak()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "pro-word", "W:pro", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "pro-tech", "T:pro", "en", TokenKind.TechnicalTerm, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            InsertWord(connection, "pro-abbr", "B:pro", "en", TokenKind.Abbreviation, WordStatus.Unreviewed, BaseTime.AddMinutes(3));
            InsertWord(connection, "pro-acro", "A:pro", "en", TokenKind.Acronym, WordStatus.Unreviewed, BaseTime.AddMinutes(4));
            return true;
        });

        var service = new VocabularySearchService(database);
        var page = await service.SearchAsync("en", "pro");

        CollectionAssert.AreEqual(
            new[] { "A:pro", "B:pro", "T:pro", "W:pro" },
            page.Items.Select(x => x.NormalizedTerm).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                TokenKind.Acronym,
                TokenKind.Abbreviation,
                TokenKind.TechnicalTerm,
                TokenKind.Word
            },
            page.Items.Select(x => x.TokenKind).ToArray());
    }

    [TestMethod]
    public async Task PageSizeDefault25AndMaximum30_EnforcesContract()
    {
        Assert.AreEqual(25, IVocabularySearchService.DefaultPageSize);
        Assert.AreEqual(30, IVocabularySearchService.MaxPageSize);
        Assert.AreEqual(25, VocabularySearchService.DefaultPageSize);
        Assert.AreEqual(30, VocabularySearchService.MaxPageSize);

        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            for (var i = 1; i <= 35; i++)
            {
                InsertWord(connection, $"alpha-{i:D2}", $"W:alpha-{i:D2}", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
            }
            return true;
        });

        IVocabularySearchService service = new VocabularySearchService(database);

        var defaultPage = await service.SearchAsync("en", "alpha");
        Assert.AreEqual(25, defaultPage.PageSize);
        Assert.HasCount(25, defaultPage.Items);
        Assert.IsTrue(defaultPage.HasMore);
        Assert.IsNotNull(defaultPage.NextCursor);
        Assert.AreEqual(25, defaultPage.NextCursor.PageSize);

        var maxPage = await service.SearchAsync("en", "alpha", pageSize: 30);
        Assert.AreEqual(30, maxPage.PageSize);
        Assert.HasCount(30, maxPage.Items);
        Assert.IsTrue(maxPage.HasMore);
        Assert.IsNotNull(maxPage.NextCursor);
        Assert.AreEqual(30, maxPage.NextCursor.PageSize);
    }

    [TestMethod]
    public async Task ExactHasMoreBoundaries_ZeroMinusOneExactAndPlusOneItems()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            for (var i = 1; i <= 24; i++)
            {
                InsertWord(connection, $"b24-{i:D2}", $"W:b24-{i:D2}", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
            }

            for (var i = 1; i <= 25; i++)
            {
                InsertWord(connection, $"b25-{i:D2}", $"W:b25-{i:D2}", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
            }

            for (var i = 1; i <= 26; i++)
            {
                InsertWord(connection, $"b26-{i:D2}", $"W:b26-{i:D2}", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
            }

            return true;
        });

        var service = new VocabularySearchService(database);

        var page0 = await service.SearchAsync("en", "b00", pageSize: 25);
        Assert.IsEmpty(page0.Items);
        Assert.IsFalse(page0.HasMore);
        Assert.IsNull(page0.NextCursor);

        var page24 = await service.SearchAsync("en", "b24", pageSize: 25);
        Assert.HasCount(24, page24.Items);
        Assert.IsFalse(page24.HasMore);
        Assert.IsNull(page24.NextCursor);

        var page25 = await service.SearchAsync("en", "b25", pageSize: 25);
        Assert.HasCount(25, page25.Items);
        Assert.IsFalse(page25.HasMore, "Exact pageSize boundary must report HasMore = false without an extra page.");
        Assert.IsNull(page25.NextCursor);

        var page26First = await service.SearchAsync("en", "b26", pageSize: 25);
        Assert.HasCount(25, page26First.Items);
        Assert.IsTrue(page26First.HasMore);
        Assert.IsNotNull(page26First.NextCursor);

        var page26Second = await service.SearchAsync("en", "b26", pageSize: 25, cursor: page26First.NextCursor);
        Assert.HasCount(1, page26Second.Items);
        Assert.AreEqual("W:b26-26", page26Second.Items[0].NormalizedTerm);
        Assert.IsFalse(page26Second.HasMore);
        Assert.IsNull(page26Second.NextCursor);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(-25)]
    [DataRow(int.MinValue)]
    [DataRow(31)]
    [DataRow(50)]
    [DataRow(100)]
    [DataRow(int.MaxValue)]
    public async Task RejectionOfInvalidPageSizes_FailsSafelyWithoutDatabaseQueries(int invalidPageSize)
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();
        var countingDb = new SnapshotCountingDatabase(innerDb);

        var service = new VocabularySearchService(countingDb);
        var page = await service.SearchAsync("en", "network", pageSize: invalidPageSize);

        Assert.AreEqual(VocabularySearchStatus.InvalidPageSize, page.Status);
        Assert.IsFalse(page.IsValid);
        Assert.IsFalse(page.IsSearchActive);
        Assert.IsFalse(page.UseRecentOverview);
        Assert.IsEmpty(page.Items);
        Assert.IsFalse(page.HasMore);
        Assert.IsNull(page.NextCursor);
        Assert.AreEqual(0, countingDb.SnapshotCount, "Invalid page size must not touch the database.");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("ru")]
    [DataRow("fr")]
    [DataRow("es")]
    [DataRow("system")]
    [DataRow("en-US")]
    [DataRow("de-DE")]
    public async Task RejectionOfInvalidSourceLanguages_FailsSafelyWithoutDatabaseQueries(string? invalidLanguage)
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();
        var countingDb = new SnapshotCountingDatabase(innerDb);

        var service = new VocabularySearchService(countingDb);
        var page = await service.SearchAsync(invalidLanguage, "network");

        Assert.AreEqual(VocabularySearchStatus.InvalidSourceLanguage, page.Status);
        Assert.AreEqual(VocabularySearchQueryStatus.InvalidSourceLanguage, page.QueryStatus);
        Assert.IsFalse(page.IsValid);
        Assert.IsFalse(page.IsSearchActive);
        Assert.IsFalse(page.UseRecentOverview);
        Assert.IsEmpty(page.Items);
        Assert.IsFalse(page.HasMore);
        Assert.IsNull(page.NextCursor);
        Assert.AreEqual(0, countingDb.SnapshotCount, "Invalid source language must not touch the database.");
    }

    [TestMethod]
    public async Task RejectionOfOversizedAndInvalidUnicodeInput_FailsSafelyWithoutDatabaseQueries()
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();
        var countingDb = new SnapshotCountingDatabase(innerDb);

        var service = new VocabularySearchService(countingDb);

        var oversizedQuery = new string('a', VocabularySearchKeyPolicy.MaxQueryScalarCount + 1);
        var oversizedPage = await service.SearchAsync("en", oversizedQuery);
        Assert.AreEqual(VocabularySearchStatus.QueryTooLong, oversizedPage.Status);
        Assert.AreEqual(VocabularySearchQueryStatus.QueryTooLong, oversizedPage.QueryStatus);
        Assert.IsFalse(oversizedPage.IsValid);
        Assert.IsFalse(oversizedPage.IsSearchActive);
        Assert.IsEmpty(oversizedPage.Items);
        Assert.AreEqual(0, countingDb.SnapshotCount);

        var malformedUnicode = string.Concat("abc", new string(['\uD800']));
        var invalidUnicodePage = await service.SearchAsync("en", malformedUnicode);
        Assert.AreEqual(VocabularySearchStatus.InvalidUnicode, invalidUnicodePage.Status);
        Assert.AreEqual(VocabularySearchQueryStatus.InvalidUnicode, invalidUnicodePage.QueryStatus);
        Assert.IsFalse(invalidUnicodePage.IsValid);
        Assert.IsFalse(invalidUnicodePage.IsSearchActive);
        Assert.IsEmpty(invalidUnicodePage.Items);
        Assert.AreEqual(0, countingDb.SnapshotCount);
    }

    [TestMethod]
    public async Task RejectionOfIncompatibleCursors_FailsSafelyWithoutDatabaseQueries()
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();
        var countingDb = new SnapshotCountingDatabase(innerDb);

        var service = new VocabularySearchService(countingDb);
        var validCursor = new VocabularySearchCursor("en", "net", 25, "W:network", 10);

        // 1. Wrong source language
        var wrongLanguage = await service.SearchAsync("de", "net", pageSize: 25, cursor: validCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, wrongLanguage.Status);
        Assert.IsFalse(wrongLanguage.IsValid);

        // 2. Wrong normalized query
        var wrongQuery = await service.SearchAsync("en", "bank", pageSize: 25, cursor: validCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, wrongQuery.Status);

        // 3. Different query casing (changes Acronym/Abbreviation/TechnicalTerm ranges)
        var differentCaseQuery = await service.SearchAsync("en", "NET", pageSize: 25, cursor: validCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, differentCaseQuery.Status);

        // 4. Mismatched page size
        var wrongPageSize = await service.SearchAsync("en", "net", pageSize: 10, cursor: validCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, wrongPageSize.Status);

        // 5. Non-positive LastWordId
        var nonPositiveIdCursor = validCursor with { LastWordId = 0 };
        var nonPositiveIdResult = await service.SearchAsync("en", "net", pageSize: 25, cursor: nonPositiveIdCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, nonPositiveIdResult.Status);

        // 6. LastNormalizedTerm outside the active search plan's ranges/aliases
        var outOfRangeCursor = validCursor with { LastNormalizedTerm = "W:bank" };
        var outOfRangeResult = await service.SearchAsync("en", "net", pageSize: 25, cursor: outOfRangeCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, outOfRangeResult.Status);

        // 7. Invalid Unicode in LastNormalizedTerm
        var badUnicodeCursor = validCursor with { LastNormalizedTerm = new string(['\uD800']) };
        var badUnicodeResult = await service.SearchAsync("en", "net", pageSize: 25, cursor: badUnicodeCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, badUnicodeResult.Status);

        // 8. Cursor supplied with empty query
        var emptyQueryWithCursor = await service.SearchAsync("en", "   ", pageSize: 25, cursor: validCursor);
        Assert.AreEqual(VocabularySearchStatus.InvalidCursor, emptyQueryWithCursor.Status);
        Assert.IsFalse(emptyQueryWithCursor.IsValid);

        Assert.AreEqual(0, countingDb.SnapshotCount, "All incompatible cursors must fail before touching SQLite.");
    }

    [TestMethod]
    public async Task DistinctPersistedWordIds_PreservedWhenCanonicalTermsCollide()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int idAcronym = 0, idWord = 0;
        await database.RunInTransactionAsync(connection =>
        {
            // Both rows have identical displayed CanonicalTerm = "IT", but distinct NormalizedTerm and Id
            idWord = InsertWord(connection, "IT", "W:it", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            idAcronym = InsertWord(connection, "IT", "A:IT", "en", TokenKind.Acronym, WordStatus.Unreviewed, BaseTime.AddMinutes(2));
            return true;
        });

        Assert.AreNotEqual(idWord, idAcronym);

        var service = new VocabularySearchService(database);
        var page = await service.SearchAsync("en", "IT");

        Assert.HasCount(2, page.Items);
        Assert.AreEqual(idAcronym, page.Items[0].WordId);
        Assert.AreEqual("IT", page.Items[0].CanonicalTerm);
        Assert.AreEqual("A:IT", page.Items[0].NormalizedTerm);
        Assert.AreEqual(idWord, page.Items[1].WordId);
        Assert.AreEqual("IT", page.Items[1].CanonicalTerm);
        Assert.AreEqual("W:it", page.Items[1].NormalizedTerm);
    }

    [TestMethod]
    public async Task NoFabricatedSchema14LearningStatus_ReturnsAllValidWordAndSenseStates()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "status-unreviewed", "W:status-unreviewed", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(1));
            InsertWord(connection, "status-known", "W:status-known", "en", TokenKind.Word, WordStatus.Known, BaseTime.AddMinutes(2));
            InsertWord(connection, "status-backlog", "W:status-backlog", "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime.AddMinutes(3));
            InsertWord(connection, "status-ignored", "W:status-ignored", "en", TokenKind.Word, WordStatus.Ignored, BaseTime.AddMinutes(4));

            var learningWordId = InsertWord(connection, "status-learning-sense", "W:status-learning-sense", "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime.AddMinutes(5));
            InsertSense(connection, learningWordId, SenseStatus.Learning);

            var preparedWordId = InsertWord(connection, "status-prepared-sense", "W:status-prepared-sense", "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime.AddMinutes(6));
            InsertSense(connection, preparedWordId, SenseStatus.Prepared);

            return true;
        });

        var service = new VocabularySearchService(database);
        var page = await service.SearchAsync("en", "status-");

        Assert.HasCount(6, page.Items);
        CollectionAssert.AreEqual(
            new[]
            {
                "status-backlog",
                "status-ignored",
                "status-known",
                "status-learning-sense",
                "status-prepared-sense",
                "status-unreviewed"
            },
            page.Items.Select(x => x.CanonicalTerm).ToArray());

        // Verify VocabularySearchItem does not expose any fabricated or obsolete word-wide learning status property
        var itemProperties = typeof(VocabularySearchItem).GetProperties().Select(p => p.Name).ToArray();
        CollectionAssert.DoesNotContain(itemProperties, "Status");
        CollectionAssert.DoesNotContain(itemProperties, "WordStatus");
        CollectionAssert.DoesNotContain(itemProperties, "LearningStatus");
    }

    [TestMethod]
    public async Task SnapshotConsistencyAndReadOnlyBehavior_SingleSnapshotPerPageAndNoMutations()
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();

        await innerDb.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "alpha", "W:alpha", "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime);
            InsertWord(connection, "ALPHA", "A:ALPHA", "en", TokenKind.Acronym, WordStatus.UnknownBacklog, BaseTime.AddMinutes(1));
            return true;
        });

        var beforeWords = await innerDb.ReadAsync(conn => conn.QueryAsync<WordEntity>("SELECT * FROM Words ORDER BY Id"));

        var countingDb = new SnapshotCountingDatabase(innerDb);
        var service = new VocabularySearchService(countingDb);

        var page = await service.SearchAsync("en", "alpha");
        Assert.AreEqual(VocabularySearchStatus.Active, page.Status);
        Assert.AreEqual(1, countingDb.SnapshotCount, "All SQLite reads for one search page must run inside a single snapshot.");
        Assert.AreEqual(0, countingDb.WriteTransactionCount, "Search service must never call RunInTransactionAsync.");

        var afterWords = await innerDb.ReadAsync(conn => conn.QueryAsync<WordEntity>("SELECT * FROM Words ORDER BY Id"));
        Assert.HasCount(beforeWords.Count, afterWords);
        for (var i = 0; i < beforeWords.Count; i++)
        {
            Assert.AreEqual(beforeWords[i].Id, afterWords[i].Id);
            Assert.AreEqual(beforeWords[i].CanonicalTerm, afterWords[i].CanonicalTerm);
            Assert.AreEqual(beforeWords[i].NormalizedTerm, afterWords[i].NormalizedTerm);
            Assert.AreEqual(beforeWords[i].Status, afterWords[i].Status);
            Assert.AreEqual(beforeWords[i].UpdatedAt, afterWords[i].UpdatedAt);
        }
    }

    [TestMethod]
    public async Task DependencyInjection_MauiProgramRegistersVocabularySearchServiceAsSingleton()
    {
        var services = new ServiceCollection();
        await using var database = new ProductionInitializedDatabase();
        services.AddSingleton<IKnownFirstDatabase>(database);
        services.AddSingleton<IVocabularySearchService, VocabularySearchService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var first = provider.GetRequiredService<IVocabularySearchService>();
        var second = provider.GetRequiredService<IVocabularySearchService>();
        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.IsInstanceOfType<VocabularySearchService>(first);

        var repoRoot = FindRepositoryRoot();
        var mauiProgramSource = File.ReadAllText(Path.Combine(repoRoot, "MauiProgram.cs"));
        StringAssert.Contains(
            mauiProgramSource,
            "builder.Services.AddSingleton<IVocabularySearchService, VocabularySearchService>();");
    }

    [TestMethod]
    public async Task SqlParameterization_UserInputNeverInterpolatedAndInjectionPayloadsSafe()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "safe-word", "W:safe-word", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime);
            return true;
        });

        var maliciousQuery = "'; DROP TABLE Words; --";
        var plan = VocabularySearchKeyPolicy.CreatePlan("en", maliciousQuery);
        var commands = VocabularySearchService.BuildSqlCommands(plan, 25);

        Assert.IsNotEmpty(commands);
        foreach (var cmd in commands)
        {
            Assert.IsFalse(cmd.CommandText.Contains(maliciousQuery, StringComparison.Ordinal));
            Assert.IsFalse(cmd.CommandText.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(cmd.CommandText.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(cmd.CommandText.Contains("COLLATE NOCASE", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(cmd.CommandText.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        }

        var service = new VocabularySearchService(database);
        var page = await service.SearchAsync("en", maliciousQuery);
        Assert.AreEqual(VocabularySearchStatus.Active, page.Status);
        Assert.IsEmpty(page.Items);

        var remainingCount = await database.ReadAsync(conn => conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(1, remainingCount);
    }

    [TestMethod]
    public async Task BoundedCandidateMaterialization_FetchesAtMostPageSizePlusOnePerRangeWithoutCountQuery()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            for (var i = 1; i <= 100; i++)
            {
                InsertWord(connection, $"item-w-{i:D3}", $"W:item-{i:D3}", "en", TokenKind.Word, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
                InsertWord(connection, $"item-a-{i:D3}", $"A:item-{i:D3}", "en", TokenKind.Acronym, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
                InsertWord(connection, $"item-b-{i:D3}", $"B:item-{i:D3}", "en", TokenKind.Abbreviation, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
                InsertWord(connection, $"item-t-{i:D3}", $"T:item-{i:D3}", "en", TokenKind.TechnicalTerm, WordStatus.Unreviewed, BaseTime.AddMinutes(i));
            }
            return true;
        });

        var plan = VocabularySearchKeyPolicy.CreatePlan("en", "item");
        var commands = VocabularySearchService.BuildSqlCommands(plan, pageSize: 25);
        Assert.HasCount(4, commands);
        foreach (var cmd in commands)
        {
            Assert.AreEqual(26, cmd.FetchLimit);
            Assert.IsFalse(cmd.CommandText.Contains("COUNT(", StringComparison.OrdinalIgnoreCase));
        }

        int executedQueries = 0;
        int materializedCandidates = 0;
        var page = await database.ExecuteSnapshotAsync(connection =>
            VocabularySearchService.ExecuteSnapshotCore(
                connection,
                plan,
                pageSize: 25,
                cursor: null,
                out executedQueries,
                out materializedCandidates));

        Assert.AreEqual(4, executedQueries);
        // 4 ranges * (25 + 1) = 104 max materialized rows out of 400 matching rows in DB
        Assert.AreEqual(104, materializedCandidates);
        Assert.HasCount(25, page.Items);
        Assert.IsTrue(page.HasMore);
    }

    [TestMethod]
    public async Task ExplainQueryPlan_AllTokenKindRangesAndAlias_UseCompositeIndexRangeLookupWithoutScanOrIndexedBy()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        await database.RunInTransactionAsync(connection =>
        {
            InsertWord(connection, "I", "W:i", "en", TokenKind.Word, WordStatus.Known, BaseTime);
            InsertWord(connection, "mean", "W:mean", "en", TokenKind.Word, WordStatus.UnknownBacklog, BaseTime.AddMinutes(1));
            InsertWord(connection, "ME", "A:ME", "en", TokenKind.Acronym, WordStatus.UnknownBacklog, BaseTime.AddMinutes(2));
            return true;
        });

        // 1. Initial page plan for "me" (includes 4 TokenKind prefix ranges + 1 exact alias "W:i")
        var initialPlan = VocabularySearchKeyPolicy.CreatePlan("en", "me");
        var initialCommands = VocabularySearchService.BuildSqlCommands(initialPlan, pageSize: 25, cursor: null);
        Assert.HasCount(5, initialCommands);

        // 2. Continuation page plan for "me" with cursor inside W:me..W:mf
        var cursorInsideWordRange = new VocabularySearchCursor("en", "me", 25, "W:mean", 2);
        var continuationCommands = VocabularySearchService.BuildSqlCommands(initialPlan, pageSize: 25, cursor: cursorInsideWordRange);
        Assert.HasCount(1, continuationCommands, "Once cursor reaches W:mean, A:, B:, T:, and W:i are already before cursor.");

        // 3. Continuation page plan for "me" when cursor is exactly at the pronoun alias "W:i"
        var cursorAtAlias = new VocabularySearchCursor("en", "me", 25, "W:i", 1);
        var aliasContinuationCommands = VocabularySearchService.BuildSqlCommands(initialPlan, pageSize: 25, cursor: cursorAtAlias);
        Assert.HasCount(2, aliasContinuationCommands, "When cursor is at W:i, only W:i (Id > cursor) and W:me..W:mf remain.");

        await database.ExecuteSnapshotAsync(connection =>
        {
            foreach (var cmd in initialCommands.Concat(continuationCommands).Concat(aliasContinuationCommands))
            {
                Assert.IsFalse(
                    cmd.CommandText.Contains("INDEXED BY", StringComparison.OrdinalIgnoreCase),
                    "SQL must not force INDEXED BY.");

                var planRows = connection.Query<ExplainQueryPlanRow>(
                    $"EXPLAIN QUERY PLAN {cmd.CommandText}",
                    cmd.Parameters.ToArray());

                Assert.IsNotEmpty(planRows, "EXPLAIN QUERY PLAN must return at least one row.");
                var details = string.Join(" | ", planRows.Select(r => r.Detail));

                StringAssert.Contains(details, "SEARCH Words USING INDEX IX_Words_Language_NormalizedTerm");
                Assert.IsFalse(
                    details.Contains("SCAN", StringComparison.OrdinalIgnoreCase),
                    $"Query plan must not perform a table or full-index SCAN: {details}");
                Assert.IsFalse(
                    details.Contains("USE TEMP B-TREE", StringComparison.OrdinalIgnoreCase),
                    $"Query plan must not require a temporary B-tree sort: {details}");

                if (cmd.IsExactAlias)
                {
                    Assert.AreEqual(
                        "SEARCH Words USING INDEX IX_Words_Language_NormalizedTerm (Language=? AND NormalizedTerm=?)",
                        details);
                }
                else
                {
                    Assert.AreEqual(
                        "SEARCH Words USING INDEX IX_Words_Language_NormalizedTerm (Language=? AND NormalizedTerm>? AND NormalizedTerm<?)",
                        details);
                }
            }

            return true;
        });
    }

    private sealed class ExplainQueryPlanRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("parent")]
        public int Parent { get; set; }

        [Column("notused")]
        public int NotUsed { get; set; }

        [Column("detail")]
        public string Detail { get; set; } = string.Empty;
    }

    private sealed class SnapshotCountingDatabase(IKnownFirstDatabase inner) : IKnownFirstDatabase
    {
        public int SnapshotCount { get; private set; }
        public int WriteTransactionCount { get; private set; }

        public string DatabasePath => inner.DatabasePath;

        public Task InitializeAsync() => inner.InitializeAsync();

        public Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation) =>
            inner.ReadAsync(operation);

        public Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            WriteTransactionCount++;
            return inner.RunInTransactionAsync(operation);
        }

        public Task ResetAsync() => inner.ResetAsync();

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
        {
            SnapshotCount++;
            return inner.ExecuteSnapshotAsync(operation);
        }
    }

    private static int InsertWord(
        SQLiteConnection connection,
        string canonicalTerm,
        string normalizedTerm,
        string language,
        TokenKind tokenKind,
        WordStatus status,
        DateTime createdAt)
    {
        connection.Execute(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            language,
            canonicalTerm,
            normalizedTerm,
            (int)status,
            (int)tokenKind,
            createdAt,
            createdAt);

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static void InsertSense(SQLiteConnection connection, int wordId, SenseStatus status)
    {
        connection.Execute(
            """
            INSERT INTO Senses
                (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                 PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, 'en', 'de', '', '', '', '', '', NULL, ?, ?, ?)
            """,
            Guid.NewGuid().ToString("N"),
            wordId,
            (int)status,
            BaseTime,
            BaseTime);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the KnownFirst repository root.");
    }
}
