using System.Security.Cryptography;
using System.Text;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

internal static class BackupTestData
{
    internal static readonly DateTime UtcTime =
        new(2030, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);

    public static BackupPayload CreateMaximumPayload()
    {
        const string sourceText = "Über Security schützt café.\r\nΣυνέχεια\n";
        var surfaceStart = sourceText.IndexOf("Security", StringComparison.Ordinal);
        var source = new BackupSourceMaterial(
            "source-1",
            "Unicode source",
            "en",
            "de",
            BackupLexicalLookupMode.DefinitionAndTranslation,
            "de",
            sourceText,
            Sha256(sourceText),
            UtcTime,
            1,
            [new BackupSentenceRange("sentence-1", 0, 0, sourceText.Length)],
            [new BackupOccurrence(
                "vocabulary-1",
                "sentence-1",
                surfaceStart,
                "Security".Length,
                "Security",
                0,
                BackupTechnicalTokenFamily.None,
                null,
                null,
                null)]);

        var vocabulary = new BackupVocabularyItem(
            "vocabulary-1",
            "en",
            "security",
            "security",
            BackupTokenKind.Word,
            BackupKnowledgeState.Learning,
            BackupPreparationState.Prepared,
            1,
            1,
            UtcTime.AddDays(-10),
            UtcTime,
            [new BackupEncounteredForm("Security", 1)],
            new BackupAutomaticLearningState(
                BackupLearningInteractionMode.Typing,
                2,
                3,
                1,
                true),
            [new BackupLegacyReviewSummary(4, 1, 1, 2, UtcTime.AddDays(-1))]);

        var sourceReference = new BackupSourceReference(
            "Wiktionary",
            "English Wiktionary",
            "security",
            123456789,
            "CC BY-SA");
        var prepared = new BackupPreparedItem(
            "prepared-1",
            "vocabulary-1",
            "en",
            "de",
            "security",
            "Security",
            "direct sense",
            BackupTokenKind.Word,
            "meaning-1",
            "Information Security",
            "Sicherheit",
            "Protection from danger or loss.",
            "Security protects data.\nSecond retained line.",
            "Notiz mit Umlaut äöü.",
            "legacy answer",
            ["security", "safety"],
            true,
            sourceReference,
            UtcTime.AddDays(-2),
            UtcTime.AddDays(-1),
            UtcTime,
            [new BackupContextSnapshot(
                "source-1",
                "Unicode source",
                sourceText,
                surfaceStart,
                "Security".Length,
                "über security schützt café. συνέχεια",
                UtcTime)]);

        var card = new BackupLearningCard(
            "card-1",
            "vocabulary-1",
            "prepared-1",
            BackupCardDirection.MeaningToTerm,
            BackupCardState.Review,
            UtcTime.AddDays(3),
            3,
            2.5,
            4,
            1,
            UtcTime,
            BackupReviewRating.Good,
            UtcTime.AddDays(-2),
            UtcTime);
        var learningReview = new BackupLearningReview(
            "card-1",
            "learning-workflow-1",
            BackupReviewRating.Good,
            true,
            true,
            UtcTime,
            UtcTime.AddDays(3),
            3,
            2.5);

        var reviewWorkflow = new BackupVocabularyReviewWorkflow(
            "review-workflow-1",
            "source-1",
            BackupReviewSessionStatus.Completed,
            1,
            1,
            0,
            1,
            0,
            1,
            UtcTime.AddDays(-5),
            UtcTime.AddDays(-4),
            [new BackupVocabularyReviewItem(
                "review-item-1",
                "vocabulary-1",
                0,
                BackupKnowledgeState.UnknownBacklog,
                BackupKnowledgeState.Unreviewed,
                0,
                0,
                UtcTime.AddDays(-5),
                1,
                true,
                UtcTime.AddDays(-4))]);

        var lookupDraft = new BackupLookupDraft(
            BackupLexicalLookupStatus.Success,
            BackupLexicalLookupMode.DefinitionAndTranslation,
            "en",
            "de",
            "de",
            "systems",
            "systems",
            BackupTokenKind.Word,
            null,
            "systems",
            "plural of system",
            [new BackupLookupMeaning(
                "meaning-1",
                "noun",
                "A set of connected things.",
                "System",
                "The systems are connected.",
                ["computing", "formal"])],
            sourceReference,
            UtcTime,
            1,
            [new BackupFormRelation(
                BackupGrammaticalRelationKind.Plural,
                "system",
                "plural of")]);
        var preparationWorkflow = new BackupPreparationWorkflow(
            "preparation-workflow-1",
            BackupPreparationSessionStatus.Active,
            BackupPreparationMethod.AutomaticOnline,
            1,
            0,
            UtcTime.AddHours(-1),
            UtcTime,
            null,
            [new BackupPreparationItem(
                "preparation-item-1",
                "vocabulary-1",
                0,
                BackupPreparationCandidateStatus.ResultReady,
                0,
                "lookup-ready",
                1,
                UtcTime,
                lookupDraft)]);

        var learningWorkflow = new BackupLearningWorkflow(
            "learning-workflow-1",
            BackupLearningSessionStatus.Active,
            1,
            0,
            0,
            0,
            0,
            0,
            UtcTime.AddMinutes(-10),
            UtcTime,
            null,
            [new BackupLearningQueueItem(
                "queue-item-1",
                "card-1",
                0,
                true,
                false,
                true,
                true,
                true,
                false,
                null,
                null)]);

        return new BackupPayload(
            [source],
            [vocabulary],
            [prepared],
            new BackupLearningData([card], [learningReview]),
            new BackupWorkflowData(
                [reviewWorkflow],
                [preparationWorkflow],
                [learningWorkflow]),
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>
            {
                ["synthetic-feature"] = new("{\"enabled\":true,\"label\":\"synthetic\"}")
            }));
    }

    public static BackupPayload CreateEmptyPayload() => new(
        [],
        [],
        [],
        new BackupLearningData([], []),
        new BackupWorkflowData([], [], []),
        new BackupExtensions(new Dictionary<string, BackupExtensionPayload>()));

    public static BackupManifest CreateManifest(BackupPayload payload) => new(
        BackupFormatLimits.FormatVersion,
        "1.0.0-beta.8",
        7,
        UtcTime,
        BackupSourcePlatform.Windows,
        BackupModelContract.CountRecords(payload),
        $"sha256:{new string('0', 64)}",
        payload.Extensions.Features.Count == 0 ? [] : ["synthetic-feature"],
        []);

    public static BackupRestorePreview CreatePreview(BackupPayload payload) => new(
        BackupFormatLimits.FormatVersion,
        "1.0.0-beta.8",
        7,
        UtcTime,
        BackupSourcePlatform.Windows,
        BackupModelContract.CountRecords(payload),
        payload.Extensions.Features.Count == 0 ? [] : ["synthetic-feature"],
        [],
        true,
        new BackupReplaceAllScope(true, true, true, true, true));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
