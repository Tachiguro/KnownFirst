using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3 §7/§8: provider-index integrity (stale <c>SelectedMeaningId</c>, already-resolved
/// and out-of-range <c>SelectMeaningAsync</c> selections) and the Schema-7 zero-mutation metadata-bounds
/// guarantee.
/// </summary>
[TestClass]
public sealed class PreparationProviderIndexIntegrityTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    private TemporarySchema8Database _database = null!;
    private FakeClock _clock = null!;
    private TextReviewService _review = null!;
    private MutableProvider _provider = null!;
    private PreparationService _preparation = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporarySchema8Database("knownfirst-schema8-provider-index");
        await _database.InitializeAsync();
        // This class characterizes provider/index integrity, not literal-version behavior. The fixture
        // upgrades immediately after construction so TextReviewService's review-selection/completion
        // setup methods, which now require the current schema, keep working.
        await _database.UpgradeToCurrentSchemaAsync();
        _clock = new FakeClock(Now);
        _review = new TextReviewService(
            _database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        _provider = new MutableProvider(_clock);
        _preparation = new PreparationService(
            _database,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(_database),
                new LexicalLookupProviderResolver([_provider])),
            _clock);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _preparation.CancelPrefetchAsync();
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task Accept_StaleSelectedMeaningId_ThrowsBeforeMutation()
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution"), Meaning("wikt-river-edge")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();

        var staleInput = InputFrom(item!, 0) with { SelectedMeaningId = "wikt-river-edge" };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _preparation.AcceptAsync(item!.CandidateId, staleInput, CardDirectionPreference.Both));

        var senseCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(0, senseCount);
    }

    [TestMethod]
    public async Task SelectMeaningAsync_AlreadyResolvedIndex_Throws()
    {
        await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution"), Meaning("wikt-river-edge")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        var candidateId = item!.CandidateId;
        await _preparation.AcceptAsync(candidateId, InputFrom(item, 0), CardDirectionPreference.Both);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _preparation.SelectMeaningAsync(candidateId, 0));
    }

    [TestMethod]
    public async Task SelectMeaningAsync_OutOfRangeIndex_Throws()
    {
        await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => _preparation.SelectMeaningAsync(item!.CandidateId, 7));
    }

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
        public Func<LexicalLookupRequest, IReadOnlyList<LexicalMeaning>> MeaningsFactory { get; set; } =
            _ => [new LexicalMeaning("primary", "noun", "Definition", null, null, [])];

        public string ProviderName => "Wiktionary";

        public int ProviderSchemaVersion => 1;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
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

/// <summary>KF-MEANING-001 Slice 3 §8: Schema-7 validates TopicOrDomain/PartOfSpeech API bounds but never
/// persists them or mutates any legacy row.</summary>
[TestClass]
public sealed class PreparationSchema7MetadataZeroMutationTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Accept_Schema7_OverLimitTopicOrDomain_ThrowsBeforeAnyMutation()
    {
        var database = new TemporaryKnownFirstDatabase("knownfirst-schema7-metadata");
        await database.InitializeAsync();
        try
        {
            var clock = new FakeClock(Now);
            var review = new TextReviewService(
                database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
            var provider = new FixedMeaningProvider(clock);
            var preparation = new PreparationService(
                database,
                new LexicalEnrichmentService(
                    new AcronymExpansionDetector(),
                    new MeaningRanker(),
                    new LexicalCacheRepository(database),
                    new LexicalLookupProviderResolver([provider])),
                clock);

            var request = new ImportTextRequest($"Document {Guid.NewGuid():N}", "bank text here.", "en", LexicalLookupMode.Definition, null);
            var result = await review.ImportAsync(request);
            Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

            // This fixture must stay at genuine Schema 7 throughout, so PreparationService.AcceptAsync's
            // zero-mutation-on-validation-failure guarantee is proven against the real oldest supported
            // physical shape rather than an upgraded one. TextReviewService.GetCurrentCandidateAsync/
            // DecideAsync-to-completion now require the current schema (DerivedTermEvidenceEntries), so the
            // review-completion outcome this test needs (the imported word reaching UnknownBacklog with no
            // Active review session left blocking PreparationService.StartAsync) is applied directly instead.
            await database.RunInTransactionAsync(connection =>
            {
                var session = connection.Table<ReviewSessionEntity>().Single();
                var candidates = connection.Table<ReviewCandidateEntity>()
                    .Where(c => c.SessionId == session.Id)
                    .ToList();
                foreach (var candidate in candidates)
                {
                    var word = connection.Find<WordEntity>(candidate.WordId)
                        ?? throw new InvalidOperationException("A review candidate has no word record.");
                    word.Status = WordStatus.UnknownBacklog;
                    connection.Update(word);

                    // PreparationService.ReviewIsResolved requires the candidate's own Status to have moved
                    // off Unreviewed (not just the word), exactly as TextReviewService.DecideAsync would do.
                    candidate.Status = WordStatus.UnknownBacklog;
                    candidate.DecidedAt = DateTime.UtcNow;
                    connection.Update(candidate);
                }

                session.Status = ReviewSessionStatus.Completed;
                session.CompletedAt = DateTime.UtcNow;
                session.ReviewedCount = candidates.Count;
                session.UnknownCount = candidates.Count;
                connection.Update(session);
                return true;
            });

            await preparation.StartAsync(PreparationMethod.Manual, 1);
            var item = await preparation.LookupCurrentAsync();
            var meaning = item!.Result!.Meanings[0];
            var overLimitInput = new PreparedMeaningInput(
                meaning.MeaningId, null, meaning.Translation, meaning.Definition, null, null, [],
                item.Result.ProviderName, item.Result.SourceProject, item.Result.PageTitle, item.Result.RevisionId,
                item.Result.Attribution, item.EncounteredSurfaceForm, item.Result.GrammaticalRelationship,
                null, new string('a', PreparationMetadataPolicy.MaxTopicOrDomainUtf8Bytes + 1));

            await Assert.ThrowsExactlyAsync<PreparationMetadataValidationException>(
                () => preparation.AcceptAsync(item.CandidateId, overLimitInput, CardDirectionPreference.Both));

            var meaningCount = await database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings"));
            Assert.AreEqual(0, meaningCount);
            var word = await database.ReadAsync(c => c.Table<WordEntity>().FirstAsync());
            Assert.AreEqual(PreparationState.Preparing, word.PreparationState);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private sealed class FixedMeaningProvider(FakeClock clock) : IDictionaryLookupProvider
    {
        public string ProviderName => "Wiktionary";

        public int ProviderSchemaVersion => 1;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            var result = new LexicalResult(
                LexicalLookupStatus.Success,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                [new LexicalMeaning("wikt-financial-institution", "noun", "Definition", "Translation", null, [])],
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
