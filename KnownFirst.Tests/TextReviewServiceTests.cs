using KnownFirst.Core.Learning;
using KnownFirst.Core.Review;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using SQLite;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace KnownFirst.Tests;

[TestClass]
public sealed class TextReviewServiceTests
{
    private TemporaryDatabase _database = null!;
    private TextReviewService _service = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporaryDatabase();
        await _database.InitializeAsync();
        _service = new TextReviewService(
            _database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new ThrowingGermanLexicon());
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ActiveSchema8UsesPreferredMeaningId()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("diagnostic");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "diagnostic", translation: "Diagnose");
        var cardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        var database = new ExistingFixtureDatabase(fixture);
        var service = new TextReviewService(
            database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new ThrowingGermanLexicon());

        var diagnostics = await service.GetDiagnosticsAsync();

        var card = diagnostics.LearningCards.Single(item => item.Id == cardId);
        Assert.AreEqual(meaningId, card.MeaningId);
    }

    [TestMethod]
    public async Task ImportAsync_StoresOriginalTextAndExactCoordinates()
    {
        const string content = "  Die Häuser stehen hier.\r\nOAuth2 bleibt!  ";

        var result = await _service.ImportAsync(CreateRequest(content));
        var stored = await _database.ReadAsync(async connection =>
        {
            var document = await connection.FindAsync<DocumentEntity>(result.DocumentId);
            var sentences = await connection.Table<SentenceSpanEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            return (Document: document!, Sentences: sentences, Occurrences: occurrences);
        });

        Assert.AreEqual(content, stored.Document.Content);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        Assert.AreEqual(64, stored.Document.ContentFingerprint.Length);
        Assert.AreEqual("en", stored.Document.TextLanguage);
        Assert.AreEqual("de", stored.Document.ExplanationLanguage);
        Assert.AreEqual(LexicalLookupMode.Translation, stored.Document.LookupMode);
        Assert.AreEqual("de", stored.Document.TargetLanguage);
        foreach (var sentence in stored.Sentences)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(
                stored.Document.Content.Substring(sentence.StartPosition, sentence.Length)));
        }

        foreach (var occurrence in stored.Occurrences)
        {
            Assert.AreEqual(
                occurrence.SurfaceForm,
                stored.Document.Content.Substring(occurrence.StartPosition, occurrence.Length));
        }
    }

    // ----- German Enhanced Term Recognition: Package 2 TextReviewService wiring -----

    [TestMethod]
    public async Task EnhancedRecognitionDisabled_DoesNotUseGermanLexicon()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: false);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new ThrowingGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var words = await _database.ReadAsync(connection => connection.Table<WordEntity>().ToListAsync());
        Assert.IsFalse(words.Any(word => word.NormalizedTerm == "W:schreiben"));
        Assert.IsFalse(words.Any(word => word.NormalizedTerm == "W:maschine"));
    }

    [TestMethod]
    public async Task EnhancedRecognitionEnabled_GermanImport_UsesGermanCompoundRecognition()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var words = await _database.ReadAsync(connection => connection.Table<WordEntity>().ToListAsync());
        var normalizedTerms = words.Select(word => word.NormalizedTerm).ToArray();

        CollectionAssert.Contains(normalizedTerms, "W:schreibmaschine");
        CollectionAssert.Contains(normalizedTerms, "W:schreiben");
        CollectionAssert.Contains(normalizedTerms, "W:maschine");

        var schreibmaschineWord = words.Single(word => word.NormalizedTerm == "W:schreibmaschine");
        var schreibenWord = words.Single(word => word.NormalizedTerm == "W:schreiben");
        var maschineWord = words.Single(word => word.NormalizedTerm == "W:maschine");

        Assert.IsGreaterThanOrEqualTo(1, schreibmaschineWord.TotalOccurrenceCount);
        Assert.AreEqual(0, schreibenWord.TotalOccurrenceCount);
        Assert.AreEqual(0, maschineWord.TotalOccurrenceCount);

        var candidateOrders = await _database.ReadAsync(connection =>
            connection.Table<ReviewCandidateEntity>().ToListAsync());
        Assert.IsTrue(candidateOrders.Any(candidate => candidate.WordId == schreibmaschineWord.Id));
        Assert.IsTrue(candidateOrders.Any(candidate => candidate.WordId == schreibenWord.Id));
        Assert.IsTrue(candidateOrders.Any(candidate => candidate.WordId == maschineWord.Id));
    }

    [TestMethod]
    public async Task EnhancedRecognitionEnabled_NonGermanImport_DoesNotUseGermanLexicon()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new ThrowingGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("English import", "The writing machine works well.", "en", "en"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
    }

    // ----- German Enhanced Term Recognition: Package 3 Persistence & Read Model -----

    [TestMethod]
    public async Task EnhancedRecognition_GermanImport_PersistsExactDerivationEvidence()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var (words, candidates, evidence) = await _database.ReadAsync(async conn =>
        {
            var w = await conn.Table<WordEntity>().ToListAsync();
            var c = await conn.Table<ReviewCandidateEntity>().ToListAsync();
            var e = await conn.Table<DerivedTermEvidenceEntity>().ToListAsync();
            return (w, c, e);
        });

        var schreibmaschineWord = words.Single(w => w.NormalizedTerm == "W:schreibmaschine");
        var schreibenWord = words.Single(w => w.NormalizedTerm == "W:schreiben");
        var maschineWord = words.Single(w => w.NormalizedTerm == "W:maschine");

        var schreibmaschineCandidate = candidates.Single(c => c.WordId == schreibmaschineWord.Id);
        var schreibenCandidate = candidates.Single(c => c.WordId == schreibenWord.Id);
        var maschineCandidate = candidates.Single(c => c.WordId == maschineWord.Id);

        var directEvidence = evidence.Where(e => e.ReviewCandidateId == schreibmaschineCandidate.Id).ToList();
        Assert.IsEmpty(directEvidence, "Direct candidates must persist zero evidence rows.");

        var schreibenEvidence = evidence.Where(e => e.ReviewCandidateId == schreibenCandidate.Id).ToList();
        Assert.AreEqual(1, schreibenEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", schreibenEvidence[0].SourceIdentity);
        Assert.AreEqual("Schreibmaschine", schreibenEvidence[0].SourceSurfaceForm);
        Assert.AreEqual(4, schreibenEvidence[0].SourceStartPosition);
        Assert.AreEqual(15, schreibenEvidence[0].SourceLength);
        Assert.AreEqual(0, schreibenEvidence[0].SourceSentenceOrder);
        Assert.AreEqual("Schreib", schreibenEvidence[0].ComponentForm);

        var maschineEvidence = evidence.Where(e => e.ReviewCandidateId == maschineCandidate.Id).ToList();
        Assert.AreEqual(1, maschineEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", maschineEvidence[0].SourceIdentity);
        Assert.AreEqual("Schreibmaschine", maschineEvidence[0].SourceSurfaceForm);
        Assert.AreEqual(4, maschineEvidence[0].SourceStartPosition);
        Assert.AreEqual(15, maschineEvidence[0].SourceLength);
        Assert.AreEqual(0, maschineEvidence[0].SourceSentenceOrder);
        Assert.AreEqual("maschine", maschineEvidence[0].ComponentForm);
    }

    [TestMethod]
    public async Task EnhancedRecognition_KnownSourceCompound_PersistsEvidenceWithoutSourceOccurrence()
    {
        var now = DateTime.UtcNow;
        var knownWord = new WordEntity
        {
            Language = "de",
            CanonicalTerm = "Schreibmaschine",
            NormalizedTerm = "W:schreibmaschine",
            TokenKind = TokenKind.Word,
            Status = WordStatus.Known,
            TotalOccurrenceCount = 5,
            DocumentCount = 2,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _database.RunInTransactionAsync(conn => conn.Insert(knownWord));

        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var (words, candidates, occurrences, evidence) = await _database.ReadAsync(async conn =>
        {
            var w = await conn.Table<WordEntity>().ToListAsync();
            var c = await conn.Table<ReviewCandidateEntity>().ToListAsync();
            var o = await conn.Table<WordOccurrenceEntity>().Where(item => item.DocumentId == result.DocumentId).ToListAsync();
            var e = await conn.Table<DerivedTermEvidenceEntity>().ToListAsync();
            return (w, c, o, e);
        });

        var sourceWord = words.Single(w => w.NormalizedTerm == "W:schreibmaschine");
        Assert.AreEqual(WordStatus.Known, sourceWord.Status);
        Assert.AreEqual(0, occurrences.Count(o => o.WordId == sourceWord.Id), "Known source compound must have no new occurrences in this document.");

        var schreibenWord = words.Single(w => w.NormalizedTerm == "W:schreiben");
        var schreibenCandidate = candidates.Single(c => c.WordId == schreibenWord.Id);
        var schreibenEvidence = evidence.Where(e => e.ReviewCandidateId == schreibenCandidate.Id).ToList();

        Assert.AreEqual(1, schreibenEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", schreibenEvidence[0].SourceIdentity);
        Assert.AreEqual("Schreibmaschine", schreibenEvidence[0].SourceSurfaceForm);
        Assert.AreEqual(4, schreibenEvidence[0].SourceStartPosition);
        Assert.AreEqual(15, schreibenEvidence[0].SourceLength);
    }

    [TestMethod]
    public async Task EnhancedRecognition_IgnoredSourceCompound_PersistsEvidenceWithoutSourceOccurrence()
    {
        var now = DateTime.UtcNow;
        var ignoredWord = new WordEntity
        {
            Language = "de",
            CanonicalTerm = "Schreibmaschine",
            NormalizedTerm = "W:schreibmaschine",
            TokenKind = TokenKind.Word,
            Status = WordStatus.Ignored,
            TotalOccurrenceCount = 0,
            DocumentCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _database.RunInTransactionAsync(conn => conn.Insert(ignoredWord));

        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var (candidates, occurrences, evidence) = await _database.ReadAsync(async conn =>
        {
            var c = await conn.Table<ReviewCandidateEntity>().ToListAsync();
            var o = await conn.Table<WordOccurrenceEntity>().Where(item => item.DocumentId == result.DocumentId).ToListAsync();
            var e = await conn.Table<DerivedTermEvidenceEntity>().ToListAsync();
            return (c, o, e);
        });

        Assert.AreEqual(0, occurrences.Count(o => o.WordId == ignoredWord.Id));
        Assert.IsNotEmpty(evidence);
    }

    [TestMethod]
    public async Task EnhancedRecognition_UnknownBacklogSourceCompound_PersistsEvidence()
    {
        var now = DateTime.UtcNow;
        var unknownWord = new WordEntity
        {
            Language = "de",
            CanonicalTerm = "Schreibmaschine",
            NormalizedTerm = "W:schreibmaschine",
            TokenKind = TokenKind.Word,
            Status = WordStatus.UnknownBacklog,
            TotalOccurrenceCount = 1,
            DocumentCount = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _database.RunInTransactionAsync(conn => conn.Insert(unknownWord));

        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var (words, candidates, occurrences, evidence) = await _database.ReadAsync(async conn =>
        {
            var w = await conn.Table<WordEntity>().ToListAsync();
            var c = await conn.Table<ReviewCandidateEntity>().ToListAsync();
            var o = await conn.Table<WordOccurrenceEntity>().Where(item => item.DocumentId == result.DocumentId).ToListAsync();
            var e = await conn.Table<DerivedTermEvidenceEntity>().ToListAsync();
            return (w, c, o, e);
        });

        Assert.AreEqual(1, occurrences.Count(o => o.WordId == unknownWord.Id));
        Assert.IsFalse(candidates.Any(c => c.WordId == unknownWord.Id));

        var schreibenWord = words.Single(w => w.NormalizedTerm == "W:schreiben");
        var schreibenCandidate = candidates.Single(c => c.WordId == schreibenWord.Id);
        var schreibenEvidence = evidence.Where(e => e.ReviewCandidateId == schreibenCandidate.Id).ToList();
        Assert.AreEqual(1, schreibenEvidence.Count);
    }

    [TestMethod]
    public async Task EnhancedRecognition_RepeatedSourceOccurrences_PersistDistinctEvidence()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier. Eine zweite Schreibmaschine ist da.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var (words, candidates, evidence) = await _database.ReadAsync(async conn =>
        {
            var w = await conn.Table<WordEntity>().ToListAsync();
            var c = await conn.Table<ReviewCandidateEntity>().ToListAsync();
            var e = await conn.Table<DerivedTermEvidenceEntity>().ToListAsync();
            return (w, c, e);
        });

        var schreibenWord = words.Single(w => w.NormalizedTerm == "W:schreiben");
        var schreibenCandidate = candidates.Single(c => c.WordId == schreibenWord.Id);
        var schreibenEvidence = evidence
            .Where(e => e.ReviewCandidateId == schreibenCandidate.Id)
            .OrderBy(e => e.SourceStartPosition)
            .ToList();

        Assert.AreEqual(2, schreibenEvidence.Count);
        Assert.AreEqual(4, schreibenEvidence[0].SourceStartPosition);
        Assert.AreEqual(44, schreibenEvidence[1].SourceStartPosition);
    }

    [TestMethod]
    public async Task EnhancedRecognition_MultipleSourceCompoundsForSameDerivedIdentity_RemainDistinct()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine und die Waschmaschine stehen hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var (words, candidates, evidence) = await _database.ReadAsync(async conn =>
        {
            var w = await conn.Table<WordEntity>().ToListAsync();
            var c = await conn.Table<ReviewCandidateEntity>().ToListAsync();
            var e = await conn.Table<DerivedTermEvidenceEntity>().ToListAsync();
            return (w, c, e);
        });

        var maschineWord = words.Single(w => w.NormalizedTerm == "W:maschine");
        var maschineCandidate = candidates.Single(c => c.WordId == maschineWord.Id);
        var maschineEvidence = evidence
            .Where(e => e.ReviewCandidateId == maschineCandidate.Id)
            .OrderBy(e => e.SourceStartPosition)
            .ToList();

        Assert.AreEqual(2, maschineEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", maschineEvidence[0].SourceIdentity);
        Assert.AreEqual("W:waschmaschine", maschineEvidence[1].SourceIdentity);
    }

    [TestMethod]
    public async Task DirectCandidate_PersistsNoDerivationEvidence()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Hier steht ein Baum.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var evidenceCount = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(0, evidenceCount);
    }

    [TestMethod]
    public async Task DerivedCandidate_HasNoFabricatedWordOccurrence()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var (words, occurrences) = await _database.ReadAsync(async conn =>
        {
            var w = await conn.Table<WordEntity>().ToListAsync();
            var o = await conn.Table<WordOccurrenceEntity>().Where(item => item.DocumentId == result.DocumentId).ToListAsync();
            return (w, o);
        });

        var schreibenWord = words.Single(w => w.NormalizedTerm == "W:schreiben");
        var maschineWord = words.Single(w => w.NormalizedTerm == "W:maschine");

        Assert.AreEqual(0, occurrences.Count(o => o.WordId == schreibenWord.Id));
        Assert.AreEqual(0, occurrences.Count(o => o.WordId == maschineWord.Id));
    }

    [TestMethod]
    public async Task Undo_PreservesDerivationEvidence()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var candidate1 = await service.GetCurrentCandidateAsync();
        Assert.IsNotNull(candidate1);
        await service.DecideAsync(candidate1.WordId, WordStatus.Known);

        var candidate2 = await service.GetCurrentCandidateAsync();
        Assert.IsNotNull(candidate2);
        await service.DecideAsync(candidate2.WordId, WordStatus.Known);

        var undone = await service.UndoPreviousDecisionAsync();
        Assert.IsTrue(undone);

        var evidenceCount = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.IsTrue(evidenceCount > 0, "Undo must preserve all persisted derivation evidence rows.");
    }

    [TestMethod]
    public async Task DiscardActiveImport_RemovesDerivationEvidence()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var evidenceBefore = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.IsTrue(evidenceBefore > 0);

        await service.DiscardActiveImportAsync();

        var evidenceAfter = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(0, evidenceAfter, "DiscardActiveImport must remove all derivation evidence rows.");
    }

    [TestMethod]
    public async Task CompletingReview_RemovesDerivationEvidence()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Die Schreibmaschine steht hier.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        while (true)
        {
            var candidate = await service.GetCurrentCandidateAsync();
            if (candidate is null)
            {
                break;
            }

            var decision = await service.DecideAsync(candidate.WordId, WordStatus.Known);
            if (decision.IsComplete)
            {
                break;
            }
        }

        var evidenceAfter = await _database.ReadAsync(conn =>
            conn.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(0, evidenceAfter, "Completing review must remove all derivation evidence rows.");
    }

    [TestMethod]
    public async Task GetCurrentCandidateAsync_ReturnsDerivedEvidenceAndProvenance()
    {
        var settings = new FixedEnhancedRecognitionSettings(enabled: true);
        var service = new TextReviewService(
            _database, new TextAnalyzer(), settings, new FixtureGermanLexicon());

        var result = await service.ImportAsync(
            new ImportTextRequest("German import", "Schreibmaschine.", "de", "de"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        // Candidate 0 is direct (Schreibmaschine)
        var first = await service.GetCurrentCandidateAsync();
        Assert.IsNotNull(first);
        Assert.AreEqual("W:schreibmaschine", first.Identity);
        Assert.AreEqual(CandidateProvenanceKind.Direct, first.Provenance);
        Assert.IsEmpty(first.DerivationEvidence);

        await service.DecideAsync(first.WordId, WordStatus.Known);

        // Candidate 1 is derived (maschine)
        var second = await service.GetCurrentCandidateAsync();
        Assert.IsNotNull(second);
        Assert.AreEqual("W:maschine", second.Identity);
        Assert.AreEqual(CandidateProvenanceKind.DerivedFromCompound, second.Provenance);
        Assert.AreEqual(0, second.OccurrenceCount);
        Assert.IsEmpty(second.Contexts);
        Assert.AreEqual(1, second.DerivationEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", second.DerivationEvidence[0].SourceIdentity);
        Assert.AreEqual("Schreibmaschine", second.DerivationEvidence[0].SourceSurfaceForm);
        Assert.AreEqual("maschine", second.DerivationEvidence[0].ComponentForm);

        await service.DecideAsync(second.WordId, WordStatus.Known);

        // Candidate 2 is derived (schreiben)
        var third = await service.GetCurrentCandidateAsync();
        Assert.IsNotNull(third);
        Assert.AreEqual("W:schreiben", third.Identity);
        Assert.AreEqual(CandidateProvenanceKind.DerivedFromCompound, third.Provenance);
        Assert.AreEqual(0, third.OccurrenceCount);
        Assert.IsEmpty(third.Contexts);
        Assert.AreEqual(1, third.DerivationEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", third.DerivationEvidence[0].SourceIdentity);
        Assert.AreEqual("Schreibmaschine", third.DerivationEvidence[0].SourceSurfaceForm);
        Assert.AreEqual("Schreib", third.DerivationEvidence[0].ComponentForm);
    }

    // ----- Package 3 correction: current-schema fail-closed regression coverage. A database that has
    // completed DatabaseSchema.InitializeAsync at CurrentVersion 11 is guaranteed to have a valid
    // DerivedTermEvidenceEntries table; if that table is later missing (corruption, external tampering),
    // TextReviewService must fail closed rather than silently degrading to Direct/skipped cleanup. -----

    private Task DropDerivedTermEvidenceEntriesTableAsync() =>
        _database.RunInTransactionAsync(connection =>
        {
            connection.Execute("DROP TABLE DerivedTermEvidenceEntries");
            return true;
        });

    [TestMethod]
    public async Task GetCurrentCandidateAsync_MissingSchema11EvidenceTable_FailsInsteadOfReturningDirect()
    {
        var result = await _service.ImportAsync(CreateRequest("Bank matters. The bank is open."));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        await DropDerivedTermEvidenceEntriesTableAsync();

        await Assert.ThrowsExactlyAsync<SQLiteException>(() => _service.GetCurrentCandidateAsync());
    }

    [TestMethod]
    public async Task DiscardActiveImport_MissingSchema11EvidenceTable_FailsInsteadOfSkippingCleanup()
    {
        var result = await _service.ImportAsync(CreateRequest("Bank matters. The bank is open."));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        await DropDerivedTermEvidenceEntriesTableAsync();

        await Assert.ThrowsExactlyAsync<SQLiteException>(() => _service.DiscardActiveImportAsync());
    }

    [TestMethod]
    public async Task CompleteSession_MissingSchema11EvidenceTable_FailsInsteadOfSkippingCleanup()
    {
        var result = await _service.ImportAsync(CreateRequest("Bank matters. The bank is open."));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        // Decide every candidate but the last one, so the session is exactly one decision away from
        // completing. GetCurrentCandidateAsync must still see the honest evidence table at this point.
        ReviewCandidateDetails? finalCandidate;
        while (true)
        {
            var candidate = await _service.GetCurrentCandidateAsync();
            Assert.IsNotNull(candidate);
            if (candidate.ReviewedCount == candidate.TotalCandidates - 1)
            {
                finalCandidate = candidate;
                break;
            }

            await _service.DecideAsync(candidate.WordId, WordStatus.Known);
        }

        await DropDerivedTermEvidenceEntriesTableAsync();

        // The completing decision drives CompleteSession, which must fail closed on the missing table
        // rather than silently skipping the DerivedTermEvidenceEntries cleanup statement.
        await Assert.ThrowsExactlyAsync<SQLiteException>(
            () => _service.DecideAsync(finalCandidate!.WordId, WordStatus.Known));
    }

    private sealed class FixedEnhancedRecognitionSettings(bool enabled) : IAppSettingsService
    {
        public int PreparationLimit => 20;
        public IReadOnlyList<int> SupportedPreparationLimits => [20];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => LearningMode.Automatic;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => enabled;

        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool value) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }

    private sealed class ThrowingGermanLexicon : IGermanLexicon
    {
        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            throw new InvalidOperationException(
                "The German lexicon must not be queried when enhanced recognition is inactive for this analysis.");

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry) =>
            throw new InvalidOperationException(
                "The German lexicon must not be queried when enhanced recognition is inactive for this analysis.");
    }

    [TestMethod]
    public async Task ImportAsync_StoresExplicitDefinitionRequestWithoutTargetLanguage()
    {
        var result = await _service.ImportAsync(new ImportTextRequest(
            "Definition request",
            "Network security matters.",
            "en",
            LexicalLookupMode.Definition,
            null));

        var document = await _database.ReadAsync(connection =>
            connection.FindAsync<DocumentEntity>(result.DocumentId));
        Assert.AreEqual(LexicalLookupMode.Definition, document!.LookupMode);
        Assert.AreEqual(string.Empty, document.TargetLanguage);
        Assert.AreEqual("en", document.ExplanationLanguage);
    }

    [TestMethod]
    public async Task ImportAsync_RejectsIdenticalTranslationLanguages()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(new ImportTextRequest(
            "Invalid request",
            "Network security matters.",
            "en",
            LexicalLookupMode.Translation,
            "en")));
    }

    // --- KF-LANG-001: Russian translation-target regression (Beta 12 hotfix) -----------

    [TestMethod]
    public async Task ImportAsync_GermanToRussianTranslationIsAccepted()
    {
        var result = await _service.ImportAsync(new ImportTextRequest(
            "German to Russian",
            "Die Sicherheit ist wichtig.",
            "de",
            LexicalLookupMode.Translation,
            "ru"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
    }

    [TestMethod]
    public async Task ImportAsync_EnglishToRussianTranslationIsAccepted()
    {
        var result = await _service.ImportAsync(new ImportTextRequest(
            "English to Russian",
            "Network security matters.",
            "en",
            LexicalLookupMode.Translation,
            "ru"));

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
    }

    [TestMethod]
    public async Task ImportAsync_RussianSourceTextRemainsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(new ImportTextRequest(
            "Russian source",
            "Безопасность важна.",
            "ru",
            LexicalLookupMode.Translation,
            "en")));
    }

    [TestMethod]
    public async Task ImportAsync_GermanToGermanTranslationRemainsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(new ImportTextRequest(
            "Invalid request",
            "Die Sicherheit ist wichtig.",
            "de",
            LexicalLookupMode.Translation,
            "de")));
    }

    // English-to-English translation rejection is already covered by
    // ImportAsync_RejectsIdenticalTranslationLanguages, and Definition mode with no
    // target language is already covered by
    // ImportAsync_StoresExplicitDefinitionRequestWithoutTargetLanguage, above.

    [TestMethod]
    public async Task ImportAsync_CombinedLookupModeIsRejectedForNewImports()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(new ImportTextRequest(
            "Legacy combined request",
            "Network security matters.",
            "en",
            LexicalLookupMode.DefinitionAndTranslation,
            "de")));
    }

    [TestMethod]
    public void ImportTextRequest_CompatibilityConstructorCreatesTranslationForDifferingLanguages()
    {
        var request = new ImportTextRequest(
            "Legacy compatibility",
            "Network security matters.",
            "en",
            "ru");

        Assert.AreEqual(LexicalLookupMode.Translation, request.LookupMode);
        Assert.AreEqual("ru", request.TargetLanguage);
    }

    [TestMethod]
    public async Task ImportAsync_SameTextInDifferentSourceLanguagesCreatesDistinctVocabulary()
    {
        const string content = "Gift die Kind Note.";
        var english = await _service.ImportAsync(new ImportTextRequest(
            "English meanings",
            content,
            "en",
            LexicalLookupMode.Translation,
            "de"));
        await CompleteReviewAsync(_ => WordStatus.UnknownBacklog);

        var german = await _service.ImportAsync(new ImportTextRequest(
            "German meanings",
            content,
            "de",
            LexicalLookupMode.Translation,
            "en"));

        var stored = await _database.ReadAsync(async connection => new
        {
            Documents = await connection.Table<DocumentEntity>().OrderBy(item => item.Id).ToListAsync(),
            Words = await connection.Table<WordEntity>().OrderBy(item => item.Id).ToListAsync()
        });

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, english.Outcome);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, german.Outcome);
        Assert.HasCount(2, stored.Documents);
        Assert.IsTrue(stored.Documents.Any(document => document.TextLanguage == "en"));
        Assert.IsTrue(stored.Documents.Any(document => document.TextLanguage == "de"));
        foreach (var normalizedTerm in new[] { "W:gift", "W:die", "W:kind", "W:note" })
        {
            var matches = stored.Words.Where(word => word.NormalizedTerm == normalizedTerm).ToArray();
            Assert.HasCount(2, matches);
            CollectionAssert.AreEquivalent(
                new[] { "en", "de" },
                matches.Select(word => word.Language).ToArray());
        }
    }

    [TestMethod]
    public async Task ImportAsync_RequiredCorpusStoresUniqueCandidatesAndEveryOccurrence()
    {
        const string content = "IT protects smart systems. It protects smart networks. Smart systems use OAuth2.";

        var result = await _service.ImportAsync(CreateRequest(content));
        var stored = await _database.ReadAsync(async connection =>
        {
            var spans = await connection.Table<SentenceSpanEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            var queue = await connection.Table<ReviewCandidateEntity>()
                .Where(item => item.SessionId == result.SessionId)
                .ToListAsync();
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            return (spans, queue, occurrences);
        });

        Assert.AreEqual(8, result.CandidateCount);
        Assert.HasCount(3, stored.spans);
        Assert.HasCount(8, stored.queue);
        Assert.AreEqual(8, stored.queue.Select(item => item.WordId).Distinct().Count());
        Assert.HasCount(12, stored.occurrences);
        Assert.IsTrue(stored.occurrences.Any(item =>
            item.SurfaceForm == "IT" && item.StartPosition == 0 && item.Length == 2));
        Assert.IsTrue(stored.occurrences.Any(item =>
            item.SurfaceForm == "smart" && item.StartPosition == 12 && item.Length == 5));
        Assert.IsTrue(stored.occurrences.Any(item =>
            item.SurfaceForm == "OAuth2" && item.StartPosition == 73 && item.Length == 6));
    }

    [TestMethod]
    public async Task ImportAsync_EnglishPronounVariantsPersistAsOneVocabularyRecord()
    {
        var result = await _service.ImportAsync(new ImportTextRequest(
            "Pronoun variants",
            "I me my.",
            "en",
            LexicalLookupMode.Definition,
            null));

        var stored = await _database.ReadAsync(async connection =>
        {
            var words = await connection.Table<WordEntity>()
                .Where(item => item.Language == "en" && item.NormalizedTerm == "W:i")
                .ToListAsync();
            var word = words.Single();
            var forms = await connection.Table<WordFormEntity>()
                .Where(item => item.WordId == word.Id)
                .OrderBy(item => item.Id)
                .ToListAsync();
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == word.Id)
                .OrderBy(item => item.Order)
                .ToListAsync();
            return (word, forms, occurrences);
        });

        Assert.AreEqual(1, result.CandidateCount);
        Assert.AreEqual("I", stored.word.CanonicalTerm);
        Assert.AreEqual(3, stored.word.TotalOccurrenceCount);
        CollectionAssert.AreEqual(
            new[] { "I", "me", "my" },
            stored.forms.Select(form => form.SurfaceForm).ToArray());
        CollectionAssert.AreEqual(
            new[] { "I", "me", "my" },
            stored.occurrences.Select(occurrence => occurrence.SurfaceForm).ToArray());
    }

    [TestMethod]
    public async Task DecideAsync_PersistsEveryDecisionAndResumeFindsFirstUnresolvedCandidate()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta Gamma."));
        var first = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(first!.WordId, WordStatus.Known);

        var recreatedService = new TextReviewService(
            _database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new ThrowingGermanLexicon());
        var resumed = await recreatedService.GetCurrentCandidateAsync();
        var persisted = await _database.ReadAsync(connection =>
            connection.FindAsync<WordEntity>(first.WordId));
        var active = await recreatedService.GetActiveReviewAsync();

        Assert.AreEqual(WordStatus.Known, persisted!.Status);
        Assert.AreNotEqual(first.WordId, resumed!.WordId);
        Assert.AreEqual(1, active!.ReviewedCount);
        Assert.AreEqual(3, active.TotalCandidates);
    }

    [TestMethod]
    public async Task GetCurrentCandidateAsync_ReturnsAtMostThreeDistinctOriginalContexts()
    {
        const string content = "Target first. Target second. Target third. Target fourth.";
        await _service.ImportAsync(CreateRequest(content));

        var candidate = await _service.GetCurrentCandidateAsync()
            ?? throw new InvalidOperationException("The expected review candidate was not created.");
        var storedRows = await _database.ReadAsync(async connection =>
        {
            var queueRows = await connection.Table<ReviewCandidateEntity>()
                .Where(item => item.WordId == candidate.WordId)
                .CountAsync();
            var occurrenceRows = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == candidate.WordId)
                .CountAsync();
            return (queueRows, occurrenceRows);
        });

        Assert.AreEqual("Target", candidate.Candidate);
        Assert.AreEqual(4, candidate.OccurrenceCount);
        Assert.AreEqual(1, storedRows.queueRows);
        Assert.AreEqual(4, storedRows.occurrenceRows);
        Assert.HasCount(3, candidate.Contexts);
        CollectionAssert.AreEqual(
            new[] { "Target first.", "Target second.", "Target third." },
            candidate.Contexts
                .Select(context => context.BeforeTarget + context.Target + context.AfterTarget)
                .ToArray());
        foreach (var context in candidate.Contexts)
        {
            Assert.AreEqual(
                context.Target,
                content.Substring(context.StartPosition, context.Length));
        }
    }

    [TestMethod]
    public async Task GetCurrentCandidateAsync_CitationSeparatedContextsEachEqualOneSentence()
    {
        const string content = "Information supports risk management.[1] Information protects confidentiality.[2]";
        var import = await _service.ImportAsync(CreateRequest(content));

        var candidate = await _service.GetCurrentCandidateAsync()
            ?? throw new InvalidOperationException("The expected review candidate was not created.");
        var storedSentences = await _database.ReadAsync(connection => connection.Table<SentenceSpanEntity>()
            .Where(sentence => sentence.DocumentId == import.DocumentId)
            .OrderBy(sentence => sentence.Order)
            .ToListAsync());

        Assert.AreEqual("Information", candidate.Candidate);
        Assert.HasCount(2, storedSentences);
        Assert.HasCount(2, candidate.Contexts);
        CollectionAssert.AreEqual(
            new[]
            {
                "Information supports risk management.[1]",
                "Information protects confidentiality.[2]"
            },
            candidate.Contexts
                .Select(context => context.BeforeTarget + context.Target + context.AfterTarget)
                .ToArray());
        foreach (var context in candidate.Contexts)
        {
            Assert.AreEqual(context.Target, content.Substring(context.StartPosition, context.Length));
        }
    }

    [TestMethod]
    public async Task GetCurrentCandidateAsync_DeduplicatesCaseOnlyEncounteredFormsForDisplay()
    {
        await _service.ImportAsync(CreateRequest("Information information INFORMATION."));

        var candidate = await _service.GetCurrentCandidateAsync()
            ?? throw new InvalidOperationException("The expected review candidate was not created.");

        Assert.AreEqual(3, candidate.OccurrenceCount);
        CollectionAssert.AreEqual(new[] { "information" }, candidate.SurfaceForms.ToArray());
    }

    [TestMethod]
    public async Task GetCurrentCandidateAsync_DuplicateContextsDoNotReduceOccurrenceCount()
    {
        const string content = "Security is important. Security is important. Security protects information.";
        await _service.ImportAsync(CreateRequest(content));

        var candidate = await _service.GetCurrentCandidateAsync()
            ?? throw new InvalidOperationException("The expected review candidate was not created.");
        var occurrenceRows = await _database.ReadAsync(connection => connection.Table<WordOccurrenceEntity>()
            .Where(occurrence => occurrence.WordId == candidate.WordId)
            .CountAsync());

        Assert.AreEqual(3, candidate.OccurrenceCount);
        Assert.AreEqual(3, occurrenceRows);
        Assert.HasCount(2, candidate.Contexts);
        CollectionAssert.AreEqual(
            new[] { "Security is important.", "Security protects information." },
            candidate.Contexts
                .Select(context => context.BeforeTarget + context.Target + context.AfterTarget)
                .ToArray());
    }

    [TestMethod]
    public async Task GetCurrentCandidateAsync_ContextDedupNormalizesLineEndingsAndWhitespaceButRetainsFirstExactSentence()
    {
        const string content = "Target  line\twraps. Target line wraps.";
        await _service.ImportAsync(CreateRequest(content));

        var candidate = await _service.GetCurrentCandidateAsync()
            ?? throw new InvalidOperationException("The expected review candidate was not created.");

        Assert.AreEqual(2, candidate.OccurrenceCount);
        Assert.HasCount(1, candidate.Contexts);
        Assert.AreEqual(
            "Target  line\twraps.",
            candidate.Contexts[0].BeforeTarget
            + candidate.Contexts[0].Target
            + candidate.Contexts[0].AfterTarget);
    }

#if DEBUG
    [TestMethod]
    public async Task GetAnalysisReportAsync_ProducesReadableBoundaryGroupingAndContextReasons()
    {
        const string content = "Information information here.[1] Information information here.[1]";
        var import = await _service.ImportAsync(CreateRequest(content));

        var report = await _service.GetAnalysisReportAsync(import.DocumentId)
            ?? throw new InvalidOperationException("The expected DEBUG analysis report was not created.");
        var text = WordAnalysisReportFormatter.Format(report);

        Assert.AreEqual(import.DocumentId, report.Summary.DocumentId);
        Assert.Contains("KnownFirst analysis report", text);
        Assert.Contains("Sentence spans", text);
        Assert.Contains(AnalysisReasonCodes.SentenceBoundaryTerminatorCitation, text);
        Assert.Contains(AnalysisReasonCodes.OrdinaryWordCaseGrouping, text);
        Assert.Contains(AnalysisReasonCodes.RejectedDuplicateContext, text);
        Assert.Contains("targetRelativeStart=", text);
        Assert.Contains("All coordinate invariants passed.", text);
    }
#endif

    [TestMethod]
    public async Task UndoPreviousDecisionAsync_RestoresPreviousCandidateAndStatus()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));
        var first = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(first!.WordId, WordStatus.Ignored);

        var undone = await _service.UndoPreviousDecisionAsync();
        var restored = await _service.GetCurrentCandidateAsync();
        var word = await _database.ReadAsync(connection => connection.FindAsync<WordEntity>(first.WordId));

        Assert.IsTrue(undone);
        Assert.AreEqual(first.WordId, restored!.WordId);
        Assert.AreEqual(WordStatus.Unreviewed, word!.Status);
        Assert.AreEqual(0, restored.ReviewedCount);
    }

    [TestMethod]
    public async Task ImportAsync_WhenReviewIsActive_BlocksSecondImport()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));

        await Assert.ThrowsExactlyAsync<ActiveReviewExistsException>(
            () => _service.ImportAsync(CreateRequest("Second import.")));

        var documentCount = await _database.ReadAsync(connection =>
            connection.Table<DocumentEntity>().CountAsync());
        Assert.AreEqual(1, documentCount);
    }

    [TestMethod]
    public async Task ImportAsync_WhenCoordinateInvariantFails_RollsBackWithoutDeletingExistingData()
    {
        var existingId = await _database.RunInTransactionAsync(connection =>
        {
            var document = new DocumentEntity
            {
                Title = "Existing",
                TextLanguage = "en",
                ExplanationLanguage = "de",
                Content = "Existing content.",
                ContentFingerprint = "existing",
                ImportedAt = DateTime.UtcNow
            };
            connection.Insert(document);
            return document.Id;
        });
        var invalidService = new TextReviewService(
            _database,
            new TextAnalyzer(new InvalidSentenceSegmenter()),
            new DisabledEnhancedRecognitionSettings(),
            new ThrowingGermanLexicon());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => invalidService.ImportAsync(CreateRequest("New content.")));
        var retained = await _database.ReadAsync(connection =>
            connection.FindAsync<DocumentEntity>(existingId));
        var documentCount = await _database.ReadAsync(connection =>
            connection.Table<DocumentEntity>().CountAsync());

        Assert.Contains("SentenceRangeOutsideDocument", exception.Message);
        Assert.IsNotNull(retained);
        Assert.AreEqual("Existing content.", retained.Content);
        Assert.AreEqual(1, documentCount);
    }

    [TestMethod]
    public async Task ReviewRoutePolicy_BlocksDirectLearnPrepareAndImportUntilCompletion()
    {
        await _service.ImportAsync(CreateRequest("Only."));
        var active = await _service.GetActiveReviewAsync();

        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("learn", active is not null));
        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("prepare-words", active is not null));
        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("import-text", active is not null));
        Assert.IsFalse(ReviewRoutePolicy.IsBlocked("settings", active is not null));

        var candidate = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(candidate!.WordId, WordStatus.Known);
        active = await _service.GetActiveReviewAsync();

        Assert.IsFalse(ReviewRoutePolicy.IsBlocked("learn", active is not null));
    }

    [TestMethod]
    public async Task Completion_KnownAndIgnoredRetainOnlyMinimalMarkerData()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));
        var known = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(known!.WordId, WordStatus.Known);
        var ignored = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(ignored!.WordId, WordStatus.Ignored);

        var retained = await _database.ReadAsync(async connection =>
        {
            var knownWord = await connection.FindAsync<WordEntity>(known.WordId);
            var ignoredWord = await connection.FindAsync<WordEntity>(ignored.WordId);
            var knownOccurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == known.WordId)
                .CountAsync();
            var ignoredOccurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == ignored.WordId)
                .CountAsync();
            var formCount = await connection.Table<WordFormEntity>()
                .Where(item => item.WordId == known.WordId || item.WordId == ignored.WordId)
                .CountAsync();
            return (knownWord!, ignoredWord!, knownOccurrences, ignoredOccurrences, formCount);
        });

        Assert.AreEqual(WordStatus.Known, retained.Item1.Status);
        Assert.AreEqual(WordStatus.Ignored, retained.Item2.Status);
        Assert.AreEqual(0, retained.Item1.TotalOccurrenceCount);
        Assert.AreEqual(0, retained.Item2.TotalOccurrenceCount);
        Assert.AreEqual(0, retained.knownOccurrences);
        Assert.AreEqual(0, retained.ignoredOccurrences);
        Assert.AreEqual(0, retained.formCount);
    }

    [TestMethod]
    public async Task Completion_AllKnownDeletesTemporaryDocumentData()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));
        await CompleteReviewAsync(_ => WordStatus.Known);

        var counts = await _database.ReadAsync(async connection => new
        {
            Documents = await connection.Table<DocumentEntity>().CountAsync(),
            Sentences = await connection.Table<SentenceSpanEntity>().CountAsync(),
            Occurrences = await connection.Table<WordOccurrenceEntity>().CountAsync(),
            Sessions = await connection.Table<ReviewSessionEntity>().CountAsync(),
            Candidates = await connection.Table<ReviewCandidateEntity>().CountAsync(),
            KnownWords = await connection.Table<WordEntity>()
                .Where(word => word.Status == WordStatus.Known)
                .CountAsync()
        });

        Assert.AreEqual(0, counts.Documents);
        Assert.AreEqual(0, counts.Sentences);
        Assert.AreEqual(0, counts.Occurrences);
        Assert.AreEqual(0, counts.Sessions);
        Assert.AreEqual(0, counts.Candidates);
        Assert.AreEqual(2, counts.KnownWords);
    }

    [TestMethod]
    public async Task LegacyIgnoredVocabulary_RemainsReadableAndPreventsAnotherReviewPrompt()
    {
        var analyzed = new TextAnalyzer().Analyze("Legacy.");
        Assert.HasCount(1, analyzed.Candidates);
        var candidate = analyzed.Candidates[0];
        await _database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new WordEntity
            {
                Language = "en",
                CanonicalTerm = candidate.CanonicalTerm,
                NormalizedTerm = candidate.Identity,
                TokenKind = candidate.Kind,
                Status = WordStatus.Ignored,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return true;
        });

        var result = await _service.ImportAsync(CreateRequest("Legacy."));
        var ignored = (await _service.GetDiagnosticsAsync()).Candidates.Single();

        Assert.AreEqual(ImportAnalysisOutcome.NoNewVocabulary, result.Outcome);
        Assert.AreEqual(WordStatus.Ignored, ignored.Status);
        Assert.AreEqual("Legacy", ignored.CanonicalTerm);
    }

    [TestMethod]
    public async Task Completion_UnknownPreservesEveryAcceptedOccurrence()
    {
        await _service.ImportAsync(CreateRequest("Target. Target! Target? Target."));
        var candidate = await _service.GetCurrentCandidateAsync();

        await _service.DecideAsync(candidate!.WordId, WordStatus.UnknownBacklog);

        var retained = await _database.ReadAsync(async connection =>
        {
            var word = await connection.FindAsync<WordEntity>(candidate.WordId);
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == candidate.WordId)
                .ToListAsync();
            return (Word: word!, Occurrences: occurrences);
        });

        Assert.AreEqual(WordStatus.UnknownBacklog, retained.Word.Status);
        Assert.AreEqual(4, retained.Word.TotalOccurrenceCount);
        Assert.HasCount(4, retained.Occurrences);
        Assert.AreEqual(4, retained.Occurrences.Select(item => item.SentenceSpanId).Distinct().Count());
    }

    [TestMethod]
    public async Task ImportAsync_WhenReorderedTextContainsOnlyExistingVocabulary_RejectsWithoutWrites()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));
        var alpha = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(alpha!.WordId, WordStatus.Known);
        var beta = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(beta!.WordId, WordStatus.Ignored);
        var before = await CaptureDatabaseStateAsync();

        var nextImport = await _service.ImportAsync(CreateRequest("Beta Alpha."));
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(ImportAnalysisOutcome.NoNewVocabulary, nextImport.Outcome);
        Assert.IsTrue(nextImport.IsComplete);
        Assert.AreEqual(0, nextImport.CandidateCount);
        Assert.IsNull(await _service.GetActiveReviewAsync());
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task ImportAsync_WhenCompletedTextIsImportedAgain_RejectsExactDuplicateWithoutWrites()
    {
        const string content = "Alpha Beta Alpha.";
        await _service.ImportAsync(CreateRequest(content));
        await CompleteReviewAsync(_ => WordStatus.UnknownBacklog);
        var before = await CaptureDatabaseStateAsync();

        var result = await _service.ImportAsync(CreateRequest(content));
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(ImportAnalysisOutcome.ExactDuplicate, result.Outcome);
        Assert.AreEqual(0, result.DocumentId);
        Assert.AreEqual(0, result.SessionId);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task ImportAsync_WhenExistingUnknownAndOneNewCandidate_AcceptsOnlyNewReviewCandidate()
    {
        await _service.ImportAsync(CreateRequest("Knownword Unknownword Ignoredword."));
        await CompleteReviewAsync(candidate => candidate.Candidate switch
        {
            "Knownword" => WordStatus.Known,
            "Unknownword" => WordStatus.UnknownBacklog,
            "Ignoredword" => WordStatus.Ignored,
            _ => throw new InvalidOperationException("Unexpected setup candidate.")
        });
        var wordsBefore = await GetWordsByCanonicalTermAsync();

        var result = await _service.ImportAsync(CreateRequest(
            "Unknownword Unknownword Knownword Ignoredword Newword."));
        var active = await _service.GetActiveReviewAsync();
        var current = await _service.GetCurrentCandidateAsync();
        var wordsAfter = await GetWordsByCanonicalTermAsync();
        var persistedOccurrences = await _database.ReadAsync(connection =>
            connection.Table<WordOccurrenceEntity>()
                .Where(occurrence => occurrence.DocumentId == result.DocumentId)
                .CountAsync());

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        Assert.AreEqual(1, result.CandidateCount);
        Assert.AreEqual(1, active!.TotalCandidates);
        Assert.AreEqual("Newword", current!.Candidate);
        Assert.AreEqual(3, persistedOccurrences);
        Assert.AreEqual(
            wordsBefore["Unknownword"].TotalOccurrenceCount + 2,
            wordsAfter["Unknownword"].TotalOccurrenceCount);
        Assert.AreEqual(
            wordsBefore["Unknownword"].DocumentCount + 1,
            wordsAfter["Unknownword"].DocumentCount);
        Assert.AreEqual(
            wordsBefore["Knownword"].TotalOccurrenceCount,
            wordsAfter["Knownword"].TotalOccurrenceCount);
        Assert.AreEqual(
            wordsBefore["Ignoredword"].TotalOccurrenceCount,
            wordsAfter["Ignoredword"].TotalOccurrenceCount);
    }

    [TestMethod]
    public async Task ImportAsync_WhenPreflightFindsNoNewVocabulary_DatabaseStateIsIdentical()
    {
        await _service.ImportAsync(CreateRequest("Existing."));
        await CompleteReviewAsync(_ => WordStatus.Known);
        var before = await CaptureDatabaseStateAsync();

        var result = await _service.ImportAsync(CreateRequest("Existing Existing."));
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(ImportAnalysisOutcome.NoNewVocabulary, result.Outcome);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task Completion_AcceptedImportUpdatesExistingUnknownAndPreservesOccurrences()
    {
        await _service.ImportAsync(CreateRequest("Existing. Existing!"));
        await CompleteReviewAsync(_ => WordStatus.UnknownBacklog);

        await _service.ImportAsync(CreateRequest(
            "Existing? Existing. Existing! Genuinelynew."));
        var newCandidate = await _service.GetCurrentCandidateAsync();
        Assert.AreEqual("Genuinelynew", newCandidate!.Candidate);
        await _service.DecideAsync(newCandidate.WordId, WordStatus.Known);

        var retained = await _database.ReadAsync(async connection =>
        {
            var existing = await connection.Table<WordEntity>()
                .Where(word => word.NormalizedTerm == "W:existing")
                .FirstAsync();
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(occurrence => occurrence.WordId == existing.Id)
                .ToListAsync();
            return (existing, occurrences);
        });

        Assert.AreEqual(5, retained.existing.TotalOccurrenceCount);
        Assert.AreEqual(2, retained.existing.DocumentCount);
        Assert.HasCount(5, retained.occurrences);
        Assert.AreEqual(5, retained.occurrences.Select(occurrence => occurrence.SentenceSpanId).Distinct().Count());
    }

    [TestMethod]
    public async Task DiagnosticsReportFormatter_ContainsReadableReviewInformation()
    {
        await _service.ImportAsync(CreateRequest("Alpha appears. Alpha returns."));

        var snapshot = await _service.GetDiagnosticsAsync();
        var report = DiagnosticsReportFormatter.Format(snapshot);

        Assert.Contains("\"Documents\"", report);
        Assert.Contains("\"Sentences\"", report);
        Assert.Contains("\"Candidates\"", report);
        Assert.Contains("\"Occurrences\"", report);
        Assert.Contains("\"Sessions\"", report);
        Assert.Contains("\"LexicalCache\"", report);
        Assert.Contains("\"PreparationSessions\"", report);
        Assert.Contains("\"PreparedMeanings\"", report);
        Assert.Contains("\"LearningCards\"", report);
        Assert.Contains("\"LearningReviews\"", report);
        Assert.Contains("\"LearningSessions\"", report);
        Assert.Contains("\"CleanupEligibility\"", report);
        Assert.Contains("Test document", report);
        Assert.Contains("Alpha appears.", report);
        Assert.Contains("\"CandidateText\": \"Alpha\"", report);
        Assert.Contains("\"Status\": \"Active\"", report);
    }

    [TestMethod]
    public async Task DiscardActiveImportAsync_RemovesOnlyImportSpecificData()
    {
        var stableWords = await _database.RunInTransactionAsync(connection =>
        {
            var statuses = new[]
            {
                WordStatus.Known,
                WordStatus.UnknownBacklog,
                WordStatus.Ignored,
                WordStatus.Mastered
            };
            var words = new List<(int Id, WordStatus Status)>();

            foreach (var status in statuses)
            {
                var canonical = $"Stable{status}";
                var stable = new WordEntity
                {
                    Language = "en",
                    CanonicalTerm = canonical,
                    NormalizedTerm = $"W:{canonical.ToLowerInvariant()}",
                    TokenKind = TokenKind.Word,
                    Status = status,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                connection.Insert(stable);
                connection.Insert(new WordFormEntity { WordId = stable.Id, SurfaceForm = canonical });
                words.Add((stable.Id, status));
            }

            return words;
        });
        await _service.ImportAsync(CreateRequest("Temporary Remaining."));
        var temporary = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(temporary!.WordId, WordStatus.Known);

        await _service.DiscardActiveImportAsync();

        var remaining = await _database.ReadAsync(async connection =>
        {
            var preservedWords = new List<WordEntity>();
            foreach (var stableWord in stableWords)
            {
                preservedWords.Add((await connection.FindAsync<WordEntity>(stableWord.Id))!);
            }

            var documents = await connection.Table<DocumentEntity>().CountAsync();
            var sessions = await connection.Table<ReviewSessionEntity>().CountAsync();
            var importSpecificWords = await connection.Table<WordEntity>()
                .Where(item => item.NormalizedTerm == "W:temporary" || item.NormalizedTerm == "W:remaining")
                .CountAsync();
            return (preservedWords, documents, sessions, importSpecificWords);
        });

        Assert.HasCount(4, remaining.preservedWords);
        foreach (var stableWord in stableWords)
        {
            Assert.AreEqual(
                stableWord.Status,
                remaining.preservedWords.Single(item => item.Id == stableWord.Id).Status);
        }

        Assert.AreEqual(0, remaining.documents);
        Assert.AreEqual(0, remaining.sessions);
        Assert.AreEqual(0, remaining.importSpecificWords);
    }

    [TestMethod]
    public async Task DiscardActiveImportAsync_RollsBackExistingUnknownContribution()
    {
        await _service.ImportAsync(CreateRequest("Existing."));
        await CompleteReviewAsync(_ => WordStatus.UnknownBacklog);
        var before = await CaptureDatabaseStateAsync();

        await _service.ImportAsync(CreateRequest("Existing Existing Newword."));
        await _service.DiscardActiveImportAsync();
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task DatabaseSchema_ForwardMigrationPreservesExistingDataAndSettingsTable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"knownfirst-migration-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(path);

        // The legacy occurrence has to describe a real slice of the legacy document: its range must fit
        // inside the document and inside its SentenceSpan, and the exact substring it points at must be its
        // SurfaceForm. "Unchanged systems" carries the unchanged-content marker and the 'systems' surface
        // form of Word 42 at offset 10.
        const string documentContent = "Unchanged systems";
        const string surfaceForm = "systems";
        const int occurrenceStart = 10;
        const int occurrenceLength = 7;

        try
        {
            await connection.CreateTableAsync<DocumentEntity>();
            var document = new DocumentEntity
            {
                Title = "Existing",
                Content = documentContent,
                TextLanguage = "en",
                ExplanationLanguage = "de"
            };
            await connection.InsertAsync(document);
            await connection.ExecuteAsync("CREATE TABLE AppSettings (SettingKey TEXT PRIMARY KEY, SettingValue TEXT)");
            await connection.ExecuteAsync(
                "INSERT INTO AppSettings (SettingKey, SettingValue) VALUES (?, ?)",
                "theme_preference",
                "2");
            await connection.ExecuteAsync(
                "CREATE TABLE Words (Id INTEGER PRIMARY KEY AUTOINCREMENT, Language TEXT, CanonicalTerm TEXT, NormalizedTerm TEXT, Status INTEGER, TotalOccurrenceCount INTEGER, DocumentCount INTEGER, CreatedAt INTEGER, UpdatedAt INTEGER)");
            await connection.ExecuteAsync(
                "INSERT INTO Words (Id, Language, CanonicalTerm, NormalizedTerm, Status, TotalOccurrenceCount, DocumentCount, CreatedAt, UpdatedAt) VALUES (42, 'en', 'systems', 'systems', 2, 1, 1, 0, 0)");
            await connection.CreateTableAsync<LegacyMeaningEntity>();
            var legacyMeaning = new LegacyMeaningEntity
            {
                WordId = 42,
                DisplayTerm = "systems",
                Definition = "Legacy definition"
            };
            await connection.InsertAsync(legacyMeaning);
            await connection.CreateTableAsync<SentenceSpanEntity>();
            var legacySentence = new SentenceSpanEntity
            {
                Id = 7,
                DocumentId = document.Id,
                StartPosition = 0,
                Length = document.Content.Length,
                Order = 0
            };
            await connection.ExecuteAsync(
                "INSERT INTO SentenceSpans (Id, DocumentId, StartPosition, Length, \"Order\") VALUES (?, ?, ?, ?, ?)",
                legacySentence.Id,
                legacySentence.DocumentId,
                legacySentence.StartPosition,
                legacySentence.Length,
                legacySentence.Order);
            await connection.CreateTableAsync<LegacyWordOccurrenceEntity>();
            var legacyOccurrence = new LegacyWordOccurrenceEntity
            {
                WordId = 42,
                DocumentId = document.Id,
                SentenceSpanId = 7,
                StartPosition = occurrenceStart,
                Length = occurrenceLength,
                SurfaceForm = surfaceForm,
                Order = 1
            };
            await connection.InsertAsync(legacyOccurrence);

            await DatabaseSchema.InitializeAsync(connection);

            var preservedDocument = await connection.FindAsync<DocumentEntity>(document.Id);
            var preservedSetting = await connection.ExecuteScalarAsync<string>(
                "SELECT SettingValue FROM AppSettings WHERE SettingKey = ?",
                "theme_preference");
            var version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");
            var migratedMeaning = await connection.FindAsync<MeaningEntity>(legacyMeaning.Id);
            var migratedSentence = await connection.FindAsync<SentenceSpanEntity>(legacySentence.Id);
            var migratedOccurrence = await connection.FindAsync<WordOccurrenceEntity>(legacyOccurrence.Id);

            Assert.AreEqual(documentContent, preservedDocument!.Content);
            Assert.AreEqual(LexicalLookupMode.Definition, preservedDocument.LookupMode);
            Assert.AreEqual("2", preservedSetting);
            Assert.AreEqual(DatabaseSchema.CurrentVersion, version);
            Assert.AreEqual("systems", migratedMeaning!.DisplayTerm);
            Assert.AreEqual(TokenKind.Word, migratedMeaning.TokenKind);
            Assert.AreEqual(string.Empty, migratedMeaning.EncounteredSurfaceForm);
            Assert.AreEqual(string.Empty, migratedMeaning.GrammaticalRelationship);
            Assert.IsNotNull(migratedSentence);
            Assert.AreEqual(7, migratedSentence.Id);
            Assert.AreEqual(document.Id, migratedSentence.DocumentId);
            Assert.AreEqual(0, migratedSentence.StartPosition);
            Assert.AreEqual(documentContent.Length, migratedSentence.Length);
            Assert.AreEqual(0, migratedSentence.Order);

            // Full coordinate evidence: identity, ownership, offsets and the exact text the offsets address.
            Assert.IsNotNull(migratedOccurrence);
            Assert.AreEqual(legacyOccurrence.Id, migratedOccurrence.Id);
            Assert.AreEqual(42, migratedOccurrence.WordId);
            Assert.AreEqual(document.Id, migratedOccurrence.DocumentId);
            Assert.AreEqual(migratedSentence.DocumentId, migratedOccurrence.DocumentId);
            Assert.AreEqual(migratedSentence.Id, migratedOccurrence.SentenceSpanId);
            Assert.AreEqual(occurrenceStart, migratedOccurrence.StartPosition);
            Assert.AreEqual(occurrenceLength, migratedOccurrence.Length);
            Assert.AreEqual(surfaceForm, migratedOccurrence.SurfaceForm);
            Assert.AreEqual(1, migratedOccurrence.Order);
            Assert.AreEqual(TechnicalTokenFamily.None, migratedOccurrence.TechnicalFamily);

            var occurrenceEnd = migratedOccurrence.StartPosition + migratedOccurrence.Length;
            var sentenceEnd = migratedSentence.StartPosition + migratedSentence.Length;
            Assert.IsTrue(migratedOccurrence.StartPosition >= 0);
            Assert.IsTrue(occurrenceEnd <= preservedDocument.Content.Length);
            Assert.IsTrue(migratedOccurrence.StartPosition >= migratedSentence.StartPosition);
            Assert.IsTrue(occurrenceEnd <= sentenceEnd);
            Assert.IsTrue(sentenceEnd <= preservedDocument.Content.Length);
            Assert.AreEqual(
                migratedOccurrence.SurfaceForm,
                preservedDocument.Content.Substring(migratedOccurrence.StartPosition, migratedOccurrence.Length));
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    private async Task CompleteReviewAsync(Func<ReviewCandidateDetails, WordStatus> statusSelector)
    {
        while (await _service.GetCurrentCandidateAsync() is { } candidate)
        {
            await _service.DecideAsync(candidate.WordId, statusSelector(candidate));
        }
    }

    [Table("Meanings")]
    private sealed class LegacyMeaningEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int WordId { get; set; }

        public string DisplayTerm { get; set; } = string.Empty;

        public string Definition { get; set; } = string.Empty;
    }

    [Table("WordOccurrences")]
    private sealed class LegacyWordOccurrenceEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int WordId { get; set; }

        public int DocumentId { get; set; }

        public int SentenceSpanId { get; set; }

        public int StartPosition { get; set; }

        public int Length { get; set; }

        public string SurfaceForm { get; set; } = string.Empty;

        public int Order { get; set; }
    }

    private Task<Dictionary<string, WordEntity>> GetWordsByCanonicalTermAsync() =>
        _database.ReadAsync(async connection =>
        {
            var words = await connection.Table<WordEntity>().ToListAsync();
            return words.ToDictionary(word => word.CanonicalTerm, StringComparer.Ordinal);
        });

    private Task<string> CaptureDatabaseStateAsync() => _database.ReadAsync(async connection =>
    {
        var state = new
        {
            Documents = await connection.Table<DocumentEntity>().OrderBy(item => item.Id).ToListAsync(),
            Sentences = await connection.Table<SentenceSpanEntity>().OrderBy(item => item.Id).ToListAsync(),
            Words = await connection.Table<WordEntity>().OrderBy(item => item.Id).ToListAsync(),
            Forms = await connection.Table<WordFormEntity>().OrderBy(item => item.Id).ToListAsync(),
            Occurrences = await connection.Table<WordOccurrenceEntity>().OrderBy(item => item.Id).ToListAsync(),
            Meanings = await connection.Table<MeaningEntity>().OrderBy(item => item.Id).ToListAsync(),
            ReviewStates = await connection.Table<ReviewStateEntity>().OrderBy(item => item.Id).ToListAsync(),
            Sessions = await connection.Table<ReviewSessionEntity>().OrderBy(item => item.Id).ToListAsync(),
            ReviewCandidates = await connection.Table<ReviewCandidateEntity>().OrderBy(item => item.Id).ToListAsync()
        };
        // This test-only snapshot remains reflection-based; production JSON is source-generated.
        return JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        });
    });

    private static ImportTextRequest CreateRequest(string content) => new(
        "Test document",
        content,
        "en",
        "de");

    private sealed class InvalidSentenceSegmenter : ISentenceSegmenter
    {
        public IReadOnlyList<TextSpan> Segment(string content) =>
            [new TextSpan(content.Length + 1, 1, 0)];
    }

    private sealed class TemporaryDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SQLiteAsyncConnection? _connection;
        private bool _initialized;

        public TemporaryDatabase()
        {
            DatabasePath = Path.Combine(
                Path.GetTempPath(),
                $"knownfirst-review-{Guid.NewGuid():N}.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            await DatabaseSchema.InitializeAsync(_connection);
            _initialized = true;
        }

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                return await operation(_connection!);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ResetAsync()
        {
            await DisposeConnectionAsync();
            File.Delete(DatabasePath);
            _initialized = false;
            await InitializeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeConnectionAsync();
            File.Delete(DatabasePath);
            _gate.Dispose();
        }

        private async Task DisposeConnectionAsync()
        {
            if (_connection is null)
            {
                return;
            }

            await _connection.CloseAsync();
            _connection = null;
        }
    }

    private sealed class ExistingFixtureDatabase(Schema7Fixture fixture) : IKnownFirstDatabase
    {
        public string DatabasePath => fixture.DatabasePath;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation) => operation(fixture.Connection);

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            T? result = default;
            await fixture.Connection.RunInTransactionAsync(connection => result = operation(connection));
            return result!;
        }

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation) => RunInTransactionAsync(operation);

        public Task ResetAsync() => throw new NotSupportedException("Not used by this test.");
    }
}
