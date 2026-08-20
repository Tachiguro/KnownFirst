using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// German Enhanced Term Recognition Package 5A: a derived component decided Unknown intentionally has no
/// <see cref="WordOccurrenceEntity"/> rows (per the German derived-compound contract), so Preparation must
/// build its display context from the surviving <see cref="DerivedTermEvidenceEntity"/> row(s)
/// <c>TextReviewService.CompleteSession</c> retains for it — the real whole-compound source span, never a
/// fabricated component occurrence — and that retained evidence must be cleaned up once the word leaves
/// the Unknown lifecycle through MarkKnown/Exclude.
/// </summary>
[TestClass]
public sealed class GermanDerivedTermPreparationTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private TemporarySchema8Database _database = null!;
    private FakeClock _clock = null!;
    private TextReviewService _review = null!;
    private FixedMeaningProvider _provider = null!;
    private PreparationService _preparation = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporarySchema8Database("knownfirst-german-derived-preparation");
        await _database.InitializeAsync();
        await _database.UpgradeToCurrentSchemaAsync();
        _clock = new FakeClock(Now);
        _review = new TextReviewService(
            _database, new TextAnalyzer(), new EnabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        _provider = new FixedMeaningProvider(_clock);
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
    public async Task DerivedUnknownPreparation_UsesSourceCompoundEvidenceForContext()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();

        var occurrenceCount = await _database.ReadAsync(conn =>
            conn.Table<WordOccurrenceEntity>().Where(o => o.WordId == maschineWordId).CountAsync());
        Assert.AreEqual(0, occurrenceCount, "The derived component must have zero WordOccurrence rows.");

        await _preparation.StartAsync(PreparationMethod.Manual, 20);
        var item = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(item);
        Assert.AreEqual(maschineWordId, item!.WordId);

        Assert.IsNotEmpty(item.Contexts, "Preparation must build real context from the surviving derived evidence.");
        var context = item.Contexts[0];
        Assert.AreEqual("Die Schreibmaschine steht hier.", context.Text);
        Assert.AreEqual(
            "Schreibmaschine",
            context.Text.Substring(context.TargetStart, context.TargetLength),
            "The displayed target must be the real whole-compound source span, never a fabricated component occurrence.");

        await _preparation.AcceptAsync(item.CandidateId, InputFrom(item, 0), CardDirectionPreference.Both);

        var snapshotCount = await _database.ReadAsync(conn =>
            conn.Table<ContextSnapshotEntity>().Where(s => s.WordId == maschineWordId).CountAsync());
        Assert.IsGreaterThan(0, snapshotCount, "Accepting the derived term must persist a normal ContextSnapshot.");

        var occurrenceCountAfterAccept = await _database.ReadAsync(conn =>
            conn.Table<WordOccurrenceEntity>().Where(o => o.WordId == maschineWordId).CountAsync());
        Assert.AreEqual(0, occurrenceCountAfterAccept, "Accepting must still never fabricate a WordOccurrence.");
    }

    /// <summary>
    /// Review-correction (MINOR): the display path (<c>ResolveFrozenContextsAsync</c>) gated its
    /// derived-evidence fallback on the raw <c>WordOccurrenceEntity</c> row count, while the Accept path
    /// (<c>ResolveContextDataFromFrozenEvidence</c>) already gated on the post-validation occurrence-context
    /// count. This constructs a word with a raw occurrence row that exists but fails coordinate/substring
    /// validation — so zero valid occurrence-based contexts remain — and proves both paths agree to fall
    /// back to the real derived evidence rather than the display path silently returning nothing.
    /// </summary>
    [TestMethod]
    public async Task DerivedUnknownPreparation_DisplayAndAcceptAgreeOnDerivedFallbackWhenOccurrencesAreInvalid()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();

        var (documentId, sentenceSpanId) = await _database.ReadAsync(async conn =>
        {
            var document = await conn.Table<DocumentEntity>().FirstAsync();
            var sentence = await conn.Table<SentenceSpanEntity>().Where(s => s.DocumentId == document.Id).FirstAsync();
            return (document.Id, sentence.Id);
        });
        await _database.RunInTransactionAsync(conn =>
        {
            // A raw occurrence row for the derived word that exists but does not validate: the real
            // document text at [0..3) is "Die", not "XXX", so TryCreateContext must reject it.
            conn.Insert(new WordOccurrenceEntity
            {
                WordId = maschineWordId,
                DocumentId = documentId,
                SentenceSpanId = sentenceSpanId,
                StartPosition = 0,
                Length = 3,
                SurfaceForm = "XXX",
                Order = 999
            });
            return true;
        });

        await _preparation.StartAsync(PreparationMethod.Manual, 20);

        var displayItem = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(displayItem);
        Assert.AreEqual(maschineWordId, displayItem!.WordId);
        Assert.IsNotEmpty(
            displayItem.Contexts,
            "Display must fall back to derived evidence when raw occurrence rows exist but none validate.");

        var lookupItem = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(lookupItem);
        await _preparation.AcceptAsync(lookupItem!.CandidateId, InputFrom(lookupItem, 0), CardDirectionPreference.Both);

        var snapshotCount = await _database.ReadAsync(conn =>
            conn.Table<ContextSnapshotEntity>().Where(s => s.WordId == maschineWordId).CountAsync());
        Assert.IsGreaterThan(
            0,
            snapshotCount,
            "Accept must also fall back to derived evidence in the same situation, keeping display/Accept parity.");
    }

    [TestMethod]
    public async Task CompletingPreparationWithoutLearning_RemovesRetainedDerivationEvidence()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();

        var evidenceBefore = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.IsGreaterThan(0, evidenceBefore, "Retained derivation evidence must exist before MarkKnown.");
        var candidateCountBefore = await _database.ReadAsync(conn =>
            conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == maschineWordId).CountAsync());
        Assert.IsGreaterThan(0, candidateCountBefore, "The owning ReviewCandidate must be retained before MarkKnown.");

        await _preparation.StartAsync(PreparationMethod.Manual, 20);
        var item = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        Assert.AreEqual(maschineWordId, item!.WordId);

        await _preparation.MarkKnownAsync(item.CandidateId);

        var evidenceAfter = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(0, evidenceAfter, "MarkKnown must remove retained derivation evidence once the word leaves the Unknown lifecycle.");
        var candidateCountAfter = await _database.ReadAsync(conn =>
            conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == maschineWordId).CountAsync());
        Assert.AreEqual(0, candidateCountAfter, "MarkKnown must remove the retained owning ReviewCandidate.");

        var word = await _database.ReadAsync(conn => conn.Table<WordEntity>().Where(w => w.Id == maschineWordId).FirstAsync());
        Assert.AreEqual(WordStatus.Known, word.Status);
    }

    /// <summary>
    /// Review-correction (MAJOR): before Package 5A, a Completed ReviewSession's ReviewCandidates were
    /// always fully deleted by normal in-app completion, so the portable-archive export's candidate-item
    /// list was always empty for such a Completed session. Package 5A's retention of an Unknown derived
    /// candidate's ReviewCandidateEntity makes a non-empty item list reachable for a normally-completed
    /// session for the first time. This must not export that specific derived-evidence-only candidate
    /// (DerivedTermEvidenceEntries themselves are still never exported anywhere, so such an item would
    /// carry no provenance on the target side) — while a Completed session's other, non-derived candidates
    /// (e.g. one legitimately written back by restore/merge) must keep exporting exactly as before; see
    /// <see cref="BackupCreationTests.PortableExport_NeverEmitsTwoReviewItemsSharingOneVocabularyIdInOneWorkflow"/>.
    /// </summary>
    [TestMethod]
    public async Task PortableArchiveExport_DoesNotExportRetainedCandidateItemsForCompletedSession()
    {
        await ImportGermanReviewWithMaschineUnknownAsync();

        var evidenceCount = await _database.ReadAsync(conn => conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.IsGreaterThan(0, evidenceCount, "Precondition: retained derivation evidence must exist.");

        Schema8BackupSnapshot? snapshot = null;
        await _database.RunInTransactionAsync(conn =>
        {
            var captured = Schema8BackupSnapshotRepository.CaptureSnapshot(conn);
            snapshot = Schema8BackupSnapshotRepository.WithSchema11DerivedEvidenceOwningCandidateIds(conn, captured);
            return true;
        });
        Assert.IsNotNull(snapshot);
        Assert.IsGreaterThan(
            0,
            snapshot!.ReviewCandidates.Count,
            "Precondition: the raw snapshot capture must still see the retained candidate row internally.");
        Assert.IsNotEmpty(
            snapshot.DerivedTermEvidenceOwningReviewCandidateIds!,
            "Precondition: the Schema-11 enrichment step must identify the retained candidate as derived-evidence-owning.");

        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        var completedWorkflow = payload.Workflows.VocabularyReviews
            .Single(workflow => workflow.Status == BackupReviewSessionStatus.Completed);
        Assert.IsEmpty(
            completedWorkflow.Items,
            "A Completed review session must not export a candidate item that is retained only for its derived-evidence provenance.");
    }

    /// <summary>
    /// Review-correction (BLOCKER): the generic document-cleanup sweep
    /// (<c>DocumentCleanupOperations.CleanupEligibleDocuments</c>), reached from any unrelated
    /// MarkKnown/Exclude action anywhere in the app, did not recognize retained derived evidence and could
    /// therefore delete the Document/SentenceSpan/ReviewCandidate a surviving DerivedTermEvidenceEntries
    /// row depends on, orphaning it and making the next <see cref="DatabaseSchema.InitializeAsync"/> fail
    /// closed. This proves the trigger — acting on a completely unrelated word — does not disturb the
    /// German document, and that a full startup reopen still succeeds afterward.
    /// </summary>
    [TestMethod]
    public async Task GenericDocumentCleanup_DoesNotOrphanRetainedDerivedEvidence()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();

        var germanDocumentId = await _database.ReadAsync(async conn =>
        {
            var candidate = await conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == maschineWordId).FirstAsync();
            var session = await conn.FindAsync<ReviewSessionEntity>(candidate.SessionId);
            return session.DocumentId;
        });

        var germanOccurrenceCount = await _database.ReadAsync(conn =>
            conn.Table<WordOccurrenceEntity>().Where(o => o.DocumentId == germanDocumentId).CountAsync());
        Assert.AreEqual(
            0,
            germanOccurrenceCount,
            "Precondition: the German document must have zero occurrences, matching the generic cleanup's own eligibility signal.");

        // An unrelated, ordinary English import/decision — the retained derived word itself is never
        // touched by this trigger.
        var englishResult = await _review.ImportAsync(new ImportTextRequest("English import", "harbor.", "en", "en"));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, englishResult.Outcome);
        var englishCandidate = await _review.GetCurrentCandidateAsync();
        Assert.IsNotNull(englishCandidate);
        await _review.DecideAsync(englishCandidate!.WordId, WordStatus.UnknownBacklog);

        await _preparation.StartAsync(PreparationMethod.Manual, 20);
        var englishItem = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(englishItem);
        Assert.AreNotEqual(
            maschineWordId,
            englishItem!.WordId,
            "The trigger must act on the unrelated word, not the retained derived word.");

        // Triggers PreparationService.CompleteWithoutLearningAsync, which calls
        // DocumentCleanupOperations.CleanupEligibleDocuments across every document in the database.
        await _preparation.MarkKnownAsync(englishItem.CandidateId);

        var germanDocumentSurvives = await _database.ReadAsync(conn =>
            conn.Table<DocumentEntity>().Where(d => d.Id == germanDocumentId).CountAsync()) > 0;
        Assert.IsTrue(
            germanDocumentSurvives,
            "The generic document cleanup must not delete a document whose only remaining reason to exist is retained derived evidence.");

        var evidenceCount = await _database.ReadAsync(conn => conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.IsGreaterThan(0, evidenceCount, "Retained derivation evidence must survive the unrelated cleanup trigger.");

        // Reopen through the real startup path — proves no dependency (Document/SentenceSpan/
        // ReviewCandidate) was silently deleted while the evidence referencing it survived.
        var reopenedConnection = new SQLiteAsyncConnection(_database.DatabasePath);
        try
        {
            await DatabaseSchema.InitializeAsync(reopenedConnection);
        }
        finally
        {
            await reopenedConnection.CloseAsync();
        }
    }

    /// <summary>
    /// Negative counterpart to <see cref="GenericDocumentCleanup_DoesNotOrphanRetainedDerivedEvidence"/>:
    /// a document with no retained derived evidence at all must remain fully cleanup-eligible once its
    /// only word leaves the Unknown lifecycle — the correction must not broadly protect unrelated
    /// documents.
    /// </summary>
    [TestMethod]
    public async Task GenericDocumentCleanup_StillDeletesGenuinelyUnreferencedDocument()
    {
        var result = await _review.ImportAsync(new ImportTextRequest("English import", "harbor.", "en", "en"));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var candidate = await _review.GetCurrentCandidateAsync();
        Assert.IsNotNull(candidate);
        await _review.DecideAsync(candidate!.WordId, WordStatus.UnknownBacklog);

        await _preparation.StartAsync(PreparationMethod.Manual, 20);
        var item = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        await _preparation.MarkKnownAsync(item!.CandidateId);

        var documentSurvives = await _database.ReadAsync(conn =>
            conn.Table<DocumentEntity>().Where(d => d.Id == result.DocumentId).CountAsync()) > 0;
        Assert.IsFalse(
            documentSurvives,
            "A genuinely unreferenced document with no retained derived evidence must remain cleanup-eligible.");
    }

    /// <summary>
    /// Drives an active review to completion for "Die Schreibmaschine steht hier.", deciding the derived
    /// "maschine" candidate Unknown and every other candidate (the Direct compound, derived "schreiben",
    /// and the ordinary Direct words "Die"/"steht"/"hier") Known.
    /// </summary>
    private async Task<int> ImportGermanReviewWithMaschineUnknownAsync()
    {
        var result = await _review.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var maschineWordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (candidate.Identity == "W:maschine")
            {
                maschineWordId = candidate.WordId;
                await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
                continue;
            }

            await _review.DecideAsync(candidate.WordId, WordStatus.Known);
        }

        Assert.AreNotEqual(-1, maschineWordId, "The derived 'maschine' candidate must have been reviewed.");
        return maschineWordId;
    }

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

    private sealed class EnabledEnhancedRecognitionSettings : IAppSettingsService
    {
        public int PreparationLimit => 20;
        public IReadOnlyList<int> SupportedPreparationLimits => [20];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => LearningMode.Automatic;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => true;

        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool value) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
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
                [new LexicalMeaning("primary", "noun", "Definition", "Übersetzung", null, [])],
                ProviderName,
                "de.wiktionary.org",
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
