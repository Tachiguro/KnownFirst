namespace KnownFirst.Data.Migrations.Schema8;

/// <summary>
/// Raw DDL for the Schema-8 shape (architecture doc §2, §4.3 step 1). Issued only from inside
/// <see cref="Schema8DormantMigration"/> — never from <c>DatabaseSchema.InitializeAsync</c>.
/// </summary>
internal static class Schema8Ddl
{
    public const string CreateSenses = """
        CREATE TABLE Senses (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            WordId INTEGER NOT NULL,
            SourceLanguage TEXT NOT NULL,
            ExplanationLanguage TEXT NOT NULL,
            ProviderSenseId TEXT NOT NULL DEFAULT '',
            TopicOrDomain TEXT NOT NULL DEFAULT '',
            PartOfSpeech TEXT NOT NULL DEFAULT '',
            GrammaticalRelationship TEXT NOT NULL DEFAULT '',
            AcronymExpansion TEXT NOT NULL DEFAULT '',
            DefaultMeaningId INTEGER NULL,
            Status INTEGER NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        )
        """;

    public const string IndexSensesStableId =
        "CREATE UNIQUE INDEX IX_Senses_StableId ON Senses (StableId)";

    public const string IndexSensesWordId =
        "CREATE INDEX IX_Senses_WordId ON Senses (WordId)";

    public const string CreateAnswerVariants = """
        CREATE TABLE AnswerVariants (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            SenseId INTEGER NOT NULL,
            AnswerLanguage TEXT NOT NULL,
            DisplayText TEXT NOT NULL,
            NormalizedText TEXT NOT NULL,
            SourceMeaningId INTEGER NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        )
        """;

    public const string IndexAnswerVariantsStableId =
        "CREATE UNIQUE INDEX IX_AnswerVariants_StableId ON AnswerVariants (StableId)";

    public const string IndexAnswerVariantsSenseLanguageText =
        "CREATE UNIQUE INDEX IX_AnswerVariants_Sense_Language_Text ON AnswerVariants (SenseId, AnswerLanguage, NormalizedText)";

    /// <summary>
    /// KF-MEANING-001 Slice 4: <c>RequiredSinceUtc</c> is the durable Requirement-effective boundary and
    /// the Required-epoch identity. Binding invariant: <c>Requirement = Required</c> if and only if
    /// <c>RequiredSinceUtc IS NOT NULL</c>. Replay may only consume reviews whose <c>ReviewedAtUtc</c> is at
    /// or after this boundary, so reviews from an earlier AcceptedOnly period can never retroactively grant
    /// Required progress or mastery.
    /// </summary>
    public const string CreateSenseAnswerVariantAssignments = """
        CREATE TABLE SenseAnswerVariantAssignments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            SenseId INTEGER NOT NULL,
            CardDirection INTEGER NOT NULL,
            AnswerVariantId INTEGER NOT NULL,
            Requirement INTEGER NOT NULL,
            IsPreferred INTEGER NOT NULL,
            RequiredSinceUtc TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        )
        """;

    /// <summary>The exact column name the Schema-8 physical shape must carry (Slice 4).</summary>
    public const string AssignmentRequiredSinceUtcColumn = "RequiredSinceUtc";

    public const string IndexAssignmentsStableId =
        "CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_StableId ON SenseAnswerVariantAssignments (StableId)";

    public const string IndexAssignmentsSenseDirectionVariant =
        "CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Variant ON SenseAnswerVariantAssignments (SenseId, CardDirection, AnswerVariantId)";

    public const string IndexAssignmentsSenseDirectionPreferred = """
        CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred
        ON SenseAnswerVariantAssignments (SenseId, CardDirection)
        WHERE IsPreferred = 1
        """;

    public const string CreateAnswerVariantProgress = """
        CREATE TABLE AnswerVariantProgress (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CardId INTEGER NOT NULL,
            AnswerVariantId INTEGER NOT NULL,
            InteractionMode INTEGER NOT NULL,
            ConsecutiveReadingSuccessCount INTEGER NOT NULL,
            ConsecutiveTypingSuccessCount INTEGER NOT NULL,
            ConsecutiveTypingFailureCount INTEGER NOT NULL,
            LastAssessedAtUtc TEXT NULL,
            MasteryReviewExtensionScheduled INTEGER NOT NULL,
            IsMastered INTEGER NOT NULL,
            ReplayVersion INTEGER NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        )
        """;

    public const string IndexProgressCardVariant =
        "CREATE UNIQUE INDEX IX_AnswerVariantProgress_Card_Variant ON AnswerVariantProgress (CardId, AnswerVariantId)";

    public const string OldCardIndexName = "IX_LearningCards_Word_Direction";

    public const string IndexLearningCardsSenseDirection =
        "CREATE UNIQUE INDEX IX_LearningCards_Sense_Direction ON LearningCards (SenseId, Direction)";
}
