using System.Collections.Concurrent;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// Regression coverage for the stale-reader defect fixed in
/// <see cref="TextReviewService"/>'s diagnostics meaning projection and
/// <see cref="BackupModelMapper.ParseLookupDraft"/>: both used to deserialize
/// <c>PreparationCandidates.ResultJson</c> directly as a raw <see cref="LexicalResult"/> via
/// <see cref="LexicalJsonSerializerContext"/>, bypassing the current, correct, discriminated
/// <see cref="PreparationCandidatePayloadCodec.Read"/> contract every writer already uses. For a real
/// Schema-8 candidate accepted through <see cref="PreparationService"/>, the raw shape does not throw
/// during deserialization (the envelope's top-level JSON properties do not match any
/// <see cref="LexicalResult"/> property name), so it silently produces a non-null
/// <see cref="LexicalResult"/> with <c>Meanings == null</c> — an uncaught
/// <see cref="ArgumentNullException"/> from diagnostics, and a caught-but-wrong
/// <see cref="BackupFormatException"/> from portable export.
///
/// Every fixture here accepts through the real <see cref="PreparationService"/> (never raw entity
/// inserts), so <c>PreparationCandidates.ResultJson</c> genuinely holds a codec-written
/// <see cref="PreparationCandidatePayloadV1"/> envelope — the exact shape the prior stale readers
/// mishandled.
/// </summary>
[TestClass]
public sealed class DiagnosticsExportImportStaleReaderRegressionTests
{
    private static readonly DateTime Now = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    private TemporarySchema8Database _database = null!;
    private FakeClock _clock = null!;
    private TextReviewService _review = null!;
    private MutableProvider _provider = null!;
    private PreparationService _preparation = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporarySchema8Database("knownfirst-stale-reader-regression");
        await _database.InitializeAsync();
        // This class characterizes a stale-reader defect in the JSON envelope PreparationService writes,
        // not any behavior tied to a literal PRAGMA user_version — the codec-written envelope shape is
        // unchanged across Schema 8-11. TextReviewService's review-selection/completion methods used for
        // setup now require the current schema, so the fixture upgrades immediately after construction.
        await _database.UpgradeToCurrentSchemaAsync();
        _clock = new FakeClock(Now);
        _review = new TextReviewService(
            _database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        _provider = new MutableProvider(_clock);
        _preparation = CreatePreparationService(_provider);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _preparation.CancelPrefetchAsync();
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task Diagnostics_AfterRealSchema8PreparationAcceptance_SucceedsAndReturnsPreparedMeaning()
    {
        await AcceptBankFinancialInstitutionAsync();

        var diagnostics = await _review.GetDiagnosticsAsync();

        Assert.HasCount(1, diagnostics.PreparationCandidates);
        var candidate = diagnostics.PreparationCandidates[0];
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status);
        Assert.HasCount(1, candidate.AvailableMeanings);
        Assert.AreEqual(
            "Translation wikt-financial-institution — Definition for wikt-financial-institution",
            candidate.AvailableMeanings[0]);
    }

    [TestMethod]
    public async Task ExportThenSelfImportIntoFreshEmptyTarget_SucceedsAndRestoresIntoEmpty()
    {
        await AcceptBankFinancialInstitutionAsync();

        var sourceService = new BackupService(_database, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        using var archive = new MemoryStream();
        await sourceService.CreatePortableArchiveAsync(archive, CancellationToken.None);
        Assert.IsGreaterThan(0, archive.Length, "the export must produce a non-empty archive");
        var archiveBytes = archive.ToArray();

        await using var target = new TemporarySchema8Database("knownfirst-stale-reader-regression-target");
        await target.InitializeAsync();
        var targetService = new BackupService(target, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        using var previewStream = new MemoryStream(archiveBytes);
        var preview = await targetService.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        Assert.AreEqual(PortableImportPreviewDisposition.RestoreIntoEmpty, preview.Disposition);

        using var importStream = new MemoryStream(archiveBytes);
        var result = await targetService.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.IsNotNull(result.Summary);
        Assert.AreEqual(PortableImportDisposition.RestoredIntoEmpty, result.Summary!.Disposition);

        var restoredMeaningCount = await target.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(1, restoredMeaningCount);
        var restoredTranslation = await target.ReadAsync(c => c.ExecuteScalarAsync<string>("SELECT Translation FROM Meanings"));
        Assert.AreEqual("Translation wikt-financial-institution", restoredTranslation);

        await AssertDatabaseIntegrityAsync(_database, "source");
        await AssertDatabaseIntegrityAsync(target, "target");
    }

    private async Task AcceptBankFinancialInstitutionAsync()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both);

        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>()
            .Where(x => x.WordId == wordId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status);

        // Guards the fixture itself: this must be a genuine codec-written Schema-8 envelope, not a raw
        // legacy shape — otherwise this regression suite would silently stop exercising the defect.
        var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson);
        Assert.AreEqual(
            PreparationCandidatePayloadKind.EnvelopeV1,
            envelope.Kind,
            "the fixture must hold a codec-written EnvelopeV1, not a raw legacy LexicalResult");
    }

    private static async Task AssertDatabaseIntegrityAsync(IKnownFirstDatabase database, string label)
    {
        var integrity = await database.ReadAsync(c => c.ExecuteScalarAsync<string>("PRAGMA integrity_check"));
        Assert.AreEqual("ok", integrity, $"{label} database failed integrity_check");
        var violations = await database.ReadAsync(c => c.QueryAsync<ForeignKeyViolationRow>("PRAGMA foreign_key_check"));
        Assert.IsEmpty(violations, $"{label} database has foreign_key_check violations");
    }

    private sealed class ForeignKeyViolationRow
    {
        public string Table { get; set; } = string.Empty;
        public int RowId { get; set; }
        public string Parent { get; set; } = string.Empty;
        public int FKId { get; set; }
    }

    private PreparationService CreatePreparationService(MutableProvider provider) => new(
        _database,
        new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(_database),
            new LexicalLookupProviderResolver([provider])),
        _clock,
        diagnosticLog: null);

    private async Task<int> ImportSingleUnknownAsync(string content, string unknownTerm)
    {
        var request = new ImportTextRequest($"Document {Guid.NewGuid():N}", content, "en", LexicalLookupMode.Definition, null);
        var result = await _review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var wordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, unknownTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
            }

            await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
        }

        Assert.AreNotEqual(-1, wordId);
        return wordId;
    }

    private static LexicalMeaning Meaning(string providerMeaningId, string partOfSpeech = "noun") =>
        new(providerMeaningId, partOfSpeech, $"Definition for {providerMeaningId}", $"Translation {providerMeaningId}", null, []);

    private static PreparedMeaningInput InputFrom(PreparationItem item) => InputFrom(item, item.SelectedMeaningIndex);

    private static PreparedMeaningInput InputFrom(PreparationItem item, int meaningIndex)
    {
        var result = item.Result ?? throw new InvalidOperationException("The item has no result.");
        var meaning = result.Meanings[meaningIndex];
        return new PreparedMeaningInput(
            meaning.MeaningId,
            result.AcronymExpansion,
            meaning.Translation,
            meaning.Definition,
            meaning.Example,
            null,
            [],
            result.ProviderName,
            result.SourceProject,
            result.PageTitle,
            result.RevisionId,
            result.Attribution,
            item.EncounteredSurfaceForm,
            result.GrammaticalRelationship);
    }

    private sealed class MutableProvider(FakeClock clock) : IDictionaryLookupProvider
    {
        private readonly ConcurrentQueue<LexicalLookupRequest> _requests = new();

        public Func<LexicalLookupRequest, IReadOnlyList<LexicalMeaning>> MeaningsFactory { get; set; } =
            _ => [new LexicalMeaning("primary", "noun", "Definition", null, null, [])];

        public string ProviderName => "Wiktionary";

        public int ProviderSchemaVersion => 1;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Enqueue(request);
            var result = new LexicalResult(
                LexicalLookupStatus.Success,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                MeaningsFactory(request),
                ProviderName,
                "en.wiktionary.org",
                request.Term,
                1,
                "Wiktionary contributors",
                clock.UtcNow,
                LookupMode: request.LookupMode,
                TargetLanguage: request.TargetLanguage);
            return Task.FromResult(result);
        }
    }
}
