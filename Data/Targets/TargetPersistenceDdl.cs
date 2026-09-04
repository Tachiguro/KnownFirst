namespace KnownFirst.Data.Targets;

internal static class TargetPersistenceDdl
{
    public const string LearningTargetsTableName = "LearningTargets";
    public const string TargetAnswerVariantsTableName = "TargetAnswerVariants";

    public const string LearningTargetsStableIdIndexName = "IX_LearningTargets_StableId";
    public const string LearningTargetsSenseIdIndexName = "IX_LearningTargets_SenseId";
    public const string LearningTargetsSemanticIndexName = "IX_LearningTargets_Sense_Kind_Languages";

    public const string TargetAnswerVariantsStableIdIndexName = "IX_TargetAnswerVariants_StableId";
    public const string TargetAnswerVariantsTargetIdIndexName = "IX_TargetAnswerVariants_TargetId";
    public const string TargetAnswerVariantsNormalizedTextIndexName = "IX_TargetAnswerVariants_Target_NormalizedText";
    public const string TargetAnswerVariantsPreferredIndexName = "IX_TargetAnswerVariants_Target_Preferred";

    public const string CreateLearningTargetsTable = """
        CREATE TABLE LearningTargets (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            SenseId INTEGER NOT NULL,
            TargetKind INTEGER NOT NULL,
            SourceLanguage TEXT NOT NULL,
            TargetLanguage TEXT NOT NULL,
            TypingOptOut INTEGER NOT NULL DEFAULT 0,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (SenseId) REFERENCES Senses(Id) ON DELETE CASCADE,
            CHECK (LENGTH(TRIM(StableId)) > 0),
            CHECK (TargetKind IN (0, 1)),
            CHECK (LENGTH(TRIM(SourceLanguage)) > 0),
            CHECK (LENGTH(TRIM(TargetLanguage)) > 0),
            CHECK (TypingOptOut IN (0, 1)),
            CHECK (LENGTH(TRIM(CreatedAtUtc)) > 0),
            CHECK (LENGTH(TRIM(UpdatedAtUtc)) > 0)
        )
        """;

    public const string CreateLearningTargetsStableIdIndex =
        $"CREATE UNIQUE INDEX {LearningTargetsStableIdIndexName} ON {LearningTargetsTableName} (StableId)";

    public const string CreateLearningTargetsSenseIdIndex =
        $"CREATE INDEX {LearningTargetsSenseIdIndexName} ON {LearningTargetsTableName} (SenseId)";

    public const string CreateLearningTargetsSemanticIndex =
        $"CREATE UNIQUE INDEX {LearningTargetsSemanticIndexName} ON {LearningTargetsTableName} (SenseId, TargetKind, SourceLanguage, TargetLanguage)";

    public const string CreateTargetAnswerVariantsTable = """
        CREATE TABLE TargetAnswerVariants (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            TargetId INTEGER NOT NULL,
            AnswerLanguage TEXT NOT NULL,
            DisplayText TEXT NOT NULL,
            NormalizedText TEXT NOT NULL,
            Requirement INTEGER NOT NULL,
            IsPreferred INTEGER NOT NULL,
            RequiredSinceUtc TEXT NULL,
            SourceMeaningId INTEGER NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (TargetId) REFERENCES LearningTargets(Id) ON DELETE CASCADE,
            FOREIGN KEY (SourceMeaningId) REFERENCES Meanings(Id) ON DELETE SET NULL,
            CHECK (LENGTH(TRIM(StableId)) > 0),
            CHECK (LENGTH(TRIM(AnswerLanguage)) > 0),
            CHECK (LENGTH(TRIM(DisplayText)) > 0),
            CHECK (LENGTH(TRIM(NormalizedText)) > 0),
            CHECK (Requirement IN (0, 1)),
            CHECK (IsPreferred IN (0, 1)),
            CHECK ((Requirement = 0 AND RequiredSinceUtc IS NOT NULL) OR (Requirement = 1 AND RequiredSinceUtc IS NULL)),
            CHECK (LENGTH(TRIM(CreatedAtUtc)) > 0),
            CHECK (LENGTH(TRIM(UpdatedAtUtc)) > 0)
        )
        """;

    public const string CreateTargetAnswerVariantsStableIdIndex =
        $"CREATE UNIQUE INDEX {TargetAnswerVariantsStableIdIndexName} ON {TargetAnswerVariantsTableName} (StableId)";

    public const string CreateTargetAnswerVariantsTargetIdIndex =
        $"CREATE INDEX {TargetAnswerVariantsTargetIdIndexName} ON {TargetAnswerVariantsTableName} (TargetId)";

    public const string CreateTargetAnswerVariantsNormalizedTextIndex =
        $"CREATE UNIQUE INDEX {TargetAnswerVariantsNormalizedTextIndexName} ON {TargetAnswerVariantsTableName} (TargetId, NormalizedText)";

    public const string CreateTargetAnswerVariantsPreferredIndex =
        $"CREATE UNIQUE INDEX {TargetAnswerVariantsPreferredIndexName} ON {TargetAnswerVariantsTableName} (TargetId) WHERE IsPreferred = 1";
}
