using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using KnownFirst.Services;
using Microsoft.Extensions.DependencyInjection;
using SQLite;
using static KnownFirst.Tests.DatabaseSchema13ProductionCutoverTests;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class VocabularyDetailServiceTests
{
    private static readonly DateTime BaseTimeUtc = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset BaseOffsetUtc = new(BaseTimeUtc, TimeSpan.Zero);

    [TestMethod]
    public async Task InvalidAndNonexistentWordIds_AreDistinguishedFromDatabaseFailures()
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();
        var countingDb = new SnapshotCountingDatabase(innerDb);

        IVocabularyDetailService service = new VocabularyDetailService(countingDb);

        var zeroResult = await service.GetVocabularyDetailAsync(0);
        Assert.AreEqual(VocabularyDetailLookupStatus.InvalidWordId, zeroResult.Status);
        Assert.IsTrue(zeroResult.IsInvalidWordId);
        Assert.IsFalse(zeroResult.IsFound);
        Assert.IsFalse(zeroResult.IsNotFound);
        Assert.AreEqual(0, zeroResult.RequestedWordId);
        Assert.IsNull(zeroResult.Detail);
        Assert.AreEqual(0, countingDb.SnapshotCount, "Invalid WordId must not open a database snapshot.");

        var negativeResult = await service.GetVocabularyDetailAsync(-42);
        Assert.AreEqual(VocabularyDetailLookupStatus.InvalidWordId, negativeResult.Status);
        Assert.IsTrue(negativeResult.IsInvalidWordId);
        Assert.IsFalse(negativeResult.IsFound);
        Assert.IsFalse(negativeResult.IsNotFound);
        Assert.AreEqual(-42, negativeResult.RequestedWordId);
        Assert.IsNull(negativeResult.Detail);
        Assert.AreEqual(0, countingDb.SnapshotCount, "Negative WordId must not open a database snapshot.");

        var missingResult = await service.GetVocabularyDetailAsync(999_999);
        Assert.AreEqual(VocabularyDetailLookupStatus.NotFound, missingResult.Status);
        Assert.IsTrue(missingResult.IsNotFound);
        Assert.IsFalse(missingResult.IsFound);
        Assert.IsFalse(missingResult.IsInvalidWordId);
        Assert.AreEqual(999_999, missingResult.RequestedWordId);
        Assert.IsNull(missingResult.Detail);
        Assert.AreEqual(1, countingDb.SnapshotCount, "Nonexistent positive WordId must check the database inside one snapshot.");

        var faultingService = new VocabularyDetailService(new FaultingDatabase(new InvalidOperationException("Simulated SQLite snapshot failure.")));
        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => faultingService.GetVocabularyDetailAsync(1));
        StringAssert.Contains(thrown.Message, "Simulated SQLite snapshot failure.");
    }

    [TestMethod]
    public async Task WordWithNoDetailChildren_ReturnsValidFoundDetailWithEmptyCollections()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var createdAt = BaseTimeUtc.AddMinutes(5);
        var updatedAt = BaseTimeUtc.AddMinutes(15);
        var wordId = await database.RunInTransactionAsync(connection =>
            InsertWord(
                connection,
                canonicalTerm: "Fernweh",
                normalizedTerm: "W:fernweh",
                language: "de",
                tokenKind: TokenKind.Word,
                status: WordStatus.Unreviewed,
                totalOccurrences: 4,
                documentCount: 2,
                createdAt: createdAt,
                updatedAt: updatedAt));

        var service = new VocabularyDetailService(database);
        var lookup = await service.GetVocabularyDetailAsync(wordId);

        Assert.AreEqual(VocabularyDetailLookupStatus.Found, lookup.Status);
        Assert.IsTrue(lookup.IsFound);
        Assert.IsFalse(lookup.IsNotFound);
        Assert.IsFalse(lookup.IsInvalidWordId);
        Assert.AreEqual(wordId, lookup.RequestedWordId);
        Assert.AreEqual(IVocabularyDetailService.DefaultMaxItemsPerCollection, lookup.MaxItemsPerCollection);
        Assert.IsNotNull(lookup.Detail);

        var detail = lookup.Detail;
        Assert.AreEqual(wordId, detail.WordId);
        Assert.AreEqual(wordId, detail.Id);
        Assert.AreEqual("Fernweh", detail.CanonicalTerm);
        Assert.AreEqual("W:fernweh", detail.NormalizedTerm);
        Assert.AreEqual("de", detail.SourceLanguage);
        Assert.AreEqual("de", detail.Language);
        Assert.AreEqual(TokenKind.Word, detail.TokenKind);
        Assert.AreEqual(4, detail.TotalOccurrenceCount);
        Assert.AreEqual(2, detail.DocumentCount);
        Assert.AreEqual(new DateTimeOffset(createdAt, TimeSpan.Zero), detail.CreatedAtUtc);
        Assert.AreEqual(new DateTimeOffset(updatedAt, TimeSpan.Zero), detail.UpdatedAtUtc);
        Assert.IsNull(detail.WordLearningControl);
        Assert.IsFalse(detail.IsMarkedAlreadyKnown);
        Assert.IsEmpty(detail.Senses);
        Assert.IsFalse(detail.HasMoreSenses);
        Assert.IsFalse(detail.HasAnyTruncatedCollection);
        Assert.IsFalse(detail.IsTruncated);
    }

    [TestMethod]
    public async Task MultipleIndependentSensesMeaningsAndContexts_PreserveRelationshipsAndProvenance()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int wordId = 0;
        int sense1Id = 0;
        int sense2Id = 0;
        int meaning1SecondaryId = 0;
        int meaning1PrimaryId = 0;
        int meaning2PrimaryId = 0;
        int context1Id = 0;
        int context2Id = 0;

        await database.RunInTransactionAsync(connection =>
        {
            wordId = InsertWord(
                connection,
                canonicalTerm: "bank",
                normalizedTerm: "W:bank",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 7,
                documentCount: 3,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc.AddMinutes(10));

            sense1Id = InsertSense(
                connection,
                wordId: wordId,
                stableId: "sense-finance",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "wikt-en-bank-1",
                topicOrDomain: "Finance",
                partOfSpeech: "Noun",
                grammaticalRelationship: "countable",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc.AddMinutes(1),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(2));

            meaning1SecondaryId = InsertMeaning(
                connection,
                wordId: wordId,
                senseId: sense1Id,
                stableId: "meaning-finance-secondary",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "bank",
                encounteredSurfaceForm: "banks",
                grammaticalRelationship: "plural of bank",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "user-edit-1",
                acronymExpansion: "",
                definition: "A depository institution offering savings and credit accounts.",
                translation: "Kreditinstitut",
                dictionaryExample: "Savings are held at a depository institution.",
                additionalNote: "User-refined note",
                acceptedAliasesJson: "[\"Geldhaus\",\"Bankhaus\"]",
                source: "",
                sourceProject: "",
                sourcePageTitle: "",
                sourceRevisionId: null,
                attribution: "",
                confirmedByUser: true,
                createdAt: BaseTimeUtc.AddMinutes(2),
                updatedAt: BaseTimeUtc.AddMinutes(3),
                preparedAt: BaseTimeUtc.AddMinutes(3));

            meaning1PrimaryId = InsertMeaning(
                connection,
                wordId: wordId,
                senseId: sense1Id,
                stableId: "meaning-finance-primary",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "bank",
                encounteredSurfaceForm: "bank",
                grammaticalRelationship: "countable",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "wikt-en-bank-1",
                acronymExpansion: "",
                definition: "A financial institution that accepts deposits and channels money into lending.",
                translation: "Bank",
                dictionaryExample: "She deposited the check at the bank.",
                additionalNote: "",
                acceptedAliasesJson: "[\"Geschäftsbank\"]",
                source: "Wiktionary",
                sourceProject: "en.wiktionary.org",
                sourcePageTitle: "bank",
                sourceRevisionId: 101_001L,
                attribution: "Wiktionary contributors (CC BY-SA 4.0)",
                confirmedByUser: true,
                createdAt: BaseTimeUtc.AddMinutes(3),
                updatedAt: BaseTimeUtc.AddMinutes(4),
                preparedAt: BaseTimeUtc.AddMinutes(4));

            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaning1PrimaryId, sense1Id);

            sense2Id = InsertSense(
                connection,
                wordId: wordId,
                stableId: "sense-river",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "wikt-en-bank-2",
                topicOrDomain: "Geography",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc.AddMinutes(5),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(6));

            meaning2PrimaryId = InsertMeaning(
                connection,
                wordId: wordId,
                senseId: sense2Id,
                stableId: "meaning-river-primary",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "bank",
                encounteredSurfaceForm: "bank",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "wikt-en-bank-2",
                acronymExpansion: "",
                definition: "The sloping land beside a river or stream.",
                translation: "Ufer",
                dictionaryExample: "Willows grew along the river bank.",
                additionalNote: "",
                acceptedAliasesJson: "[\"Flussufer\"]",
                source: "Wiktionary",
                sourceProject: "en.wiktionary.org",
                sourcePageTitle: "bank",
                sourceRevisionId: 101_002L,
                attribution: "Wiktionary contributors (CC BY-SA 4.0)",
                confirmedByUser: true,
                createdAt: BaseTimeUtc.AddMinutes(6),
                updatedAt: BaseTimeUtc.AddMinutes(7),
                preparedAt: BaseTimeUtc.AddMinutes(7));

            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaning2PrimaryId, sense2Id);

            context1Id = InsertContextSnapshot(
                connection,
                meaningId: meaning1PrimaryId,
                senseId: sense1Id,
                wordId: wordId,
                sourceDocumentId: 10,
                sourceDocumentTitle: "Central Banking Report",
                text: "The central bank raised interest rates.",
                targetStart: 12,
                targetLength: 4,
                normalizedFingerprint: "fp-central-bank",
                createdAt: BaseTimeUtc.AddMinutes(8));

            context2Id = InsertContextSnapshot(
                connection,
                meaningId: meaning2PrimaryId,
                senseId: sense2Id,
                wordId: wordId,
                sourceDocumentId: 20,
                sourceDocumentTitle: "River Walk Notes",
                text: "Willows grew along the muddy river bank.",
                targetStart: 35,
                targetLength: 4,
                normalizedFingerprint: "fp-river-bank",
                createdAt: BaseTimeUtc.AddMinutes(9));

            return true;
        });

        var service = new VocabularyDetailService(database);
        var lookup = await service.GetVocabularyDetailAsync(wordId);

        Assert.IsTrue(lookup.IsFound);
        var detail = lookup.Detail!;
        Assert.HasCount(2, detail.Senses);
        Assert.IsFalse(detail.HasMoreSenses);

        var firstSense = detail.Senses[0];
        Assert.AreEqual(sense1Id, firstSense.SenseId);
        Assert.AreEqual(wordId, firstSense.WordId);
        Assert.AreEqual("sense-finance", firstSense.StableId);
        Assert.AreEqual("en", firstSense.SourceLanguage);
        Assert.AreEqual("de", firstSense.ExplanationLanguage);
        Assert.AreEqual("wikt-en-bank-1", firstSense.ProviderSenseId);
        Assert.AreEqual("Finance", firstSense.TopicOrDomain);
        Assert.AreEqual("Noun", firstSense.PartOfSpeech);
        Assert.AreEqual("countable", firstSense.GrammaticalRelationship);
        Assert.AreEqual(meaning1PrimaryId, firstSense.DefaultMeaningId);
        Assert.HasCount(2, firstSense.Meanings);
        Assert.IsFalse(firstSense.HasMoreMeanings);

        // Sense's DefaultMeaningId is ordered first, then remaining meanings by Id ASC.
        var primaryFinanceMeaning = firstSense.Meanings[0];
        Assert.AreEqual(meaning1PrimaryId, primaryFinanceMeaning.MeaningId);
        Assert.AreEqual(sense1Id, primaryFinanceMeaning.SenseId);
        Assert.AreEqual(wordId, primaryFinanceMeaning.WordId);
        Assert.AreEqual("meaning-finance-primary", primaryFinanceMeaning.StableId);
        Assert.AreEqual("en", primaryFinanceMeaning.SourceLanguage);
        Assert.AreEqual("de", primaryFinanceMeaning.ExplanationLanguage);
        Assert.AreEqual("bank", primaryFinanceMeaning.DisplayTerm);
        Assert.AreEqual("A financial institution that accepts deposits and channels money into lending.", primaryFinanceMeaning.Definition);
        Assert.AreEqual("Bank", primaryFinanceMeaning.Translation);
        Assert.AreEqual("She deposited the check at the bank.", primaryFinanceMeaning.DictionaryExample);
        CollectionAssert.AreEqual(new[] { "Geschäftsbank" }, primaryFinanceMeaning.AcceptedAliases.ToArray());
        Assert.IsTrue(primaryFinanceMeaning.ConfirmedByUser);
        Assert.IsTrue(primaryFinanceMeaning.HasProviderAttribution);
        Assert.AreEqual("Wiktionary", primaryFinanceMeaning.Source);
        Assert.AreEqual("en.wiktionary.org", primaryFinanceMeaning.SourceProject);
        Assert.AreEqual("bank", primaryFinanceMeaning.SourcePageTitle);
        Assert.AreEqual(101_001L, primaryFinanceMeaning.SourceRevisionId);
        Assert.AreEqual("Wiktionary contributors (CC BY-SA 4.0)", primaryFinanceMeaning.Attribution);

        var secondaryFinanceMeaning = firstSense.Meanings[1];
        Assert.AreEqual(meaning1SecondaryId, secondaryFinanceMeaning.MeaningId);
        Assert.AreEqual("A depository institution offering savings and credit accounts.", secondaryFinanceMeaning.Definition);
        Assert.AreEqual("Kreditinstitut", secondaryFinanceMeaning.Translation);
        Assert.AreEqual("User-refined note", secondaryFinanceMeaning.AdditionalNote);
        CollectionAssert.AreEqual(new[] { "Geldhaus", "Bankhaus" }, secondaryFinanceMeaning.AcceptedAliases.ToArray());
        Assert.IsFalse(secondaryFinanceMeaning.HasProviderAttribution);
        Assert.IsNull(secondaryFinanceMeaning.SourceRevisionId);

        Assert.HasCount(1, firstSense.ContextSnapshots);
        Assert.IsFalse(firstSense.HasMoreContextSnapshots);
        var firstContext = firstSense.ContextSnapshots[0];
        Assert.AreEqual(context1Id, firstContext.ContextSnapshotId);
        Assert.AreEqual(meaning1PrimaryId, firstContext.MeaningId);
        Assert.AreEqual(sense1Id, firstContext.SenseId);
        Assert.AreEqual(wordId, firstContext.WordId);
        Assert.AreEqual(10, firstContext.SourceDocumentId);
        Assert.AreEqual("Central Banking Report", firstContext.SourceDocumentTitle);
        Assert.AreEqual("The central bank raised interest rates.", firstContext.Text);
        Assert.AreEqual(12, firstContext.TargetStart);
        Assert.AreEqual(4, firstContext.TargetLength);
        Assert.AreEqual("fp-central-bank", firstContext.NormalizedFingerprint);

        var secondSense = detail.Senses[1];
        Assert.AreEqual(sense2Id, secondSense.SenseId);
        Assert.AreEqual("sense-river", secondSense.StableId);
        Assert.AreEqual("Geography", secondSense.TopicOrDomain);
        Assert.HasCount(1, secondSense.Meanings);
        Assert.AreEqual(meaning2PrimaryId, secondSense.Meanings[0].MeaningId);
        Assert.AreEqual("The sloping land beside a river or stream.", secondSense.Meanings[0].Definition);
        Assert.AreEqual("Ufer", secondSense.Meanings[0].Translation);

        Assert.HasCount(1, secondSense.ContextSnapshots);
        Assert.AreEqual(context2Id, secondSense.ContextSnapshots[0].ContextSnapshotId);
        Assert.AreEqual("River Walk Notes", secondSense.ContextSnapshots[0].SourceDocumentTitle);
    }

    [TestMethod]
    public async Task DefinitionAndTranslationTargetsUnderOneSense_PreserveIndependentFsrsStateVariantsAndReviewHistory()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int wordId = 0;
        int senseId = 0;
        int meaningId = 0;
        int defTargetId = 0;
        int trnTargetId = 0;
        int defVarAcceptedId = 0;
        int defVarPreferredId = 0;
        int trnVarPreferredId = 0;
        int defHist1Id = 0;
        int defHist2Id = 0;
        int trnHist1Id = 0;

        var defLastReviewed = BaseOffsetUtc.AddDays(-2);
        var defDueAt = BaseOffsetUtc.AddDays(12);
        var trnLastReviewed = BaseOffsetUtc.AddMinutes(-15);
        var trnDueAt = BaseOffsetUtc.AddMinutes(10);
        var requiredSince = BaseOffsetUtc.AddDays(-5);

        await database.RunInTransactionAsync(connection =>
        {
            wordId = InsertWord(
                connection,
                canonicalTerm: "resilience",
                normalizedTerm: "W:resilience",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 3,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            senseId = InsertSense(
                connection,
                wordId: wordId,
                stableId: "sense-resilience-1",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "p-res-1",
                topicOrDomain: "Psychology",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            meaningId = InsertMeaning(
                connection,
                wordId: wordId,
                senseId: senseId,
                stableId: "meaning-resilience-1",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "resilience",
                encounteredSurfaceForm: "resilience",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "p-res-1",
                acronymExpansion: "",
                definition: "Capacity to recover quickly from difficulties.",
                translation: "Widerstandsfähigkeit",
                dictionaryExample: "",
                additionalNote: "",
                acceptedAliasesJson: "[\"Resilienz\"]",
                source: "Wiktionary",
                sourceProject: "en.wiktionary.org",
                sourcePageTitle: "resilience",
                sourceRevisionId: 1L,
                attribution: "Wiktionary",
                confirmedByUser: true,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            defTargetId = InsertLearningTarget(
                connection,
                senseId: senseId,
                stableId: "target-def",
                targetKind: LearningTargetKind.Definition,
                sourceLanguage: "en",
                targetLanguage: "en",
                typingOptOut: true,
                createdAtUtc: BaseOffsetUtc.AddMinutes(1),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(2));

            trnTargetId = InsertLearningTarget(
                connection,
                senseId: senseId,
                stableId: "target-trn",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc.AddMinutes(3),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(4));

            // Insert non-preferred variant first so we verify IsPreferred DESC, Id ASC ordering.
            defVarAcceptedId = InsertAnswerVariant(
                connection,
                targetId: defTargetId,
                stableId: "var-def-accepted",
                answerLanguage: "en",
                displayText: "Toughness under stress",
                normalizedText: "toughness under stress",
                requirement: AnswerVariantRequirement.AcceptedOnly,
                isPreferred: false,
                requiredSinceUtc: null,
                sourceMeaningId: meaningId,
                createdAtUtc: BaseOffsetUtc.AddMinutes(5),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(5));

            defVarPreferredId = InsertAnswerVariant(
                connection,
                targetId: defTargetId,
                stableId: "var-def-preferred",
                answerLanguage: "en",
                displayText: "Capacity to recover quickly from difficulties.",
                normalizedText: "capacity to recover quickly from difficulties",
                requirement: AnswerVariantRequirement.Required,
                isPreferred: true,
                requiredSinceUtc: requiredSince,
                sourceMeaningId: meaningId,
                createdAtUtc: BaseOffsetUtc.AddMinutes(6),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(6));

            trnVarPreferredId = InsertAnswerVariant(
                connection,
                targetId: trnTargetId,
                stableId: "var-trn-preferred",
                answerLanguage: "de",
                displayText: "Widerstandsfähigkeit",
                normalizedText: "widerstandsfähigkeit",
                requirement: AnswerVariantRequirement.Required,
                isPreferred: true,
                requiredSinceUtc: requiredSince,
                sourceMeaningId: meaningId,
                createdAtUtc: BaseOffsetUtc.AddMinutes(7),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(7));

            InsertTargetFsrsState(
                connection,
                targetId: defTargetId,
                state: Fsrs6CardState.Review,
                stability: 14.5,
                difficulty: 4.25,
                lastReviewedAtUtc: defLastReviewed,
                stepIndex: null,
                dueAtUtc: defDueAt);

            InsertTargetFsrsState(
                connection,
                targetId: trnTargetId,
                state: Fsrs6CardState.Relearning,
                stability: 2.75,
                difficulty: 7.1,
                lastReviewedAtUtc: trnLastReviewed,
                stepIndex: 0,
                dueAtUtc: trnDueAt);

            defHist2Id = InsertReviewHistoryEntry(
                connection,
                targetId: defTargetId,
                stableId: "hist-def-2",
                sequenceNumber: 2,
                rating: ReviewRating.Easy,
                reviewedAtUtc: defLastReviewed);

            defHist1Id = InsertReviewHistoryEntry(
                connection,
                targetId: defTargetId,
                stableId: "hist-def-1",
                sequenceNumber: 1,
                rating: ReviewRating.Good,
                reviewedAtUtc: defLastReviewed.AddDays(-4));

            trnHist1Id = InsertReviewHistoryEntry(
                connection,
                targetId: trnTargetId,
                stableId: "hist-trn-1",
                sequenceNumber: 1,
                rating: ReviewRating.Again,
                reviewedAtUtc: trnLastReviewed);

            return true;
        });

        var service = new VocabularyDetailService(database);
        var lookup = await service.GetVocabularyDetailAsync(wordId);

        Assert.IsTrue(lookup.IsFound);
        var sense = lookup.Detail!.Senses.Single();
        Assert.HasCount(2, sense.LearningTargets);
        Assert.IsFalse(sense.HasMoreLearningTargets);

        var defTarget = sense.LearningTargets[0];
        Assert.AreEqual(defTargetId, defTarget.TargetId);
        Assert.AreEqual(senseId, defTarget.SenseId);
        Assert.AreEqual("target-def", defTarget.StableId);
        Assert.AreEqual(LearningTargetKind.Definition, defTarget.TargetKind);
        Assert.AreEqual("en", defTarget.SourceLanguage);
        Assert.AreEqual("en", defTarget.TargetLanguage);
        Assert.IsTrue(defTarget.TypingOptOut);
        Assert.IsTrue(defTarget.IsEligibleForActiveLearning);
        Assert.IsTrue(defTarget.HasFsrsState);
        Assert.IsNotNull(defTarget.FsrsState);
        Assert.AreEqual(defTargetId, defTarget.FsrsState.TargetId);
        Assert.AreEqual(Fsrs6CardState.Review, defTarget.FsrsState.State);
        Assert.AreEqual(14.5, defTarget.FsrsState.Stability!.Value, 1e-6);
        Assert.AreEqual(4.25, defTarget.FsrsState.Difficulty!.Value, 1e-6);
        Assert.AreEqual(defLastReviewed, defTarget.FsrsState.LastReviewedAtUtc);
        Assert.IsNull(defTarget.FsrsState.StepIndex);
        Assert.AreEqual(defDueAt, defTarget.FsrsState.DueAtUtc);

        Assert.HasCount(2, defTarget.AnswerVariants);
        Assert.IsFalse(defTarget.HasMoreAnswerVariants);
        Assert.AreEqual(defVarPreferredId, defTarget.AnswerVariants[0].VariantId);
        Assert.IsTrue(defTarget.AnswerVariants[0].IsPreferred);
        Assert.IsTrue(defTarget.AnswerVariants[0].IsRequired);
        Assert.AreEqual(AnswerVariantRequirement.Required, defTarget.AnswerVariants[0].Requirement);
        Assert.AreEqual(requiredSince, defTarget.AnswerVariants[0].RequiredSinceUtc);
        Assert.AreEqual(meaningId, defTarget.AnswerVariants[0].SourceMeaningId);

        Assert.AreEqual(defVarAcceptedId, defTarget.AnswerVariants[1].VariantId);
        Assert.IsFalse(defTarget.AnswerVariants[1].IsPreferred);
        Assert.IsFalse(defTarget.AnswerVariants[1].IsRequired);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, defTarget.AnswerVariants[1].Requirement);
        Assert.IsNull(defTarget.AnswerVariants[1].RequiredSinceUtc);

        Assert.HasCount(2, defTarget.ReviewHistory);
        Assert.IsFalse(defTarget.HasMoreReviewHistory);
        Assert.AreEqual(defHist1Id, defTarget.ReviewHistory[0].EntryId);
        Assert.AreEqual(1, defTarget.ReviewHistory[0].SequenceNumber);
        Assert.AreEqual(ReviewRating.Good, defTarget.ReviewHistory[0].Rating);
        Assert.AreEqual(defHist2Id, defTarget.ReviewHistory[1].EntryId);
        Assert.AreEqual(2, defTarget.ReviewHistory[1].SequenceNumber);
        Assert.AreEqual(ReviewRating.Easy, defTarget.ReviewHistory[1].Rating);

        var trnTarget = sense.LearningTargets[1];
        Assert.AreEqual(trnTargetId, trnTarget.TargetId);
        Assert.AreEqual("target-trn", trnTarget.StableId);
        Assert.AreEqual(LearningTargetKind.Translation, trnTarget.TargetKind);
        Assert.AreEqual("en", trnTarget.SourceLanguage);
        Assert.AreEqual("de", trnTarget.TargetLanguage);
        Assert.IsFalse(trnTarget.TypingOptOut);
        Assert.IsTrue(trnTarget.HasFsrsState);
        Assert.IsNotNull(trnTarget.FsrsState);
        Assert.AreEqual(Fsrs6CardState.Relearning, trnTarget.FsrsState.State);
        Assert.AreEqual(2.75, trnTarget.FsrsState.Stability!.Value, 1e-6);
        Assert.AreEqual(7.1, trnTarget.FsrsState.Difficulty!.Value, 1e-6);
        Assert.AreEqual(0, trnTarget.FsrsState.StepIndex);
        Assert.AreEqual(trnDueAt, trnTarget.FsrsState.DueAtUtc);

        Assert.HasCount(1, trnTarget.AnswerVariants);
        Assert.AreEqual(trnVarPreferredId, trnTarget.AnswerVariants[0].VariantId);
        Assert.HasCount(1, trnTarget.ReviewHistory);
        Assert.AreEqual(trnHist1Id, trnTarget.ReviewHistory[0].EntryId);
        Assert.AreEqual(ReviewRating.Again, trnTarget.ReviewHistory[0].Rating);
    }

    [TestMethod]
    public async Task ExplicitLearningControlsVersusFsrsState_NeverFabricatesFromWordStatusLegacyCardsOrControls()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int controlledWordId = 0;
        int controlledSenseId = 0;
        int controlledTargetId = 0;
        int legacyWordId = 0;
        int legacySenseId = 0;
        int legacyTargetWithoutStateId = 0;

        var wordControlDecidedAt = BaseOffsetUtc.AddHours(-2);
        var senseControlDecidedAt = BaseOffsetUtc.AddHours(-1);
        var dueAt = BaseOffsetUtc.AddDays(7);

        await database.RunInTransactionAsync(connection =>
        {
            controlledWordId = InsertWord(
                connection,
                canonicalTerm: "orbit",
                normalizedTerm: "W:orbit",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.Unreviewed,
                totalOccurrences: 1,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            controlledSenseId = InsertSense(
                connection,
                wordId: controlledWordId,
                stableId: "sense-orbit",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "",
                topicOrDomain: "",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            controlledTargetId = InsertLearningTarget(
                connection,
                senseId: controlledSenseId,
                stableId: "target-orbit-trn",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertTargetFsrsState(
                connection,
                targetId: controlledTargetId,
                state: Fsrs6CardState.Review,
                stability: 19.0,
                difficulty: 5.0,
                lastReviewedAtUtc: BaseOffsetUtc.AddDays(-3),
                stepIndex: null,
                dueAtUtc: dueAt);

            InsertWordLearningControl(connection, controlledWordId, wordControlDecidedAt);
            InsertSenseLearningControl(connection, controlledSenseId, senseControlDecidedAt);

            // Second word has legacy WordStatus.Known, PreparationState.Prepared, SenseStatus.Prepared,
            // and legacy LearningCards/FsrsCardStates rows, but NO WordLearningControls, SenseLearningControls,
            // or TargetFsrsStates rows.
            legacyWordId = InsertWord(
                connection,
                canonicalTerm: "legacyterm",
                normalizedTerm: "W:legacyterm",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.Known,
                totalOccurrences: 5,
                documentCount: 2,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparationState: PreparationState.Prepared);

            legacySenseId = InsertSense(
                connection,
                wordId: legacyWordId,
                stableId: "sense-legacy",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "",
                topicOrDomain: "",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Prepared,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            var legacyMeaningId = InsertMeaning(
                connection,
                wordId: legacyWordId,
                senseId: legacySenseId,
                stableId: "meaning-legacy",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "legacyterm",
                encounteredSurfaceForm: "legacyterm",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "",
                acronymExpansion: "",
                definition: "",
                translation: "Altbegriff",
                dictionaryExample: "",
                additionalNote: "",
                acceptedAliasesJson: "[]",
                source: "",
                sourceProject: "",
                sourcePageTitle: "",
                sourceRevisionId: null,
                attribution: "",
                confirmedByUser: true,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            legacyTargetWithoutStateId = InsertLearningTarget(
                connection,
                senseId: legacySenseId,
                stableId: "target-legacy-no-state",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            // Legacy LearningCards + FsrsCardStates must be completely ignored.
            connection.Execute(
                """
                INSERT INTO LearningCards
                    (WordId, PreferredMeaningId, SenseId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                     SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, 0, 2, ?, 30, 2.5, 10, 1, ?, ?, ?, ?)
                """,
                legacyWordId,
                legacyMeaningId,
                legacySenseId,
                BaseTimeUtc.AddDays(30),
                BaseTimeUtc,
                (int)ReviewRating.Good,
                BaseTimeUtc,
                BaseTimeUtc);

            var legacyCardId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            var legacyReviewEvent = new Fsrs6ReviewEvent(BaseOffsetUtc, ReviewRating.Good);
            var legacyReplayed = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [legacyReviewEvent]);
            connection.Execute(
                """
                INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('legacy-card-hist-1', ?, 1, ?, ?)
                """,
                legacyCardId,
                (int)legacyReviewEvent.Rating,
                Schema13TimestampCodec.FormatUtc(legacyReviewEvent.ReviewedAtUtc));
            connection.Execute(
                """
                INSERT INTO FsrsCardStates (CardId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                legacyCardId,
                (int)legacyReplayed.State,
                legacyReplayed.Stability,
                legacyReplayed.Difficulty,
                legacyReplayed.LastReviewedAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(legacyReplayed.LastReviewedAtUtc.Value) : null,
                legacyReplayed.StepIndex,
                legacyReplayed.DueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(legacyReplayed.DueAtUtc.Value) : null);

            return true;
        });

        var service = new VocabularyDetailService(database);

        var controlledLookup = await service.GetVocabularyDetailAsync(controlledWordId);
        Assert.IsTrue(controlledLookup.IsFound);
        var controlledDetail = controlledLookup.Detail!;
        Assert.IsTrue(controlledDetail.IsMarkedAlreadyKnown);
        Assert.IsNotNull(controlledDetail.WordLearningControl);
        Assert.AreEqual(controlledWordId, controlledDetail.WordLearningControl.WordId);
        Assert.AreEqual(wordControlDecidedAt, controlledDetail.WordLearningControl.DecidedAtUtc);

        var controlledSense = controlledDetail.Senses.Single();
        Assert.IsTrue(controlledSense.IsStopped);
        Assert.IsNotNull(controlledSense.SenseLearningControl);
        Assert.AreEqual(controlledSenseId, controlledSense.SenseLearningControl.SenseId);
        Assert.AreEqual(senseControlDecidedAt, controlledSense.SenseLearningControl.DecidedAtUtc);

        var controlledTarget = controlledSense.LearningTargets.Single();
        Assert.IsFalse(controlledTarget.IsEligibleForActiveLearning, "Explicit controls suppress active learning eligibility.");
        Assert.IsTrue(controlledTarget.HasFsrsState, "Explicit controls must not erase or overwrite persisted TargetFsrsState.");
        Assert.AreEqual(Fsrs6CardState.Review, controlledTarget.FsrsState!.State);
        Assert.AreEqual(19.0, controlledTarget.FsrsState.Stability!.Value, 1e-6);
        Assert.AreEqual(dueAt, controlledTarget.FsrsState.DueAtUtc);

        var legacyLookup = await service.GetVocabularyDetailAsync(legacyWordId);
        Assert.IsTrue(legacyLookup.IsFound);
        var legacyDetail = legacyLookup.Detail!;
        Assert.IsFalse(legacyDetail.IsMarkedAlreadyKnown, "Words.Status=Known must not fabricate WordLearningControls.");
        Assert.IsNull(legacyDetail.WordLearningControl);

        var legacySense = legacyDetail.Senses.Single();
        Assert.IsFalse(legacySense.IsStopped, "Senses.Status must not fabricate SenseLearningControls.");
        Assert.IsNull(legacySense.SenseLearningControl);

        var legacyTarget = legacySense.LearningTargets.Single();
        Assert.AreEqual(legacyTargetWithoutStateId, legacyTarget.TargetId);
        Assert.IsFalse(legacyTarget.HasFsrsState, "Legacy LearningCards/FsrsCardStates must never fabricate TargetFsrsState.");
        Assert.IsNull(legacyTarget.FsrsState);
        Assert.IsTrue(legacyTarget.IsEligibleForActiveLearning);

        // Verify DTOs do not expose legacy WordStatus, PreparationState, or SenseStatus properties.
        var detailProps = typeof(VocabularyDetail).GetProperties().Select(static p => p.Name).ToArray();
        CollectionAssert.DoesNotContain(detailProps, "Status");
        CollectionAssert.DoesNotContain(detailProps, "WordStatus");
        CollectionAssert.DoesNotContain(detailProps, "PreparationState");

        var senseProps = typeof(VocabularySenseDetail).GetProperties().Select(static p => p.Name).ToArray();
        CollectionAssert.DoesNotContain(senseProps, "Status");
        CollectionAssert.DoesNotContain(senseProps, "SenseStatus");
    }

    [TestMethod]
    public async Task MissingOrIncompletePersistedState_ReturnsHonestNullsAndEmptyListsWithoutInventedFallbacks()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int wordId = 0;
        int senseId = 0;
        int meaningId = 0;
        int targetWithNewStateId = 0;
        int targetWithoutStateId = 0;

        await database.RunInTransactionAsync(connection =>
        {
            wordId = InsertWord(
                connection,
                canonicalTerm: "sparse",
                normalizedTerm: "W:sparse",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 1,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            senseId = InsertSense(
                connection,
                wordId: wordId,
                stableId: "sense-sparse",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "",
                topicOrDomain: "",
                partOfSpeech: "",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            // Insert a Meaning where Definition and Translation are empty, while legacy TranslationOrDefinition
            // contains text and AcceptedAliasesJson is malformed.
            connection.Execute(
                """
                INSERT INTO Meanings
                    (WordId, SenseId, StableId, SourceLanguage, ExplanationLanguage, DisplayTerm, EncounteredSurfaceForm,
                     GrammaticalRelationship, TokenKind, SelectedMeaningId, AcronymExpansion,
                     TranslationOrDefinition, Definition, Translation, DictionaryExample, AdditionalNote,
                     AcceptedAliasesJson, Source, SourceProject, SourcePageTitle, SourceRevisionId,
                     Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
                VALUES (?, ?, 'meaning-sparse', 'en', 'de', 'sparse', 'sparse',
                        '', 0, '', '',
                        'LEGACY-DO-NOT-USE', '', '', '', '',
                        'not-valid-json', '', '', '', NULL,
                        '', 0, ?, ?, ?)
                """,
                wordId,
                senseId,
                BaseTimeUtc,
                BaseTimeUtc,
                BaseTimeUtc);
            meaningId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            targetWithNewStateId = InsertLearningTarget(
                connection,
                senseId: senseId,
                stableId: "target-sparse-new",
                targetKind: LearningTargetKind.Definition,
                sourceLanguage: "en",
                targetLanguage: "en",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertTargetFsrsState(
                connection,
                targetId: targetWithNewStateId,
                state: Fsrs6CardState.New,
                stability: null,
                difficulty: null,
                lastReviewedAtUtc: null,
                stepIndex: null,
                dueAtUtc: null);

            targetWithoutStateId = InsertLearningTarget(
                connection,
                senseId: senseId,
                stableId: "target-sparse-missing-state",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc.AddMinutes(1),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(1));

            return true;
        });

        var service = new VocabularyDetailService(database);
        var lookup = await service.GetVocabularyDetailAsync(wordId);

        Assert.IsTrue(lookup.IsFound);
        var sense = lookup.Detail!.Senses.Single();
        var meaning = sense.Meanings.Single();
        Assert.AreEqual(meaningId, meaning.MeaningId);
        Assert.AreEqual(string.Empty, meaning.Definition, "Must not fabricate Definition from legacy TranslationOrDefinition.");
        Assert.AreEqual(string.Empty, meaning.Translation, "Must not fabricate Translation from legacy TranslationOrDefinition.");
        Assert.IsEmpty(meaning.AcceptedAliases);
        Assert.IsFalse(meaning.HasProviderAttribution);

        Assert.IsEmpty(sense.ContextSnapshots);
        Assert.HasCount(2, sense.LearningTargets);

        var targetWithNewState = sense.LearningTargets[0];
        Assert.AreEqual(targetWithNewStateId, targetWithNewState.TargetId);
        Assert.IsTrue(targetWithNewState.HasFsrsState);
        Assert.IsNotNull(targetWithNewState.FsrsState);
        Assert.AreEqual(Fsrs6CardState.New, targetWithNewState.FsrsState.State);
        Assert.IsNull(targetWithNewState.FsrsState.Stability);
        Assert.IsNull(targetWithNewState.FsrsState.Difficulty);
        Assert.IsNull(targetWithNewState.FsrsState.LastReviewedAtUtc);
        Assert.IsNull(targetWithNewState.FsrsState.StepIndex);
        Assert.IsNull(targetWithNewState.FsrsState.DueAtUtc);
        Assert.IsEmpty(targetWithNewState.AnswerVariants);
        Assert.IsEmpty(targetWithNewState.ReviewHistory);

        var targetWithoutState = sense.LearningTargets[1];
        Assert.AreEqual(targetWithoutStateId, targetWithoutState.TargetId);
        Assert.IsFalse(targetWithoutState.HasFsrsState);
        Assert.IsNull(targetWithoutState.FsrsState);
        Assert.IsEmpty(targetWithoutState.AnswerVariants);
        Assert.IsEmpty(targetWithoutState.ReviewHistory);
    }

    [TestMethod]
    public async Task CrossWordAndCrossSenseIsolation_ExcludesForeignAndMismatchedRows()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int wordAId = 0;
        int wordBId = 0;
        int senseA1Id = 0;
        int senseA2Id = 0;
        int senseB1Id = 0;
        int meaningA1Id = 0;
        int meaningA2Id = 0;
        int meaningB1Id = 0;
        int validContextA1Id = 0;
        int targetA1Id = 0;
        int targetB1Id = 0;

        await database.RunInTransactionAsync(connection =>
        {
            wordAId = InsertWord(
                connection,
                canonicalTerm: "alpha",
                normalizedTerm: "W:alpha",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 2,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            wordBId = InsertWord(
                connection,
                canonicalTerm: "beta",
                normalizedTerm: "W:beta",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 2,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            senseA1Id = InsertSense(
                connection,
                wordId: wordAId,
                stableId: "sense-a1",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "",
                topicOrDomain: "A1",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            senseA2Id = InsertSense(
                connection,
                wordId: wordAId,
                stableId: "sense-a2",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "",
                topicOrDomain: "A2",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc.AddMinutes(1),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(1));

            senseB1Id = InsertSense(
                connection,
                wordId: wordBId,
                stableId: "sense-b1",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "",
                topicOrDomain: "B1",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc.AddMinutes(2),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(2));

            meaningA1Id = InsertMeaning(
                connection,
                wordId: wordAId,
                senseId: senseA1Id,
                stableId: "meaning-a1",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "alpha",
                encounteredSurfaceForm: "alpha",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "",
                acronymExpansion: "",
                definition: "Alpha sense 1",
                translation: "Alpha 1",
                dictionaryExample: "",
                additionalNote: "",
                acceptedAliasesJson: "[]",
                source: "",
                sourceProject: "",
                sourcePageTitle: "",
                sourceRevisionId: null,
                attribution: "",
                confirmedByUser: true,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            meaningA2Id = InsertMeaning(
                connection,
                wordId: wordAId,
                senseId: senseA2Id,
                stableId: "meaning-a2",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "alpha",
                encounteredSurfaceForm: "alpha",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "",
                acronymExpansion: "",
                definition: "Alpha sense 2",
                translation: "Alpha 2",
                dictionaryExample: "",
                additionalNote: "",
                acceptedAliasesJson: "[]",
                source: "",
                sourceProject: "",
                sourcePageTitle: "",
                sourceRevisionId: null,
                attribution: "",
                confirmedByUser: true,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            meaningB1Id = InsertMeaning(
                connection,
                wordId: wordBId,
                senseId: senseB1Id,
                stableId: "meaning-b1",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "beta",
                encounteredSurfaceForm: "beta",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "",
                acronymExpansion: "",
                definition: "Beta sense 1",
                translation: "Beta 1",
                dictionaryExample: "",
                additionalNote: "",
                acceptedAliasesJson: "[]",
                source: "",
                sourceProject: "",
                sourcePageTitle: "",
                sourceRevisionId: null,
                attribution: "",
                confirmedByUser: true,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            validContextA1Id = InsertContextSnapshot(
                connection,
                meaningId: meaningA1Id,
                senseId: senseA1Id,
                wordId: wordAId,
                sourceDocumentId: 1,
                sourceDocumentTitle: "Doc A1",
                text: "Valid alpha context.",
                targetStart: 6,
                targetLength: 5,
                normalizedFingerprint: "fp-valid-a1",
                createdAt: BaseTimeUtc);

            // ContextSnapshot belonging to Word B.
            InsertContextSnapshot(
                connection,
                meaningId: meaningB1Id,
                senseId: senseB1Id,
                wordId: wordBId,
                sourceDocumentId: 2,
                sourceDocumentTitle: "Doc B1",
                text: "Beta context.",
                targetStart: 0,
                targetLength: 4,
                normalizedFingerprint: "fp-b1",
                createdAt: BaseTimeUtc);

            targetA1Id = InsertLearningTarget(
                connection,
                senseId: senseA1Id,
                stableId: "target-a1",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            targetB1Id = InsertLearningTarget(
                connection,
                senseId: senseB1Id,
                stableId: "target-b1",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertAnswerVariant(
                connection,
                targetId: targetA1Id,
                stableId: "var-a1",
                answerLanguage: "de",
                displayText: "Alpha 1",
                normalizedText: "alpha 1",
                requirement: AnswerVariantRequirement.Required,
                isPreferred: true,
                requiredSinceUtc: BaseOffsetUtc,
                sourceMeaningId: meaningA1Id,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertAnswerVariant(
                connection,
                targetId: targetB1Id,
                stableId: "var-b1",
                answerLanguage: "de",
                displayText: "Beta 1",
                normalizedText: "beta 1",
                requirement: AnswerVariantRequirement.Required,
                isPreferred: true,
                requiredSinceUtc: BaseOffsetUtc,
                sourceMeaningId: meaningB1Id,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertWordLearningControl(connection, wordBId, BaseOffsetUtc);
            InsertSenseLearningControl(connection, senseB1Id, BaseOffsetUtc);

            return true;
        });

        var service = new VocabularyDetailService(database);
        var lookupA = await service.GetVocabularyDetailAsync(wordAId);

        Assert.IsTrue(lookupA.IsFound);
        var detailA = lookupA.Detail!;
        Assert.AreEqual(wordAId, detailA.WordId);
        Assert.IsFalse(detailA.IsMarkedAlreadyKnown, "Word B control must not leak to Word A.");
        CollectionAssert.AreEqual(
            new[] { senseA1Id, senseA2Id },
            detailA.Senses.Select(static s => s.SenseId).ToArray());

        var senseA1 = detailA.Senses[0];
        Assert.IsFalse(senseA1.IsStopped);
        Assert.AreEqual(meaningA1Id, senseA1.Meanings.Single().MeaningId);
        Assert.AreEqual(validContextA1Id, senseA1.ContextSnapshots.Single().ContextSnapshotId);
        Assert.AreEqual(targetA1Id, senseA1.LearningTargets.Single().TargetId);
        Assert.AreEqual("Alpha 1", senseA1.LearningTargets.Single().AnswerVariants.Single().DisplayText);

        var senseA2 = detailA.Senses[1];
        Assert.AreEqual(meaningA2Id, senseA2.Meanings.Single().MeaningId);
        Assert.IsEmpty(senseA2.ContextSnapshots);
        Assert.IsEmpty(senseA2.LearningTargets);

        // Also verify defense-in-depth SQL ownership joins when anomalous cross-word/cross-sense rows exist.
        var snapshotWithAnomalousRows = await database.RunInTransactionAsync(connection =>
        {
            InsertMeaning(
                connection,
                wordId: wordAId,
                senseId: senseB1Id,
                stableId: "meaning-cross-word-mismatch",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "alpha",
                encounteredSurfaceForm: "alpha",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "",
                acronymExpansion: "",
                definition: "Mismatched meaning",
                translation: "Mismatch",
                dictionaryExample: "",
                additionalNote: "",
                acceptedAliasesJson: "[]",
                source: "",
                sourceProject: "",
                sourcePageTitle: "",
                sourceRevisionId: null,
                attribution: "",
                confirmedByUser: false,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            InsertContextSnapshot(
                connection,
                meaningId: meaningA2Id,
                senseId: senseA1Id,
                wordId: wordAId,
                sourceDocumentId: 1,
                sourceDocumentTitle: "Doc Cross Sense",
                text: "Mismatched sense context.",
                targetStart: 0,
                targetLength: 5,
                normalizedFingerprint: "fp-cross-sense",
                createdAt: BaseTimeUtc);

            return VocabularyDetailService.LoadDetailSnapshot(connection, wordAId, 30);
        });

        Assert.IsTrue(snapshotWithAnomalousRows.IsFound);
        Assert.AreEqual(meaningA1Id, snapshotWithAnomalousRows.Detail!.Senses[0].Meanings.Single().MeaningId);
        Assert.AreEqual(validContextA1Id, snapshotWithAnomalousRows.Detail.Senses[0].ContextSnapshots.Single().ContextSnapshotId);
        Assert.AreEqual(meaningA2Id, snapshotWithAnomalousRows.Detail.Senses[1].Meanings.Single().MeaningId);
        Assert.IsEmpty(snapshotWithAnomalousRows.Detail.Senses[1].ContextSnapshots);
    }

    [TestMethod]
    public async Task DeterministicBoundedCollections_EnforceLimitsAndSurfaceHonestOverflowIndicators()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        int manySensesWordId = 0;
        int richWordId = 0;
        int richSenseId = 0;
        int richTargetId = 0;

        await database.RunInTransactionAsync(connection =>
        {
            manySensesWordId = InsertWord(
                connection,
                canonicalTerm: "polysemy",
                normalizedTerm: "W:polysemy",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.Unreviewed,
                totalOccurrences: 1,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            for (var i = 1; i <= 32; i++)
            {
                InsertSense(
                    connection,
                    wordId: manySensesWordId,
                    stableId: $"sense-poly-{i:D2}",
                    sourceLanguage: "en",
                    explanationLanguage: "de",
                    providerSenseId: $"p-{i:D2}",
                    topicOrDomain: $"Domain-{i:D2}",
                    partOfSpeech: "Noun",
                    grammaticalRelationship: "",
                    acronymExpansion: "",
                    defaultMeaningId: null,
                    status: SenseStatus.Learning,
                    createdAtUtc: BaseOffsetUtc.AddMinutes(i),
                    updatedAtUtc: BaseOffsetUtc.AddMinutes(i));
            }

            richWordId = InsertWord(
                connection,
                canonicalTerm: "bounded",
                normalizedTerm: "W:bounded",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 1,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            richSenseId = InsertSense(
                connection,
                wordId: richWordId,
                stableId: "sense-bounded-1",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "",
                topicOrDomain: "",
                partOfSpeech: "Adjective",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            int firstMeaningId = 0;
            for (var i = 1; i <= 3; i++)
            {
                var mId = InsertMeaning(
                    connection,
                    wordId: richWordId,
                    senseId: richSenseId,
                    stableId: $"meaning-bounded-{i}",
                    sourceLanguage: "en",
                    explanationLanguage: "de",
                    displayTerm: "bounded",
                    encounteredSurfaceForm: "bounded",
                    grammaticalRelationship: "",
                    tokenKind: TokenKind.Word,
                    selectedMeaningId: "",
                    acronymExpansion: "",
                    definition: $"Def {i}",
                    translation: $"Trn {i}",
                    dictionaryExample: "",
                    additionalNote: "",
                    acceptedAliasesJson: "[]",
                    source: "",
                    sourceProject: "",
                    sourcePageTitle: "",
                    sourceRevisionId: null,
                    attribution: "",
                    confirmedByUser: true,
                    createdAt: BaseTimeUtc.AddMinutes(i),
                    updatedAt: BaseTimeUtc.AddMinutes(i),
                    preparedAt: BaseTimeUtc.AddMinutes(i));

                if (i == 1)
                {
                    firstMeaningId = mId;
                    connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", firstMeaningId, richSenseId);
                }

                InsertContextSnapshot(
                    connection,
                    meaningId: firstMeaningId,
                    senseId: richSenseId,
                    wordId: richWordId,
                    sourceDocumentId: i,
                    sourceDocumentTitle: $"Doc {i}",
                    text: $"Bounded context {i}.",
                    targetStart: 0,
                    targetLength: 7,
                    normalizedFingerprint: $"fp-bounded-{i}",
                    createdAt: BaseTimeUtc.AddMinutes(i));
            }

            // Schema 13/14 allows at most one Definition and one Translation target per Sense
            // (UNIQUE (SenseId, TargetKind)), so insert both valid kinds (2 targets total).
            richTargetId = InsertLearningTarget(
                connection,
                senseId: richSenseId,
                stableId: "target-bounded-def",
                targetKind: LearningTargetKind.Definition,
                sourceLanguage: "en",
                targetLanguage: "en",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertLearningTarget(
                connection,
                senseId: richSenseId,
                stableId: "target-bounded-trn",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc.AddMinutes(1),
                updatedAtUtc: BaseOffsetUtc.AddMinutes(1));

            for (var i = 1; i <= 3; i++)
            {
                InsertAnswerVariant(
                    connection,
                    targetId: richTargetId,
                    stableId: $"var-bounded-{i}",
                    answerLanguage: "en",
                    displayText: $"Variant {i}",
                    normalizedText: $"variant {i}",
                    requirement: AnswerVariantRequirement.Required,
                    isPreferred: i == 1,
                    requiredSinceUtc: BaseOffsetUtc,
                    sourceMeaningId: firstMeaningId,
                    createdAtUtc: BaseOffsetUtc.AddMinutes(i),
                    updatedAtUtc: BaseOffsetUtc.AddMinutes(i));

                InsertReviewHistoryEntry(
                    connection,
                    targetId: richTargetId,
                    stableId: $"hist-bounded-{i}",
                    sequenceNumber: i,
                    rating: ReviewRating.Good,
                    reviewedAtUtc: BaseOffsetUtc.AddMinutes(i));
            }

            return true;
        });

        var service = new VocabularyDetailService(database);

        // 1. Default limit (30) on 32 senses -> capped at 30 with HasMoreSenses = true.
        // Passing 999 also clamps to MaxItemsPerCollection (30).
        var manySensesLookup = await service.GetVocabularyDetailAsync(manySensesWordId, maxItemsPerCollection: 999);
        Assert.IsTrue(manySensesLookup.IsFound);
        Assert.AreEqual(IVocabularyDetailService.MaxItemsPerCollection, manySensesLookup.MaxItemsPerCollection);
        var manySensesDetail = manySensesLookup.Detail!;
        Assert.HasCount(30, manySensesDetail.Senses);
        Assert.IsTrue(manySensesDetail.HasMoreSenses);
        Assert.IsTrue(manySensesDetail.HasAnyTruncatedCollection);
        Assert.IsTrue(manySensesDetail.IsTruncated);
        Assert.AreEqual("sense-poly-01", manySensesDetail.Senses[0].StableId);
        Assert.AreEqual("sense-poly-30", manySensesDetail.Senses[29].StableId);

        // 2. Custom limit = 1 on richWordId -> truncates Meanings (3 -> 1), Contexts (3 -> 1),
        // LearningTargets (2 -> 1), AnswerVariants (3 -> 1), and ReviewHistory (3 -> 1).
        var limitedByOneLookup = await service.GetVocabularyDetailAsync(richWordId, maxItemsPerCollection: 1);
        Assert.IsTrue(limitedByOneLookup.IsFound);
        Assert.AreEqual(1, limitedByOneLookup.MaxItemsPerCollection);
        var limitedSense = limitedByOneLookup.Detail!.Senses.Single();
        Assert.HasCount(1, limitedSense.Meanings);
        Assert.IsTrue(limitedSense.HasMoreMeanings);
        Assert.HasCount(1, limitedSense.ContextSnapshots);
        Assert.IsTrue(limitedSense.HasMoreContextSnapshots);
        Assert.HasCount(1, limitedSense.LearningTargets);
        Assert.IsTrue(limitedSense.HasMoreLearningTargets);
        Assert.IsTrue(limitedSense.HasAnyTruncatedCollection);

        var limitedTarget = limitedSense.LearningTargets.Single();
        Assert.AreEqual(richTargetId, limitedTarget.TargetId);
        Assert.HasCount(1, limitedTarget.AnswerVariants);
        Assert.IsTrue(limitedTarget.HasMoreAnswerVariants);
        Assert.AreEqual("Variant 1", limitedTarget.AnswerVariants[0].DisplayText);
        Assert.HasCount(1, limitedTarget.ReviewHistory);
        Assert.IsTrue(limitedTarget.HasMoreReviewHistory);
        Assert.AreEqual(1, limitedTarget.ReviewHistory[0].SequenceNumber);
        Assert.IsTrue(limitedTarget.HasAnyTruncatedCollection);
        Assert.IsTrue(limitedByOneLookup.Detail.HasAnyTruncatedCollection);

        // 3. Custom limit = 3 on richWordId -> exact match (3 Meanings, 3 Contexts, 2 Targets, 3 Variants, 3 History)
        // must report HasMore* = false across all collections.
        var exactThreeLookup = await service.GetVocabularyDetailAsync(richWordId, maxItemsPerCollection: 3);
        Assert.IsTrue(exactThreeLookup.IsFound);
        var exactThreeSense = exactThreeLookup.Detail!.Senses.Single();
        Assert.HasCount(3, exactThreeSense.Meanings);
        Assert.IsFalse(exactThreeSense.HasMoreMeanings);
        Assert.HasCount(3, exactThreeSense.ContextSnapshots);
        Assert.IsFalse(exactThreeSense.HasMoreContextSnapshots);
        Assert.HasCount(2, exactThreeSense.LearningTargets);
        Assert.IsFalse(exactThreeSense.HasMoreLearningTargets);
        Assert.HasCount(3, exactThreeSense.LearningTargets[0].AnswerVariants);
        Assert.IsFalse(exactThreeSense.LearningTargets[0].HasMoreAnswerVariants);
        Assert.HasCount(3, exactThreeSense.LearningTargets[0].ReviewHistory);
        Assert.IsFalse(exactThreeSense.LearningTargets[0].HasMoreReviewHistory);
        Assert.IsFalse(exactThreeLookup.Detail.HasAnyTruncatedCollection);
    }

    [TestMethod]
    public async Task ReadOperations_UseSingleSnapshotAndLeaveDatabaseContentsUnchanged()
    {
        await using var innerDb = new ProductionInitializedDatabase();
        await innerDb.InitializeAsync();

        int wordId = 0;
        await innerDb.RunInTransactionAsync(connection =>
        {
            wordId = InsertWord(
                connection,
                canonicalTerm: "immutable",
                normalizedTerm: "W:immutable",
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 3,
                documentCount: 2,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            var senseId = InsertSense(
                connection,
                wordId: wordId,
                stableId: "sense-immutable",
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "p-imm",
                topicOrDomain: "Computer Science",
                partOfSpeech: "Adjective",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            var meaningId = InsertMeaning(
                connection,
                wordId: wordId,
                senseId: senseId,
                stableId: "meaning-immutable",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: "immutable",
                encounteredSurfaceForm: "immutable",
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "p-imm",
                acronymExpansion: "",
                definition: "Unchanging over time or unable to be changed.",
                translation: "unveränderlich",
                dictionaryExample: "",
                additionalNote: "",
                acceptedAliasesJson: "[\"konstant\"]",
                source: "Wiktionary",
                sourceProject: "en.wiktionary.org",
                sourcePageTitle: "immutable",
                sourceRevisionId: 42L,
                attribution: "Wiktionary",
                confirmedByUser: true,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            InsertContextSnapshot(
                connection,
                meaningId: meaningId,
                senseId: senseId,
                wordId: wordId,
                sourceDocumentId: 1,
                sourceDocumentTitle: "Architecture Spec",
                text: "DTO projections are immutable.",
                targetStart: 20,
                targetLength: 9,
                normalizedFingerprint: "fp-immutable",
                createdAt: BaseTimeUtc);

            var targetId = InsertLearningTarget(
                connection,
                senseId: senseId,
                stableId: "target-immutable",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertAnswerVariant(
                connection,
                targetId: targetId,
                stableId: "var-immutable",
                answerLanguage: "de",
                displayText: "unveränderlich",
                normalizedText: "unveränderlich",
                requirement: AnswerVariantRequirement.Required,
                isPreferred: true,
                requiredSinceUtc: BaseOffsetUtc,
                sourceMeaningId: meaningId,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            InsertTargetFsrsState(
                connection,
                targetId: targetId,
                state: Fsrs6CardState.Review,
                stability: 10.0,
                difficulty: 5.0,
                lastReviewedAtUtc: BaseOffsetUtc,
                stepIndex: null,
                dueAtUtc: BaseOffsetUtc.AddDays(10));

            InsertReviewHistoryEntry(
                connection,
                targetId: targetId,
                stableId: "hist-immutable-1",
                sequenceNumber: 1,
                rating: ReviewRating.Good,
                reviewedAtUtc: BaseOffsetUtc);

            InsertWordLearningControl(connection, wordId, BaseOffsetUtc);
            InsertSenseLearningControl(connection, senseId, BaseOffsetUtc);

            return true;
        });

        var beforeDigest = await CaptureDatabaseDigestAsync(innerDb);

        var countingDb = new SnapshotCountingDatabase(innerDb);
        var service = new VocabularyDetailService(countingDb);

        var lookup = await service.GetVocabularyDetailAsync(wordId);
        Assert.IsTrue(lookup.IsFound);
        Assert.AreEqual(1, countingDb.SnapshotCount, "GetVocabularyDetailAsync must execute within a single database snapshot.");
        Assert.AreEqual(0, countingDb.WriteTransactionCount, "GetVocabularyDetailAsync must never open a write transaction.");

        var afterDigest = await CaptureDatabaseDigestAsync(innerDb);
        CollectionAssert.AreEqual(beforeDigest, afterDigest, "Read operations must leave all database tables completely unchanged.");
    }

    [TestMethod]
    public async Task DependencyInjection_MauiProgramRegistersVocabularyDetailServiceAsSingleton()
    {
        var services = new ServiceCollection();
        await using var database = new ProductionInitializedDatabase();
        services.AddSingleton<IKnownFirstDatabase>(database);
        services.AddSingleton<IVocabularyDetailService, VocabularyDetailService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var first = provider.GetRequiredService<IVocabularyDetailService>();
        var second = provider.GetRequiredService<IVocabularyDetailService>();
        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.IsInstanceOfType<VocabularyDetailService>(first);

        var repoRoot = FindRepositoryRoot();
        var mauiProgramSource = File.ReadAllText(Path.Combine(repoRoot, "MauiProgram.cs"));
        StringAssert.Contains(
            mauiProgramSource,
            "builder.Services.AddSingleton<IVocabularyDetailService, VocabularyDetailService>();");
    }

    private static async Task<string[]> CaptureDatabaseDigestAsync(IKnownFirstDatabase database)
    {
        return await database.ExecuteSnapshotAsync(connection =>
        {
            var tables = new[]
            {
                "Words",
                "Senses",
                "Meanings",
                "ContextSnapshots",
                "LearningTargets",
                "TargetAnswerVariants",
                "TargetFsrsStates",
                "TargetFsrsReviewHistoryEntries",
                "WordLearningControls",
                "SenseLearningControls"
            };

            var digest = new List<string>(tables.Length * 2);
            foreach (var table in tables)
            {
                var count = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");
                digest.Add($"{table}:count={count}");
            }

            var wordUpdatedAt = connection.ExecuteScalar<long>("SELECT UpdatedAt FROM Words LIMIT 1");
            var senseUpdatedAt = connection.ExecuteScalar<string>("SELECT UpdatedAtUtc FROM Senses LIMIT 1");
            var targetDueAt = connection.ExecuteScalar<string>("SELECT DueAtUtc FROM TargetFsrsStates LIMIT 1");
            digest.Add($"wordUpdatedAt={wordUpdatedAt}");
            digest.Add($"senseUpdatedAt={senseUpdatedAt}");
            digest.Add($"targetDueAt={targetDueAt}");

            return digest.ToArray();
        });
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

    private sealed class FaultingDatabase(Exception exception) : IKnownFirstDatabase
    {
        public string DatabasePath => ":memory:";

        public Task InitializeAsync() => Task.FromException(exception);

        public Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation) =>
            Task.FromException<T>(exception);

        public Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation) =>
            Task.FromException<T>(exception);

        public Task ResetAsync() => Task.FromException(exception);

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation) =>
            Task.FromException<T>(exception);
    }

    private static int InsertWord(
        SQLiteConnection connection,
        string canonicalTerm,
        string normalizedTerm,
        string language,
        TokenKind tokenKind,
        WordStatus status,
        int totalOccurrences,
        int documentCount,
        DateTime createdAt,
        DateTime updatedAt,
        PreparationState preparationState = PreparationState.Unprepared)
    {
        connection.Execute(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, 0, 0, 0, 0, ?, ?)
            """,
            language,
            canonicalTerm,
            normalizedTerm,
            (int)status,
            (int)tokenKind,
            (int)preparationState,
            totalOccurrences,
            documentCount,
            createdAt,
            updatedAt);

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertSense(
        SQLiteConnection connection,
        int wordId,
        string stableId,
        string sourceLanguage,
        string explanationLanguage,
        string providerSenseId,
        string topicOrDomain,
        string partOfSpeech,
        string grammaticalRelationship,
        string acronymExpansion,
        int? defaultMeaningId,
        SenseStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO Senses
                (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                 PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId,
            wordId,
            sourceLanguage,
            explanationLanguage,
            providerSenseId,
            topicOrDomain,
            partOfSpeech,
            grammaticalRelationship,
            acronymExpansion,
            defaultMeaningId,
            (int)status,
            Schema13TimestampCodec.FormatUtc(createdAtUtc),
            Schema13TimestampCodec.FormatUtc(updatedAtUtc));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertMeaning(
        SQLiteConnection connection,
        int wordId,
        int senseId,
        string stableId,
        string sourceLanguage,
        string explanationLanguage,
        string displayTerm,
        string encounteredSurfaceForm,
        string grammaticalRelationship,
        TokenKind tokenKind,
        string selectedMeaningId,
        string acronymExpansion,
        string definition,
        string translation,
        string dictionaryExample,
        string additionalNote,
        string acceptedAliasesJson,
        string source,
        string sourceProject,
        string sourcePageTitle,
        long? sourceRevisionId,
        string attribution,
        bool confirmedByUser,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime preparedAt)
    {
        connection.Execute(
            """
            INSERT INTO Meanings
                (WordId, SenseId, StableId, SourceLanguage, ExplanationLanguage, DisplayTerm, EncounteredSurfaceForm,
                 GrammaticalRelationship, TokenKind, SelectedMeaningId, AcronymExpansion,
                 TranslationOrDefinition, Definition, Translation, DictionaryExample, AdditionalNote,
                 AcceptedAliasesJson, Source, SourceProject, SourcePageTitle, SourceRevisionId,
                 Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, '', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            wordId,
            senseId,
            stableId,
            sourceLanguage,
            explanationLanguage,
            displayTerm,
            encounteredSurfaceForm,
            grammaticalRelationship,
            (int)tokenKind,
            selectedMeaningId,
            acronymExpansion,
            definition,
            translation,
            dictionaryExample,
            additionalNote,
            acceptedAliasesJson,
            source,
            sourceProject,
            sourcePageTitle,
            sourceRevisionId,
            attribution,
            confirmedByUser ? 1 : 0,
            createdAt,
            updatedAt,
            preparedAt);

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertContextSnapshot(
        SQLiteConnection connection,
        int meaningId,
        int senseId,
        int wordId,
        int sourceDocumentId,
        string sourceDocumentTitle,
        string text,
        int targetStart,
        int targetLength,
        string normalizedFingerprint,
        DateTime createdAt)
    {
        connection.Execute(
            """
            INSERT OR IGNORE INTO Documents
                (Id, Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage,
                 Content, ContentFingerprint, ImportedAt, WordCount)
            VALUES (?, ?, 'en', 'de', 0, 'de', ?, ?, ?, 5)
            """,
            sourceDocumentId,
            sourceDocumentTitle,
            text,
            $"doc-fp-{sourceDocumentId}",
            createdAt);

        connection.Execute(
            """
            INSERT INTO ContextSnapshots
                (MeaningId, SenseId, WordId, SourceDocumentId, SourceDocumentTitle, Text,
                 TargetStart, TargetLength, NormalizedFingerprint, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            meaningId,
            senseId,
            wordId,
            sourceDocumentId,
            sourceDocumentTitle,
            text,
            targetStart,
            targetLength,
            normalizedFingerprint,
            createdAt);

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertLearningTarget(
        SQLiteConnection connection,
        int senseId,
        string stableId,
        LearningTargetKind targetKind,
        string sourceLanguage,
        string targetLanguage,
        bool typingOptOut,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO LearningTargets
                (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId,
            senseId,
            (int)targetKind,
            sourceLanguage,
            targetLanguage,
            typingOptOut ? 1 : 0,
            Schema13TimestampCodec.FormatUtc(createdAtUtc),
            Schema13TimestampCodec.FormatUtc(updatedAtUtc));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertAnswerVariant(
        SQLiteConnection connection,
        int targetId,
        string stableId,
        string answerLanguage,
        string displayText,
        string normalizedText,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTimeOffset? requiredSinceUtc,
        int? sourceMeaningId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO TargetAnswerVariants
                (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred,
                 RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId,
            targetId,
            answerLanguage,
            displayText,
            normalizedText,
            (int)requirement,
            isPreferred ? 1 : 0,
            requiredSinceUtc.HasValue ? Schema13TimestampCodec.FormatUtc(requiredSinceUtc.Value) : null,
            sourceMeaningId,
            Schema13TimestampCodec.FormatUtc(createdAtUtc),
            Schema13TimestampCodec.FormatUtc(updatedAtUtc));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static void InsertTargetFsrsState(
        SQLiteConnection connection,
        int targetId,
        Fsrs6CardState state,
        double? stability,
        double? difficulty,
        DateTimeOffset? lastReviewedAtUtc,
        int? stepIndex,
        DateTimeOffset? dueAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO TargetFsrsStates
                (TargetId, State, Stability, Difficulty, LastReviewedAtUtc, StepIndex, DueAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            targetId,
            (int)state,
            stability,
            difficulty,
            lastReviewedAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(lastReviewedAtUtc.Value) : null,
            stepIndex,
            dueAtUtc.HasValue ? Schema13TimestampCodec.FormatUtc(dueAtUtc.Value) : null);
    }

    private static int InsertReviewHistoryEntry(
        SQLiteConnection connection,
        int targetId,
        string stableId,
        int sequenceNumber,
        ReviewRating rating,
        DateTimeOffset reviewedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO TargetFsrsReviewHistoryEntries
                (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
            VALUES (?, ?, ?, ?, ?)
            """,
            stableId,
            targetId,
            sequenceNumber,
            (int)rating,
            Schema13TimestampCodec.FormatUtc(reviewedAtUtc));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static void InsertWordLearningControl(
        SQLiteConnection connection,
        int wordId,
        DateTimeOffset decidedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO WordLearningControls (WordId, DecidedAtUtc)
            VALUES (?, ?)
            """,
            wordId,
            Schema13TimestampCodec.FormatUtc(decidedAtUtc));
    }

    private static void InsertSenseLearningControl(
        SQLiteConnection connection,
        int senseId,
        DateTimeOffset decidedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO SenseLearningControls (SenseId, DecidedAtUtc)
            VALUES (?, ?)
            """,
            senseId,
            Schema13TimestampCodec.FormatUtc(decidedAtUtc));
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
