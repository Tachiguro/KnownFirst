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
    private LearningService _learning = null!;

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
        _learning = new LearningService(
            _database,
            new SimpleSpacedRepetitionScheduler(),
            new SpellingAnswerComparer(),
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
    /// Package 5B: native (non-merge) Exclude counterpart to
    /// <see cref="CompletingPreparationWithoutLearning_RemovesRetainedDerivationEvidence"/>. Both MarkKnown
    /// and Exclude route through the same shared <c>PreparationService.CompleteWithoutLearningAsync</c>
    /// cleanup, so this test is characterization/regression-hardening evidence protecting that shared path
    /// against future divergence between the two callers, not a new production behavior.
    /// </summary>
    [TestMethod]
    public async Task CompletingPreparationWithoutLearning_ExcludeRemovesRetainedDerivationEvidence()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();

        var evidenceBefore = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.IsGreaterThan(0, evidenceBefore, "Retained derivation evidence must exist before Exclude.");
        var candidateCountBefore = await _database.ReadAsync(conn =>
            conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == maschineWordId).CountAsync());
        Assert.IsGreaterThan(0, candidateCountBefore, "The owning ReviewCandidate must be retained before Exclude.");

        await _preparation.StartAsync(PreparationMethod.Manual, 20);
        var item = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        Assert.AreEqual(maschineWordId, item!.WordId);

        await _preparation.ExcludeAsync(item.CandidateId);

        var evidenceAfter = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(0, evidenceAfter, "Exclude must remove retained derivation evidence once the word leaves the Unknown lifecycle.");
        var candidateCountAfter = await _database.ReadAsync(conn =>
            conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == maschineWordId).CountAsync());
        Assert.AreEqual(0, candidateCountAfter, "Exclude must remove the retained owning ReviewCandidate.");

        var word = await _database.ReadAsync(conn => conn.Table<WordEntity>().Where(w => w.Id == maschineWordId).FirstAsync());
        Assert.AreEqual(WordStatus.Ignored, word.Status);
    }

    /// <summary>
    /// Package 5B: end-to-end characterization proof that an accepted derived word enters the normal
    /// Preparation-to-Learning pipeline exactly like any other prepared word. No code anywhere downstream of
    /// Preparation Accept branches on <see cref="CandidateProvenanceKind"/> (verified by inspection), so this
    /// is expected to pass immediately without any production change; it exists to protect that guarantee
    /// against future regression.
    /// </summary>
    [TestMethod]
    public async Task DerivedUnknownAccept_EntersNormalLearningPathWithoutDuplicateIdentity()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();

        await _preparation.StartAsync(PreparationMethod.Manual, 20);
        var item = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(item);
        Assert.AreEqual(maschineWordId, item!.WordId);

        await _preparation.AcceptAsync(item.CandidateId, InputFrom(item, 0), CardDirectionPreference.TermToMeaning);

        var cardsAfterAccept = await _database.ReadAsync(conn =>
            conn.Table<LearningCardEntity>().Where(c => c.WordId == maschineWordId).ToListAsync());
        Assert.HasCount(
            1,
            cardsAfterAccept,
            "Accepting the derived term must create exactly one normal learning card, with no duplicate/synthetic identity created because of provenance.");
        var cardId = cardsAfterAccept[0].Id;

        var loadResult = await _learning.GetOrStartAsync();
        Assert.IsNotNull(loadResult.Card);
        Assert.AreEqual(
            maschineWordId,
            loadResult.Card!.WordId,
            "The derived word must enter the normal learning queue exactly like any other prepared word.");

        await _learning.RevealAnswerAsync(loadResult.Card.QueueItemId);
        await _learning.RateAsync(loadResult.Card.QueueItemId, ReviewRating.Good);

        var reviewsAfterRating = await _database.ReadAsync(conn =>
            conn.Table<LearningReviewEntity>().Where(r => r.CardId == cardId).ToListAsync());
        Assert.HasCount(
            1,
            reviewsAfterRating,
            "Rating the derived word's card must produce exactly one LearningReview row.");

        var finalCardCount = await _database.ReadAsync(conn =>
            conn.Table<LearningCardEntity>().Where(c => c.WordId == maschineWordId).CountAsync());
        Assert.AreEqual(1, finalCardCount, "Rating must not create a second card for the same derived word.");
    }

    /// <summary>
    /// Package 5A-2 supersedes the temporary Package 5A provenance-loss guard: the guard existed only
    /// because the archive had no way to transport <see cref="DerivedTermEvidenceEntity"/> rows alongside
    /// the retained candidate, so exporting the candidate alone would have produced a provenance-less item
    /// on the target side. Now that evidence transport exists, the retained candidate must be exported
    /// faithfully through the normal completed-session archive path, exactly like any other candidate; see
    /// <see cref="BackupCreationTests.PortableExport_NeverEmitsTwoReviewItemsSharingOneVocabularyIdInOneWorkflow"/>
    /// for the unrelated non-derived-candidate case this must not disturb.
    /// </summary>
    [TestMethod]
    public async Task PortableExport_RetainsCandidateItemForCompletedSession()
    {
        await ImportGermanReviewWithMaschineUnknownAsync();

        var evidenceCount = await _database.ReadAsync(conn => conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.IsGreaterThan(0, evidenceCount, "Precondition: retained derivation evidence must exist.");

        Schema8BackupSnapshot? snapshot = null;
        await _database.RunInTransactionAsync(conn =>
        {
            var captured = BackupSnapshotCapture.CaptureForExport(conn);
            Assert.IsInstanceOfType<CapturedSchema11SnapshotEnvelope>(captured);
            snapshot = ((CapturedSchema11SnapshotEnvelope)captured).Snapshot;
            return true;
        });
        Assert.IsNotNull(snapshot);
        Assert.IsGreaterThan(
            0,
            snapshot!.ReviewCandidates.Count,
            "Precondition: the raw snapshot capture must still see the retained candidate row internally.");

        var payload = BackupModelMapperV2.MapToExternal(snapshot);

        var completedWorkflow = payload.Workflows.VocabularyReviews
            .Single(workflow => workflow.Status == BackupReviewSessionStatus.Completed);
        Assert.IsNotEmpty(
            completedWorkflow.Items,
            "Ordinary portable export must faithfully export the retained candidate through the normal completed-session archive path.");
        AssertMaschineEvidenceIsExported(payload, completedWorkflow);
    }

    /// <summary>Full/internal backup capture must retain the candidate exactly like ordinary portable export.</summary>
    [TestMethod]
    public async Task FullBackup_RetainsCandidateItemForCompletedSession()
    {
        await ImportGermanReviewWithMaschineUnknownAsync();

        Schema8BackupSnapshot? snapshot = null;
        await _database.RunInTransactionAsync(conn =>
        {
            var captured = BackupSnapshotCapture.CaptureFullForBackup(conn);
            Assert.IsInstanceOfType<CapturedSchema11SnapshotEnvelope>(captured);
            snapshot = ((CapturedSchema11SnapshotEnvelope)captured).Snapshot;
            return true;
        });

        var payload = BackupModelMapperV2.MapToExternal(snapshot!);
        var completedWorkflow = payload.Workflows.VocabularyReviews
            .Single(workflow => workflow.Status == BackupReviewSessionStatus.Completed);
        Assert.IsNotEmpty(
            completedWorkflow.Items,
            "Full/internal backup capture must faithfully export the retained candidate, exactly like ordinary portable export.");
        AssertMaschineEvidenceIsExported(payload, completedWorkflow);
    }

    /// <summary>Asserts the transported evidence for the retained "maschine" candidate is present in
    /// <paramref name="payload"/> and points back at the real whole-compound source span.</summary>
    private static void AssertMaschineEvidenceIsExported(BackupPayloadV2 payload, BackupVocabularyReviewWorkflow completedWorkflow)
    {
        var maschineVocabularyId = payload.Vocabulary.Single(v => v.IdentityKey == "W:maschine").Id;
        var maschineItem = completedWorkflow.Items.Single(item => item.VocabularyId == maschineVocabularyId);
        var evidence = payload.DerivedTermEvidence.Where(e => e.ReviewItemId == maschineItem.Id).ToList();
        Assert.IsNotEmpty(evidence, "The retained candidate's derivation evidence must be transported in the portable archive.");
        var single = evidence.Single();
        Assert.AreEqual("Schreibmaschine", single.SourceSurfaceForm);
        Assert.AreEqual(0, single.SourceSentenceOrder);
        Assert.IsGreaterThanOrEqualTo(0, single.SourceStartPosition);
        Assert.IsGreaterThan(0, single.SourceLength);
        Assert.IsNotEmpty(single.ComponentForm);
        Assert.IsNotEmpty(single.SourceIdentity);
    }

    /// <summary>
    /// The merge-safety-copy capture path is the one Schema-11 snapshot-capture call site that currently
    /// never applies the Schema-11 derived-evidence enrichment at all (unlike ordinary export and full
    /// backup, which already call it). This proves the gap directly against the enrichment field the
    /// current production code already exposes, without depending on any not-yet-existing archive DTO.
    /// </summary>
    [TestMethod]
    public async Task MergeSafetyCopyCapture_EnrichesDerivedEvidenceOwningCandidateId()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();
        var candidate = await _database.ReadAsync(conn =>
            conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == maschineWordId).FirstAsync());

        Schema8BackupSnapshot? snapshot = null;
        await _database.RunInTransactionAsync(conn =>
        {
            var captured = BackupMergeSafetyCopySnapshotCapture.CaptureForMergeSafetyCopy(conn);
            if (captured is MergeSafetyCopySchema12Captured schema12)
            {
                snapshot = schema12.Snapshot;
            }
            else if (captured is MergeSafetyCopySchema11Captured schema11)
            {
                snapshot = schema11.Snapshot;
            }
            else
            {
                Assert.Fail($"Expected Schema 11 or 12 captured merge safety copy, got {captured.GetType().Name}");
            }
            return true;
        });

        Assert.IsNotNull(snapshot!.DerivedTermEvidenceOwningReviewCandidateIds,
            "Merge safety copy capture must apply the same Schema-11 derived-evidence enrichment as ordinary export and full backup.");
        Assert.IsTrue(
            snapshot.DerivedTermEvidenceOwningReviewCandidateIds!.Contains(candidate.Id),
            "The retained candidate's own row id must appear in the merge-safety-copy enrichment set.");

        var payload = BackupModelMapperV2.MapToExternal(snapshot);
        var completedWorkflow = payload.Workflows.VocabularyReviews
            .Single(workflow => workflow.Status == BackupReviewSessionStatus.Completed);
        AssertMaschineEvidenceIsExported(payload, completedWorkflow);
    }

    /// <summary>Proves the transported evidence survives a real source-generated V2 archive write/read
    /// round-trip (never a reflection-based path), through the exact production writer/reader pair.</summary>
    [TestMethod]
    public async Task PortableExport_DerivedEvidenceRoundTripsThroughSourceGeneratedArchive()
    {
        await ImportGermanReviewWithMaschineUnknownAsync();

        Schema8BackupSnapshot? snapshot = null;
        ValidatedSchema11Capability? capability = null;
        await _database.RunInTransactionAsync(conn =>
        {
            var captured = BackupSnapshotCapture.CaptureForExport(conn);
            Assert.IsInstanceOfType<CapturedSchema11SnapshotEnvelope>(captured);
            var envelope = (CapturedSchema11SnapshotEnvelope)captured;
            snapshot = envelope.Snapshot;
            capability = envelope.Capability;
            return true;
        });

        var payload = BackupModelMapperV2.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(
            payload, new Schema8BackupFixtureBuilders.FakePlatformInfo(), capability!, DateTime.UtcNow, stream, CancellationToken.None);
        stream.Position = 0;

        var validated = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        Assert.IsNotNull(validated.V2);
        var roundTripped = validated.V2!.Payload;

        Assert.AreEqual(payload.DerivedTermEvidence.Count, roundTripped.DerivedTermEvidence.Count);
        Assert.IsGreaterThan(0, roundTripped.DerivedTermEvidence.Count);

        var completedWorkflow = roundTripped.Workflows.VocabularyReviews
            .Single(workflow => workflow.Status == BackupReviewSessionStatus.Completed);
        AssertMaschineEvidenceIsExported(roundTripped, completedWorkflow);
    }

    /// <summary>
    /// Package 5A-2 empty-target restore: the transported evidence must be recreated in a freshly restored
    /// database, referencing exactly one real local <see cref="ReviewCandidateEntity"/> row — never a
    /// synthesized <see cref="WordOccurrenceEntity"/>, and never multiplying the candidate.
    /// </summary>
    [TestMethod]
    public async Task EmptyTargetRestore_RecreatesRetainedDerivedEvidence()
    {
        await ImportGermanReviewWithMaschineUnknownAsync();

        using var archiveStream = new MemoryStream();
        var sourceService = new BackupService(_database, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        await sourceService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        await using var target = new TemporarySchema8Database("knownfirst-german-derived-restore-target");
        await target.UpgradeToCurrentSchemaAsync();
        var targetService = new BackupService(target, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        archiveStream.Position = 0;
        var result = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);

        var evidenceRows = await target.ReadAsync(conn => conn.Table<DerivedTermEvidenceEntity>().ToListAsync());
        Assert.IsNotEmpty(evidenceRows, "Empty-target restore must recreate the transported derivation evidence.");
        var evidenceRow = evidenceRows.Single();
        Assert.AreEqual("Schreibmaschine", evidenceRow.SourceSurfaceForm);
        Assert.AreEqual(0, evidenceRow.SourceSentenceOrder);
        Assert.IsGreaterThan(0, evidenceRow.SourceLength);

        var owningCandidateCount = await target.ReadAsync(conn =>
            conn.Table<ReviewCandidateEntity>().Where(c => c.Id == evidenceRow.ReviewCandidateId).CountAsync());
        Assert.AreEqual(1, owningCandidateCount, "The restored evidence must reference exactly one real local ReviewCandidate row.");

        var restoredOccurrenceCount = await target.ReadAsync(conn =>
            conn.Table<WordOccurrenceEntity>().CountAsync());
        Assert.AreEqual(0, restoredOccurrenceCount, "Restore must never synthesize a WordOccurrence for transported evidence.");
    }

    /// <summary>
    /// Multiple transported evidence rows sharing one owning candidate archive reference must still resolve
    /// to exactly one local candidate after restore — never one candidate per evidence row.
    /// </summary>
    [TestMethod]
    public async Task EmptyTargetRestore_MultipleEvidenceRowsForOneCandidateProduceExactlyOneCandidate()
    {
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync();
        var candidate = await _database.ReadAsync(conn =>
            conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == maschineWordId).FirstAsync());
        await _database.RunInTransactionAsync(conn =>
        {
            var existing = conn.Table<DerivedTermEvidenceEntity>().Where(e => e.ReviewCandidateId == candidate.Id).First();
            conn.Insert(new DerivedTermEvidenceEntity
            {
                ReviewCandidateId = candidate.Id,
                SourceIdentity = existing.SourceIdentity,
                SourceSurfaceForm = existing.SourceSurfaceForm,
                SourceStartPosition = existing.SourceStartPosition,
                SourceLength = existing.SourceLength,
                SourceSentenceOrder = existing.SourceSentenceOrder,
                // Distinct ComponentForm so this remains a second, physically distinct row under the real
                // Schema-11 unique index rather than colliding with the original.
                ComponentForm = existing.ComponentForm + "-alt"
            });
            return true;
        });

        var evidenceCountBefore = await _database.ReadAsync(conn => conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(2, evidenceCountBefore, "Precondition: two evidence rows must now share one owning candidate.");

        using var archiveStream = new MemoryStream();
        var sourceService = new BackupService(_database, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        await sourceService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        await using var target = new TemporarySchema8Database("knownfirst-german-derived-restore-multi-evidence");
        await target.UpgradeToCurrentSchemaAsync();
        var targetService = new BackupService(target, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        archiveStream.Position = 0;
        var result = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);

        var restoredEvidenceCount = await target.ReadAsync(conn => conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(2, restoredEvidenceCount, "Both transported evidence rows must be restored.");

        var restoredWordCandidateCount = await target.ReadAsync(async conn =>
        {
            var word = await conn.Table<WordEntity>().Where(w => w.NormalizedTerm == "W:maschine").FirstAsync();
            return await conn.Table<ReviewCandidateEntity>().Where(c => c.WordId == word.Id).CountAsync();
        });
        Assert.AreEqual(1, restoredWordCandidateCount, "Two evidence rows sharing one owning candidate must produce exactly one restored candidate.");
    }

    /// <summary>Preparation against a freshly restored database must build its display context from the
    /// restored evidence, exactly as it does against the original database.</summary>
    [TestMethod]
    public async Task EmptyTargetRestore_RestoredEvidenceProvidesPreparationSourceCompoundContext()
    {
        await ImportGermanReviewWithMaschineUnknownAsync();

        using var archiveStream = new MemoryStream();
        var sourceService = new BackupService(_database, new Schema8BackupFixtureBuilders.FakePlatformInfo());
        await sourceService.CreatePortableArchiveAsync(archiveStream, CancellationToken.None);

        await using var target = new TemporarySchema8Database("knownfirst-german-derived-restore-preparation");
        await target.UpgradeToCurrentSchemaAsync();
        var targetService = new BackupService(target, new Schema8BackupFixtureBuilders.FakePlatformInfo());

        archiveStream.Position = 0;
        var result = await targetService.ImportPortableArchiveAsync(archiveStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);

        var targetPreparation = new PreparationService(
            target,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(target),
                new LexicalLookupProviderResolver([_provider])),
            _clock);
        try
        {
            await targetPreparation.StartAsync(PreparationMethod.Manual, 20);
            var item = await targetPreparation.LookupCurrentAsync();
            Assert.IsNotNull(item);
            Assert.IsNotEmpty(item!.Contexts, "Preparation against the restored database must build real context from the restored derived evidence.");
            var context = item.Contexts[0];
            Assert.AreEqual("Die Schreibmaschine steht hier.", context.Text);
            Assert.AreEqual(
                "Schreibmaschine",
                context.Text.Substring(context.TargetStart, context.TargetLength));
        }
        finally
        {
            await targetPreparation.CancelPrefetchAsync();
        }
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
        public LearningTimezoneMode LearningTimezoneMode => LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId => null;
        public int LearningDayCutoffMinutes => 0;

        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool value) => throw new NotSupportedException();
        public void SetLearningTimezoneMode(LearningTimezoneMode mode) => throw new NotSupportedException();
        public void SetExplicitLearningTimezoneId(string? timezoneId) => throw new NotSupportedException();
        public void SetLearningDayCutoffMinutes(int minutes) => throw new NotSupportedException();
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
