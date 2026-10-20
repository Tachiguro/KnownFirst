using System.Net;
using System.Net.Http.Headers;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Lexical.Wikipedia;
using KnownFirst.Services.Study;
using KnownFirst.Tests.LexicalEvidence;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PreparationLexicalCorrelationTests
{
    [TestMethod]
    public async Task DirectPreparationCandidate_CurrentSchema14_CorrelatesStartLookupSurfaceFormAndAttemptCounter()
    {
        await using var database = new TemporaryCurrentSchemaDatabase("kf-lex-005-corr-direct");
        await database.InitializeAsync();

        var schemaVersion = await database.ReadAsync(c => c.ExecuteScalarAsync<int>("PRAGMA user_version;"));
        Assert.AreEqual(14, DatabaseSchema.CurrentVersion);
        Assert.AreEqual(DatabaseSchema.CurrentVersion, schemaVersion, "Database must use current Schema 14 baseline.");

        var wordId = await ImportAndSelectTargetWordAsync(
            database,
            title: "EnglishDirectDoc",
            content: "Systems protect data.",
            language: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetCandidateTerm: "Systems",
            useEnhancedGermanRecognition: false);

        var correlation = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-001",
                Description: "Direct preparation candidate on Schema 14 with inflected surface form redirecting to base lemma.",
                Database: database,
                OfflineHttpHandler: (req, _) =>
                {
                    var uri = req.RequestUri?.ToString() ?? string.Empty;
                    if (uri.Contains("page=systems", StringComparison.Ordinal))
                    {
                        return Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-systems-form.json")));
                    }

                    if (uri.Contains("page=system", StringComparison.Ordinal))
                    {
                        return Task.FromResult(JsonResponse(BuildWiktionaryJson(
                            "system",
                            "<div class=\"mw-heading mw-heading2\"><h2 id=\"English\">English</h2></div><div class=\"mw-heading mw-heading3\"><h3>Noun</h3></div><ol><li><span class=\"definition\">A set of interacting components.</span></li></ol>")));
                    }

                    return Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")));
                }));

        var evidence = correlation.Evidence;
        var item = correlation.ItemAfterLookup;
        var candidate = correlation.PersistedCandidate;

        Assert.AreEqual(wordId, item.WordId);
        Assert.AreEqual(item.CandidateId, candidate.Id);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, item.Status);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, candidate.Status);
        Assert.AreEqual(string.Empty, candidate.LastErrorCode);

        // Persisted lookup attempt counter and three independent counters
        Assert.AreEqual(1, candidate.LookupAttemptCount);
        Assert.AreEqual(1, evidence.PreparationLookupAttemptCount);
        Assert.AreEqual(2, evidence.ProviderInvocationCount);
        Assert.AreEqual(0, evidence.WikipediaApiClientInvocationCount);
        Assert.AreEqual(2, evidence.HttpAttemptCount);
        Assert.AreEqual(0, evidence.HttpRetryCount);

        // Requested/canonical term vs encountered surface form and redirected lemma
        Assert.AreEqual("Systems", item.Term);
        Assert.AreEqual("system", item.LearningTerm);
        Assert.AreEqual("Systems", item.EncounteredSurfaceForm);
        Assert.AreEqual("Systems", evidence.DisplayedSurfaceForm);
        Assert.AreEqual("Systems", evidence.VocabularyCanonicalTerm);
        Assert.AreEqual("systems", evidence.CanonicalLookupTerm);
        Assert.AreEqual("system", evidence.FinalQueriedLemma);
        Assert.AreEqual("system", evidence.FinalDisplayTerm);
        Assert.AreEqual("Systems", evidence.EncounteredSurfaceForm);
        Assert.AreEqual("plural of system", evidence.GrammaticalRelationship);
        Assert.AreEqual(LexicalLookupStatus.Success, evidence.FinalStatus);
        Assert.AreEqual(LexicalOutcomeClassification.LemmaRedirectSuccess, evidence.OutcomeClassification);
        Assert.AreEqual(LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport, evidence.TransportRealism);
    }

    [TestMethod]
    public async Task DerivedCompoundCandidate_PreservesCompoundProvenanceWithZeroWordOccurrencesAndCorrelatesEvidence()
    {
        await using var database = new TemporaryCurrentSchemaDatabase("kf-lex-005-corr-derived");
        await database.InitializeAsync();

        var schemaVersion = await database.ReadAsync(c => c.ExecuteScalarAsync<int>("PRAGMA user_version;"));
        Assert.AreEqual(14, schemaVersion);

        var review = new TextReviewService(
            database,
            new TextAnalyzer(),
            new EnabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        await review.ImportAsync(new ImportTextRequest(
            "DerivedCompoundDoc",
            "Die Schreibmaschine steht im Zimmer.",
            "de",
            LexicalLookupMode.Definition,
            null));

        var maschineWordId = -1;
        while (await review.GetCurrentCandidateAsync() is { } reviewCandidate)
        {
            if (string.Equals(reviewCandidate.Candidate, "Maschine", StringComparison.Ordinal))
            {
                Assert.AreEqual(CandidateProvenanceKind.DerivedFromCompound, reviewCandidate.Provenance);
                Assert.HasCount(1, reviewCandidate.DerivationEvidence);
                Assert.AreEqual("Schreibmaschine", reviewCandidate.DerivationEvidence[0].SourceSurfaceForm);
                maschineWordId = reviewCandidate.WordId;
                await review.DecideAsync(reviewCandidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await review.DecideAsync(reviewCandidate.WordId, WordStatus.Known);
            }
        }

        Assert.AreNotEqual(-1, maschineWordId, "Derived compound candidate 'Maschine' must be emitted by TextReviewService.");

        // Verify zero WordOccurrence rows exist for derived-only candidate 'Maschine'
        var occurrenceCountBeforeLookup = await database.ReadAsync(c =>
            c.Table<WordOccurrenceEntity>().Where(o => o.WordId == maschineWordId).CountAsync());
        Assert.AreEqual(0, occurrenceCountBeforeLookup, "Derived-only candidate must not have WordOccurrence rows.");

        var maschineReviewCandidate = await database.ReadAsync(c =>
            c.Table<ReviewCandidateEntity>().Where(r => r.WordId == maschineWordId).FirstAsync());
        var derivedRows = await database.ReadAsync(c =>
            c.Table<DerivedTermEvidenceEntity>().Where(d => d.ReviewCandidateId == maschineReviewCandidate.Id).ToListAsync());
        Assert.HasCount(1, derivedRows);
        Assert.AreEqual("Schreibmaschine", derivedRows[0].SourceSurfaceForm);
        Assert.AreEqual("maschine", derivedRows[0].ComponentForm);

        var correlation = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-002",
                Description: "Genuine DerivedFromCompound preparation candidate preserving source compound provenance without WordOccurrence rows.",
                Database: database,
                OfflineHttpHandler: (req, _) =>
                {
                    var uri = req.RequestUri?.ToString() ?? string.Empty;
                    if (uri.Contains("page=Maschine", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(JsonResponse(BuildWiktionaryJson(
                            "Maschine",
                            "<div class=\"mw-heading mw-heading2\"><h2 id=\"Maschine_(Deutsch)\">Maschine (Deutsch)</h2></div><div class=\"mw-heading mw-heading3\"><h3>Substantiv</h3></div><p style=\"font-weight:bold;\">Bedeutungen:</p><dl><dd>[1] Mechanische oder technische Vorrichtung zur Arbeitsleistung.</dd></dl>")));
                    }

                    return Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")));
                }));

        var occurrenceCountAfterLookup = await database.ReadAsync(c =>
            c.Table<WordOccurrenceEntity>().Where(o => o.WordId == maschineWordId).CountAsync());
        Assert.AreEqual(0, occurrenceCountAfterLookup, "Lookup must not invent WordOccurrence rows for derived-only candidates.");

        Assert.IsNotNull(correlation.PersistedEnvelope);
        Assert.HasCount(1, correlation.PersistedEnvelope.FrozenEvidence);
        Assert.AreEqual(derivedRows[0].SourceStartPosition, correlation.PersistedEnvelope.FrozenEvidence[0].TargetStart);
        Assert.AreEqual(derivedRows[0].SourceLength, correlation.PersistedEnvelope.FrozenEvidence[0].TargetLength);

        // Correlate persisted candidate and evidence fields
        var evidence = correlation.Evidence;
        Assert.AreEqual(maschineWordId, correlation.ItemAfterLookup.WordId);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, correlation.PersistedCandidate.Status);
        Assert.AreEqual(1, correlation.PersistedCandidate.LookupAttemptCount);
        Assert.AreEqual(1, evidence.PreparationLookupAttemptCount);
        Assert.AreEqual(1, evidence.ProviderInvocationCount);
        Assert.AreEqual(1, evidence.HttpAttemptCount);
        Assert.AreEqual("Schreibmaschine", evidence.DisplayedSurfaceForm);
        Assert.AreEqual("Maschine", evidence.VocabularyCanonicalTerm);
        Assert.AreEqual("Maschine", evidence.CanonicalLookupTerm);
        Assert.AreEqual("Maschine", evidence.FinalQueriedLemma);
        Assert.AreEqual("Maschine", evidence.FinalDisplayTerm);
        Assert.AreEqual("Die Schreibmaschine steht im Zimmer.", evidence.RepresentativeContext);
        Assert.AreEqual(LexicalLookupStatus.Success, evidence.FinalStatus);
    }

    [TestMethod]
    public async Task MeaningSelection_UpdatesPersistedSelectedIndex_CorrelatesRankedMeaningsAndMeaningPreviewPolicy()
    {
        await using var database = new TemporaryCurrentSchemaDatabase("kf-lex-005-corr-select");
        await database.InitializeAsync();

        await ImportAndSelectTargetWordAsync(
            database,
            title: "EnglishSelectionDoc",
            content: "She learned the craft quickly.",
            language: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetCandidateTerm: "craft",
            useEnhancedGermanRecognition: false);

        var correlation = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-003",
                Description: "Multi-meaning candidate verifying SelectMeaningAsync and policy-level MeaningPreviewPolicy correlation.",
                Database: database,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(BuildWiktionaryJson(
                    "craft",
                    "<div class=\"mw-heading mw-heading2\"><h2 id=\"English\">English</h2></div><div class=\"mw-heading mw-heading3\"><h3>Noun</h3></div><ol>" +
                    "<li><span class=\"usage-label\">archaic</span><span class=\"definition\">Occult art or magic practiced in antiquity.</span></li>" +
                    "<li><span class=\"definition\">A skill, trade, or occupation requiring manual dexterity and artistic skill in workshop settings.</span></li>" +
                    "</ol>"))),
                PostLookupAction: async (prepService, itemAfterLookup, _) =>
                {
                    Assert.AreEqual(0, itemAfterLookup.SelectedMeaningIndex);
                    await prepService.SelectMeaningAsync(itemAfterLookup.CandidateId, 1);
                }));

        var evidence = correlation.Evidence;
        var itemBeforeSelect = correlation.ItemAfterLookup;
        var itemAfterSelect = correlation.ItemAfterPostLookupAction;

        Assert.IsNotNull(itemAfterSelect);
        Assert.AreEqual(0, itemBeforeSelect.SelectedMeaningIndex);
        Assert.AreEqual(1, itemAfterSelect.SelectedMeaningIndex);
        Assert.AreEqual(1, correlation.PersistedCandidate.SelectedMeaningIndex);

        // Verify ranking reordered general meaning ahead of archaic meaning and correlates with selectable meanings
        Assert.IsTrue(evidence.MeaningOrderChangedByRanking);
        Assert.HasCount(2, evidence.PreRankingMeanings);
        Assert.HasCount(2, evidence.RankedMeanings);
        Assert.IsNotNull(itemAfterSelect.Result);
        Assert.HasCount(2, itemAfterSelect.Result.Meanings);

        Assert.AreEqual(MeaningRelevanceTier.Standard, evidence.RankedMeanings[0].RelevanceTier);
        Assert.AreEqual(MeaningRelevanceTier.Demoted, evidence.RankedMeanings[1].RelevanceTier);
        Assert.AreEqual("archaic", evidence.RankedMeanings[1].TriggeringDemotionLabel);

        Assert.AreEqual(evidence.RankedMeanings[0].Definition, itemAfterSelect.Result.Meanings[0].Definition);
        Assert.AreEqual(evidence.RankedMeanings[1].Definition, itemAfterSelect.Result.Meanings[1].Definition);

        // Policy-level verification of MeaningPreviewPolicy for the selectable meanings (not a rendered GUI test)
        var selectableMeanings = MeaningPreviewPolicy.GetSelectableMeanings(
            itemAfterSelect.Result.Meanings,
            itemAfterSelect.LookupMode);
        Assert.HasCount(2, selectableMeanings);
        Assert.AreEqual(0, selectableMeanings[0].OriginalIndex);
        Assert.AreEqual(1, selectableMeanings[1].OriginalIndex);
        Assert.AreEqual(evidence.RankedMeanings[0].Definition, selectableMeanings[0].FullText);
        Assert.AreEqual(evidence.RankedMeanings[1].Definition, selectableMeanings[1].FullText);

        var selectedMeaning = itemAfterSelect.Result.Meanings[itemAfterSelect.SelectedMeaningIndex];
        var previewClosed = MeaningPreviewPolicy.CreateClosedPreview(selectedMeaning.Definition);
        Assert.AreEqual("Occult art or magic practiced in antiquity.", previewClosed);

        var rank0Meaning = itemAfterSelect.Result.Meanings[0];
        var previewTruncated = MeaningPreviewPolicy.CreatePreview(rank0Meaning.Definition, maximumLength: 32);
        Assert.IsTrue(previewTruncated.EndsWith('…'));
    }

    [TestMethod]
    public async Task MeaningSelection_AutoResolvesExactVariantsAgainstExistingSenseDuringLookup()
    {
        await using var database = new TemporaryCurrentSchemaDatabase("kf-lex-005-corr-autoresolve");
        await database.InitializeAsync();

        static Task<HttpResponseMessage> SystemsRedirectHandler(HttpRequestMessage req, CancellationToken _)
        {
            var uri = req.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("page=systems", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-systems-form.json")));
            }

            if (uri.Contains("page=system", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(BuildWiktionaryJson(
                    "system",
                    "<div class=\"mw-heading mw-heading2\"><h2 id=\"English\">English</h2></div><div class=\"mw-heading mw-heading3\"><h3>Noun</h3></div><ol><li><span class=\"definition\">A set of interacting components.</span></li></ol>")));
            }

            return Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")));
        }

        // Step 1: Import Document 1 for "Systems", look up (redirecting to "system" with GrammaticalRelationship = "plural of system"), and accept
        var wordId = await ImportAndSelectTargetWordAsync(
            database,
            title: "SystemsDoc1",
            content: "Systems protect critical data.",
            language: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetCandidateTerm: "Systems",
            useEnhancedGermanRecognition: false);

        await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-004A",
                Description: "Initial preparation and acceptance of 'Systems' with reliable grammatical-relationship discriminator.",
                Database: database,
                OfflineHttpHandler: SystemsRedirectHandler,
                PostLookupAction: async (prepService, item, _) =>
                {
                    var meaning = item.Result!.Meanings[0];
                    var input = new PreparedMeaningInput(
                        SelectedMeaningId: meaning.MeaningId,
                        AcronymExpansion: item.Result.AcronymExpansion,
                        Translation: meaning.Translation,
                        Definition: meaning.Definition,
                        DictionaryExample: meaning.Example,
                        AdditionalNote: null,
                        AcceptedAliases: [],
                        ProviderName: item.Result.ProviderName,
                        SourceProject: item.Result.SourceProject,
                        SourcePageTitle: item.Result.PageTitle,
                        SourceRevisionId: item.Result.RevisionId,
                        Attribution: item.Result.Attribution,
                        EncounteredSurfaceForm: item.EncounteredSurfaceForm,
                        GrammaticalRelationship: item.Result.GrammaticalRelationship);
                    await prepService.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);
                }));

        // Step 2: Import Document 2 with genuinely new evidence for "Systems" and clear LexicalCache so lookup invokes provider
        await ImportAndSelectTargetWordAsync(
            database,
            title: "SystemsDoc2",
            content: "Systems require regular maintenance. Nonce zzzautoresolve appears.",
            language: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetCandidateTerm: "Systems",
            useEnhancedGermanRecognition: false);
        await database.ReadAsync(c => c.ExecuteAsync("DELETE FROM LexicalCache"));

        // Step 3: Re-lookup matches existing Sense via GrammaticalRelationship discriminator ("plural of system"),
        // auto-resolving index 0 and auto-completing the candidate to Prepared without explicit AcceptAsync.
        var autoResolved = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-004B",
                Description: "Second lookup of 'Systems' auto-resolving exact-match Sense and auto-completing candidate to Prepared.",
                Database: database,
                OfflineHttpHandler: SystemsRedirectHandler));

        Assert.AreEqual(wordId, autoResolved.ItemAfterLookup.WordId);
        Assert.AreEqual(PreparationCandidateStatus.Prepared, autoResolved.ItemAfterLookup.Status);
        Assert.AreEqual(PreparationCandidateStatus.Prepared, autoResolved.PersistedCandidate.Status);
        Assert.IsNotNull(autoResolved.PersistedEnvelope);
        CollectionAssert.AreEqual(
            new[] { 0 },
            autoResolved.PersistedEnvelope.ResolvedProviderMeaningIndexes.ToArray());

        var senseCount = await database.ReadAsync(c =>
            c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
        Assert.AreEqual(1, senseCount, "Auto-resolution against existing Sense must not duplicate Sense rows.");
    }

    [TestMethod]
    public async Task FailureAndRetry_DistinguishesApplicationRetryFromHttpTransportRetryAndUpdatesCandidateState()
    {
        await using var database = new TemporaryCurrentSchemaDatabase("kf-lex-005-corr-retry");
        await database.InitializeAsync();

        await ImportAndSelectTargetWordAsync(
            database,
            title: "GermanRetryDoc",
            content: "Das Netzwerk wird geprüft.",
            language: "de",
            lookupMode: LexicalLookupMode.Definition,
            targetCandidateTerm: "Netzwerk",
            useEnhancedGermanRecognition: false);

        // Application Attempt 1: All 3 HTTP transport attempts fail with 503 ServiceUnavailable -> TransientFailure
        var attempt1 = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-005A",
                Description: "First PreparationService.LookupCurrentAsync call failing with TransientFailure after 3 HTTP transport attempts.",
                Database: database,
                OfflineHttpHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        Assert.AreEqual(PreparationCandidateStatus.Failed, attempt1.ItemAfterLookup.Status);
        Assert.AreEqual(PreparationCandidateStatus.Failed, attempt1.PersistedCandidate.Status);
        Assert.AreEqual("transient-server-error", attempt1.PersistedCandidate.LastErrorCode);

        // Verify the 3 distinct counters on Attempt 1:
        // - PreparationLookupAttemptCount = 1 (1 LookupCurrentAsync call)
        // - ProviderInvocationCount = 1 (1 WiktionaryLookupProvider.LookupAsync invocation)
        // - HttpAttemptCount = 3 (1 initial + 2 HTTP transport retries inside WiktionaryLookupProvider)
        Assert.AreEqual(1, attempt1.Evidence.PreparationLookupAttemptCount);
        Assert.AreEqual(1, attempt1.Evidence.ProviderInvocationCount);
        Assert.AreEqual(0, attempt1.Evidence.WikipediaApiClientInvocationCount);
        Assert.AreEqual(3, attempt1.Evidence.HttpAttemptCount);
        Assert.AreEqual(2, attempt1.Evidence.HttpRetryCount);
        Assert.AreEqual(LexicalLookupStatus.TransientFailure, attempt1.Evidence.FinalStatus);
        Assert.AreEqual("transient-server-error", attempt1.Evidence.FinalErrorCode);

        // Application Attempt 2: Retry LookupCurrentAsync on the same Failed candidate -> succeeds on first HTTP attempt
        var attempt2 = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-005B",
                Description: "Second PreparationService.LookupCurrentAsync application-level retry succeeding on the same candidate.",
                Database: database,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("german-entry.json")))));

        Assert.AreEqual(attempt1.PersistedCandidate.Id, attempt2.PersistedCandidate.Id, "Application retry must target the same PreparationCandidateEntity.");
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, attempt2.ItemAfterLookup.Status);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, attempt2.PersistedCandidate.Status);
        Assert.AreEqual(string.Empty, attempt2.PersistedCandidate.LastErrorCode, "LastErrorCode must be cleared after successful retry.");

        // Verify the 3 distinct counters on Attempt 2:
        // - PreparationLookupAttemptCount = 2 (persisted across LookupCurrentAsync calls)
        // - ProviderInvocationCount = 1 (for this second lookup execution)
        // - HttpAttemptCount = 1 (no HTTP transport retries on this second execution)
        Assert.AreEqual(2, attempt2.PersistedCandidate.LookupAttemptCount);
        Assert.AreEqual(2, attempt2.Evidence.PreparationLookupAttemptCount);
        Assert.AreEqual(1, attempt2.Evidence.ProviderInvocationCount);
        Assert.AreEqual(0, attempt2.Evidence.WikipediaApiClientInvocationCount);
        Assert.AreEqual(1, attempt2.Evidence.HttpAttemptCount);
        Assert.AreEqual(0, attempt2.Evidence.HttpRetryCount);
        Assert.AreEqual(LexicalLookupStatus.Success, attempt2.Evidence.FinalStatus);
        Assert.IsNull(attempt2.Evidence.FinalErrorCode);
    }

    [TestMethod]
    public async Task PrefetchIsolation_PreventsContaminationAcrossCandidatesAndCancelsPrefetchBeforeDisposal()
    {
        await using var database = new TemporaryCurrentSchemaDatabase("kf-lex-005-corr-prefetch");
        await database.InitializeAsync();

        // Import two unknown German words ("Netzwerk" and "Haus") into the backlog
        var review = new TextReviewService(
            database,
            new TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        await review.ImportAsync(new ImportTextRequest(
            "TwoCandidateDoc",
            "Das Netzwerk verbindet jedes Haus.",
            "de",
            LexicalLookupMode.Definition,
            null));

        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, "Netzwerk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Candidate, "Haus", StringComparison.OrdinalIgnoreCase))
            {
                await review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        // Start a 2-candidate session so PreparationService triggers BeginPrefetch for the second candidate
        // when the first candidate's LookupCurrentAsync succeeds.
        var correlation = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-PREP-CORR-006",
                Description: "Prefetch isolation verification in a two-candidate preparation session.",
                Database: database,
                RequestedSessionLimit: 2,
                OfflineHttpHandler: (req, _) =>
                {
                    var uri = req.RequestUri?.ToString() ?? string.Empty;
                    if (uri.Contains("page=Netzwerk", StringComparison.OrdinalIgnoreCase)
                        || uri.Contains("page=Haus", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(JsonResponse(LoadWiktionaryFixture("german-entry.json")));
                    }

                    return Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")));
                },
                PostLookupAction: async (_, _, ct) =>
                {
                    // Yield briefly so any background prefetch task initiated by BeginPrefetch runs into the harness barrier
                    await Task.Delay(25, ct);
                }));

        var primaryTerm = correlation.ItemAfterLookup.Term;
        var otherTerm = string.Equals(primaryTerm, "Netzwerk", StringComparison.Ordinal) ? "Haus" : "Netzwerk";

        // Primary candidate evidence must only contain calls for primaryTerm, never contaminated by otherTerm's prefetch
        Assert.AreEqual(1, correlation.Evidence.PreparationLookupAttemptCount);
        Assert.AreEqual(1, correlation.Evidence.ProviderInvocationCount);
        Assert.AreEqual(1, correlation.Evidence.HttpAttemptCount);
        CollectionAssert.AreEqual(new[] { primaryTerm }, correlation.Evidence.OrderedQueryCandidates.ToArray());
        Assert.IsFalse(
            correlation.Evidence.LookupSteps.Any(s => string.Equals(s.QueriedTerm, otherTerm, StringComparison.Ordinal)),
            "Background prefetch for the next candidate must never contaminate the primary candidate's LookupSteps.");

        // Any background prefetch activity triggered for the second candidate is attributed to otherTerm
        foreach (var prefetchedTerm in correlation.IsolatedPrefetchTerms)
        {
            Assert.AreEqual(otherTerm, prefetchedTerm);
        }

        // And the second candidate in the database still has LookupAttemptCount == 0 because prefetch does not increment it
        var secondCandidate = await database.ReadAsync(c =>
            c.Table<PreparationCandidateEntity>()
                .Where(x => x.Id != correlation.PersistedCandidate.Id)
                .FirstAsync());
        Assert.AreEqual(0, secondCandidate.LookupAttemptCount);
        Assert.AreEqual(PreparationCandidateStatus.Pending, secondCandidate.Status);
    }

    [TestMethod]
    public async Task EmitCanonicalLexicalEvidenceBundle_WritesDeterministicIgnoredArtifactsToCanonicalDestination()
    {
        var canonicalDir = LexicalEvidenceArtifactWriter.GetArtifactDirectory();
        StringAssert.EndsWith(canonicalDir, Path.Combine("artifacts", "lexical-lookup-evidence"));

        var candidatesFirstRun = await CollectCanonicalEvidenceSuiteAsync();
        var bundle1 = LexicalEvidenceArtifactWriter.WriteBundle(
            suiteId: "KF-LEX-005-CANONICAL-EVIDENCE-V1",
            description: "Deterministic lexical lookup and preparation correlation evidence bundle.",
            candidates: candidatesFirstRun,
            targetDirectory: canonicalDir,
            capturedAtUtc: LexicalEvidenceCaptureHarness.DefaultUtcNow);

        var summaryJsonBytes1 = await File.ReadAllBytesAsync(bundle1.SummaryJsonPath);
        var summaryTextBytes1 = await File.ReadAllBytesAsync(bundle1.SummaryTextPath);
        var candidateJsonBytes1 = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var candidateTextBytes1 = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in bundle1.CandidateFiles)
        {
            candidateJsonBytes1[entry.CaseId] = await File.ReadAllBytesAsync(entry.JsonPath);
            candidateTextBytes1[entry.CaseId] = await File.ReadAllBytesAsync(entry.TextPath);
        }

        // Second independent execution in reversed candidate order must produce byte-identical files
        var candidatesSecondRun = (await CollectCanonicalEvidenceSuiteAsync()).Reverse().ToArray();
        var bundle2 = LexicalEvidenceArtifactWriter.WriteBundle(
            suiteId: "KF-LEX-005-CANONICAL-EVIDENCE-V1",
            description: "Deterministic lexical lookup and preparation correlation evidence bundle.",
            candidates: candidatesSecondRun,
            targetDirectory: canonicalDir,
            capturedAtUtc: LexicalEvidenceCaptureHarness.DefaultUtcNow);

        CollectionAssert.AreEqual(summaryJsonBytes1, await File.ReadAllBytesAsync(bundle2.SummaryJsonPath));
        CollectionAssert.AreEqual(summaryTextBytes1, await File.ReadAllBytesAsync(bundle2.SummaryTextPath));
        foreach (var entry in bundle2.CandidateFiles)
        {
            CollectionAssert.AreEqual(candidateJsonBytes1[entry.CaseId], await File.ReadAllBytesAsync(entry.JsonPath));
            CollectionAssert.AreEqual(candidateTextBytes1[entry.CaseId], await File.ReadAllBytesAsync(entry.TextPath));
        }

        Assert.AreEqual(7, bundle2.Document.Summary.TotalCandidateCount);
        Assert.AreEqual(5, bundle2.Document.Summary.PreparationServiceCandidateCount);
        Assert.AreEqual(2, bundle2.Document.Summary.DirectEnrichmentCandidateCount);
        Assert.AreEqual(6, bundle2.Document.Summary.SuccessCount);
        Assert.AreEqual(1, bundle2.Document.Summary.TransientFailureCount);
    }

    private static async Task<IReadOnlyList<LexicalCandidateEvidence>> CollectCanonicalEvidenceSuiteAsync()
    {
        var list = new List<LexicalCandidateEvidence>();

        // 1. Direct preparation candidate (Schema 14, plural surface form redirect)
        await using (var db1 = new TemporaryCurrentSchemaDatabase("kf-lex-suite-1"))
        {
            await db1.InitializeAsync();
            await ImportAndSelectTargetWordAsync(
                db1,
                "SuiteDoc1",
                "Systems protect data.",
                "en",
                LexicalLookupMode.Definition,
                "Systems",
                useEnhancedGermanRecognition: false);

            var c1 = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
                new LexicalEvidencePreparationCaptureOptions(
                    CaseId: "KF-LEX-005-CASE-01-PREP-DIRECT-REDIRECT",
                    Description: "PreparationService lookup of inflected plural surface form redirecting to base lemma on Schema 14.",
                    Database: db1,
                    OfflineHttpHandler: (req, _) =>
                    {
                        var uri = req.RequestUri?.ToString() ?? string.Empty;
                        if (uri.Contains("page=systems", StringComparison.Ordinal))
                        {
                            return Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-systems-form.json")));
                        }

                        return Task.FromResult(JsonResponse(BuildWiktionaryJson(
                            "system",
                            "<div class=\"mw-heading mw-heading2\"><h2 id=\"English\">English</h2></div><div class=\"mw-heading mw-heading3\"><h3>Noun</h3></div><ol><li><span class=\"definition\">A set of interacting components.</span></li></ol>")));
                    }));
            list.Add(c1.Evidence);
        }

        // 2. Derived compound preparation candidate (Schema 14, DerivedFromCompound, zero WordOccurrences)
        await using (var db2 = new TemporaryCurrentSchemaDatabase("kf-lex-suite-2"))
        {
            await db2.InitializeAsync();
            var review = new TextReviewService(
                db2,
                new TextAnalyzer(),
                new EnabledEnhancedRecognitionSettings(),
                new FixtureGermanLexicon());
            await review.ImportAsync(new ImportTextRequest(
                "SuiteDoc2",
                "Die Schreibmaschine steht im Zimmer.",
                "de",
                LexicalLookupMode.Definition,
                null));
            while (await review.GetCurrentCandidateAsync() is { } rc)
            {
                await review.DecideAsync(
                    rc.WordId,
                    string.Equals(rc.Candidate, "Maschine", StringComparison.Ordinal)
                        ? WordStatus.UnknownBacklog
                        : WordStatus.Known);
            }

            var c2 = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
                new LexicalEvidencePreparationCaptureOptions(
                    CaseId: "KF-LEX-005-CASE-02-PREP-DERIVED-COMPOUND",
                    Description: "PreparationService lookup of DerivedFromCompound candidate 'Maschine' from 'Schreibmaschine' with zero WordOccurrence rows.",
                    Database: db2,
                    OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(BuildWiktionaryJson(
                        "Maschine",
                        "<div class=\"mw-heading mw-heading2\"><h2 id=\"Maschine_(Deutsch)\">Maschine (Deutsch)</h2></div><div class=\"mw-heading mw-heading3\"><h3>Substantiv</h3></div><p style=\"font-weight:bold;\">Bedeutungen:</p><dl><dd>[1] Mechanische oder technische Vorrichtung zur Arbeitsleistung.</dd></dl>")))));
            list.Add(c2.Evidence);
        }

        // 3. Multi-meaning preparation candidate with ranking reorder and SelectMeaningAsync
        await using (var db3 = new TemporaryCurrentSchemaDatabase("kf-lex-suite-3"))
        {
            await db3.InitializeAsync();
            await ImportAndSelectTargetWordAsync(
                db3,
                "SuiteDoc3",
                "She learned the craft quickly.",
                "en",
                LexicalLookupMode.Definition,
                "craft",
                useEnhancedGermanRecognition: false);

            var c3 = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
                new LexicalEvidencePreparationCaptureOptions(
                    CaseId: "KF-LEX-005-CASE-03-PREP-MEANING-SELECTION",
                    Description: "English multi-meaning preparation candidate with archaic-sense demotion and SelectMeaningAsync correlation.",
                    Database: db3,
                    OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(BuildWiktionaryJson(
                        "craft",
                        "<div class=\"mw-heading mw-heading2\"><h2 id=\"English\">English</h2></div><div class=\"mw-heading mw-heading3\"><h3>Noun</h3></div><ol>" +
                        "<li><span class=\"usage-label\">archaic</span><span class=\"definition\">Occult art or magic practiced in antiquity.</span></li>" +
                        "<li><span class=\"definition\">A skill, trade, or occupation requiring manual dexterity and artistic skill.</span></li>" +
                        "</ol>"))),
                    PostLookupAction: async (prep, item, _) => await prep.SelectMeaningAsync(item.CandidateId, 1)));
            list.Add(c3.Evidence);
        }

        // 4 & 5. Preparation transient failure (Attempt 1) followed by application-level retry success (Attempt 2)
        await using (var db4 = new TemporaryCurrentSchemaDatabase("kf-lex-suite-4"))
        {
            await db4.InitializeAsync();
            await ImportAndSelectTargetWordAsync(
                db4,
                "SuiteDoc4",
                "Das Netzwerk wird geprüft.",
                "de",
                LexicalLookupMode.Definition,
                "Netzwerk",
                useEnhancedGermanRecognition: false);

            var c4Fail = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
                new LexicalEvidencePreparationCaptureOptions(
                    CaseId: "KF-LEX-005-CASE-04-PREP-TRANSIENT-FAILURE",
                    Description: "PreparationService first lookup attempt failing with HTTP 503 across 3 transport attempts.",
                    Database: db4,
                    OfflineHttpHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
            list.Add(c4Fail.Evidence);

            var c5Retry = await LexicalEvidenceCaptureHarness.CapturePreparationCorrelationAsync(
                new LexicalEvidencePreparationCaptureOptions(
                    CaseId: "KF-LEX-005-CASE-05-PREP-RETRY-SUCCESS",
                    Description: "PreparationService second lookup attempt succeeding on retry with persisted LookupAttemptCount=2.",
                    Database: db4,
                    OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("german-entry.json")))));
            list.Add(c5Retry.Evidence);
        }

        // 6. German case fallback + Wikipedia fallback (SimulatedWikipediaApiClient)
        var c6Wiki = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "KF-LEX-005-CASE-06-ENRICH-WIKIPEDIA-FALLBACK",
                Description: "German lowercase noun attempting case fallback on Wiktionary then recovering via Wikipedia fallback.",
                Request: new LexicalLookupRequest(
                    sourceLanguage: "de",
                    lookupMode: LexicalLookupMode.Definition,
                    targetLanguage: null,
                    canonicalLookupTerm: "quantenmechanik",
                    tokenKind: TokenKind.Word,
                    provider: WiktionaryLookupProvider.Name,
                    displayedSurfaceForm: "quantenmechanik",
                    vocabularyCanonicalTerm: "quantenmechanik"),
                OriginalDocumentContent: "Die quantenmechanik beschreibt physikalische Systeme.",
                RepresentativeContext: "Die quantenmechanik beschreibt physikalische Systeme.",
                TransportRealism: LexicalEvidenceTransportRealism.SimulatedWikipediaApiClient,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json"))),
                SimulatedWikipediaApiClient: new StubWikipediaApiClient()));
        list.Add(c6Wiki);

        // 7. Translation mode with extracted English gloss and Wikipedia fallback skipped by mode restriction
        var c7Trans = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "KF-LEX-005-CASE-07-ENRICH-TRANSLATION-GLOSS",
                Description: "German->English translation lookup extracting English gloss while enforcing translation fallback restrictions.",
                Request: new LexicalLookupRequest(
                    sourceLanguage: "de",
                    lookupMode: LexicalLookupMode.Translation,
                    targetLanguage: "en",
                    canonicalLookupTerm: "Netzwerk",
                    tokenKind: TokenKind.Word,
                    provider: WiktionaryLookupProvider.Name,
                    displayedSurfaceForm: "Netzwerk",
                    vocabularyCanonicalTerm: "Netzwerk"),
                OriginalDocumentContent: "Das Netzwerk ist sicher.",
                RepresentativeContext: "Das Netzwerk ist sicher.",
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("german-entry.json")))));
        list.Add(c7Trans);

        return list;
    }

    private static async Task<int> ImportAndSelectTargetWordAsync(
        IKnownFirstDatabase database,
        string title,
        string content,
        string language,
        LexicalLookupMode lookupMode,
        string targetCandidateTerm,
        bool useEnhancedGermanRecognition)
    {
        IAppSettingsService recognitionSettings = useEnhancedGermanRecognition
            ? new EnabledEnhancedRecognitionSettings()
            : new DisabledEnhancedRecognitionSettings();

        var review = new TextReviewService(
            database,
            new TextAnalyzer(),
            recognitionSettings,
            new FixtureGermanLexicon());

        await review.ImportAsync(new ImportTextRequest(title, content, language, lookupMode, null));

        var targetWordId = -1;
        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, targetCandidateTerm, StringComparison.OrdinalIgnoreCase))
            {
                targetWordId = candidate.WordId;
                await review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        if (targetWordId == -1)
        {
            var words = await database.ReadAsync(c => c.Table<WordEntity>().ToListAsync());
            var existingWord = words.FirstOrDefault(w =>
                string.Equals(w.CanonicalTerm, targetCandidateTerm, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.NormalizedTerm, targetCandidateTerm, StringComparison.OrdinalIgnoreCase));
            if (existingWord is not null)
            {
                return existingWord.Id;
            }

            throw new InvalidOperationException($"Target candidate '{targetCandidateTerm}' was not found during review.");
        }

        return targetWordId;
    }

    private static string LoadWiktionaryFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wiktionary", fileName), Encoding.UTF8);

    private static HttpResponseMessage JsonResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        return response;
    }

    private static string BuildWiktionaryJson(string title, string html)
    {
        var escapedTitle = title.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var escapedHtml = html.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $$"""
        {
          "parse": {
            "title": "{{escapedTitle}}",
            "pageid": 200,
            "revid": 6000,
            "text": "{{escapedHtml}}"
          }
        }
        """;
    }

    private sealed class StubWikipediaApiClient : IWikipediaApiClient
    {
        public Task<WikipediaArticleResult> GetArticleAsync(
            WikipediaArticleRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WikipediaArticleResult(
                Status: WikipediaArticleStatus.Success,
                RequestedTitle: request.RequestedTitle,
                CanonicalTitle: "Quantenmechanik",
                Extract: "Quantenmechanik ist ein zentrales Teilgebiet der theoretischen Physik.",
                SourceLanguage: request.SourceLanguage,
                SourceProject: "de.wikipedia.org",
                PageId: 4200,
                RevisionId: 9001,
                CanonicalUrl: "https://de.wikipedia.org/wiki/Quantenmechanik",
                IsRedirect: false,
                RedirectedFrom: null,
                TargetLanguage: null,
                TargetTitleCandidate: null,
                TargetUrlCandidate: null,
                Attribution: "Wikipedia contributors (CC BY-SA 4.0).",
                ErrorCode: null,
                RetryAfter: null));
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

    private sealed class TemporaryCurrentSchemaDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SQLiteAsyncConnection? _connection;
        private bool _initialized;

        public TemporaryCurrentSchemaDatabase(string prefix)
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            if (!_initialized)
            {
                await DatabaseSchema.InitializeAsync(_connection);
                _initialized = true;
            }
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
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(_connection, DatabasePath);
            _connection = null;
            _initialized = false;
            await InitializeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await TemporaryDatabaseFiles.CloseAndDeleteAsync(_connection, DatabasePath);
                _connection = null;
            }
            finally
            {
                _gate.Dispose();
            }
        }
    }
}
