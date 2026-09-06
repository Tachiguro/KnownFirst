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

    public const string TargetFsrsStatesTableName = "TargetFsrsStates";
    public const string TargetFsrsStatesDueIndexName = "IX_TargetFsrsStates_State_DueAtUtc";

    public const string TargetFsrsReviewHistoryEntriesTableName = "TargetFsrsReviewHistoryEntries";
    public const string TargetFsrsReviewHistoryEntriesStableIdIndexName = "IX_TargetFsrsReviewHistoryEntries_StableId";
    public const string TargetFsrsReviewHistoryEntriesTargetSequenceIndexName = "IX_TargetFsrsReviewHistoryEntries_Target_Sequence";
    public const string TargetFsrsReviewHistoryEntriesReplayIndexName = "IX_TargetFsrsReviewHistoryEntries_Target_Replay";

    public const string TargetReviewsTableName = "TargetReviews";
    public const string TargetReviewsStableIdIndexName = "IX_TargetReviews_StableId";
    public const string TargetReviewsTargetIdIndexName = "IX_TargetReviews_TargetId";
    public const string TargetReviewsSessionIdIndexName = "IX_TargetReviews_SessionId";
    public const string TargetReviewsTargetReviewedAtIndexName = "IX_TargetReviews_Target_ReviewedAt";

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

    public const string CreateTargetFsrsStatesTable = """
        CREATE TABLE TargetFsrsStates (
            TargetId INTEGER PRIMARY KEY,
            State INTEGER NOT NULL,
            Stability REAL,
            Difficulty REAL,
            LastReviewedAtUtc TEXT,
            StepIndex INTEGER,
            DueAtUtc TEXT,
            FOREIGN KEY (TargetId) REFERENCES LearningTargets(Id) ON DELETE CASCADE,
            CHECK (State IN (0, 1, 2, 3)),
            CHECK (
                (Stability IS NULL OR Stability >= 0.001)
                AND (Difficulty IS NULL OR (Difficulty >= 1.0 AND Difficulty <= 10.0))
            ),
            CHECK (
                (State = 0 AND Stability IS NULL AND Difficulty IS NULL AND LastReviewedAtUtc IS NULL AND StepIndex IS NULL)
                OR (State = 1 AND Stability IS NOT NULL AND Difficulty IS NOT NULL AND LastReviewedAtUtc IS NOT NULL AND StepIndex = 0)
                OR (State = 2 AND Stability IS NOT NULL AND Difficulty IS NOT NULL AND LastReviewedAtUtc IS NOT NULL AND StepIndex IS NULL)
                OR (State = 3 AND Stability IS NOT NULL AND Difficulty IS NOT NULL AND LastReviewedAtUtc IS NOT NULL AND StepIndex = 0)
            )
        )
        """;

    public const string CreateTargetFsrsStatesDueIndex =
        $"CREATE INDEX {TargetFsrsStatesDueIndexName} ON {TargetFsrsStatesTableName} (State, DueAtUtc)";

    public const string CreateTargetFsrsReviewHistoryEntriesTable = """
        CREATE TABLE TargetFsrsReviewHistoryEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            TargetId INTEGER NOT NULL,
            SequenceNumber INTEGER NOT NULL,
            Rating INTEGER NOT NULL,
            ReviewedAtUtc TEXT NOT NULL,
            FOREIGN KEY (TargetId) REFERENCES LearningTargets(Id) ON DELETE CASCADE,
            CHECK (LENGTH(TRIM(StableId)) > 0),
            CHECK (SequenceNumber > 0),
            CHECK (Rating IN (0, 1, 2, 3))
        )
        """;

    public const string CreateTargetFsrsReviewHistoryEntriesStableIdIndex =
        $"CREATE UNIQUE INDEX {TargetFsrsReviewHistoryEntriesStableIdIndexName} ON {TargetFsrsReviewHistoryEntriesTableName} (StableId)";

    public const string CreateTargetFsrsReviewHistoryEntriesTargetSequenceIndex =
        $"CREATE UNIQUE INDEX {TargetFsrsReviewHistoryEntriesTargetSequenceIndexName} ON {TargetFsrsReviewHistoryEntriesTableName} (TargetId, SequenceNumber)";

    public const string CreateTargetFsrsReviewHistoryEntriesReplayIndex =
        $"CREATE INDEX {TargetFsrsReviewHistoryEntriesReplayIndexName} ON {TargetFsrsReviewHistoryEntriesTableName} (TargetId, ReviewedAtUtc, SequenceNumber)";

    public const string CreateTargetReviewsTable = """
        CREATE TABLE TargetReviews (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            TargetId INTEGER NOT NULL,
            SessionId INTEGER NOT NULL,
            Rating INTEGER NOT NULL,
            WasTypedAnswer INTEGER NOT NULL,
            WasCorrect INTEGER NOT NULL,
            IsSessionRepeat INTEGER NOT NULL,
            TargetAnswerVariantId INTEGER NULL,
            MatchedAnswerVariantId INTEGER NULL,
            ReviewedAtUtc TEXT NOT NULL,
            DueAtUtc TEXT NOT NULL,
            FOREIGN KEY (TargetId) REFERENCES LearningTargets(Id) ON DELETE CASCADE,
            FOREIGN KEY (SessionId) REFERENCES LearningSessions(Id) ON DELETE CASCADE,
            FOREIGN KEY (TargetAnswerVariantId) REFERENCES TargetAnswerVariants(Id) ON DELETE SET NULL,
            FOREIGN KEY (MatchedAnswerVariantId) REFERENCES TargetAnswerVariants(Id) ON DELETE SET NULL,
            CHECK (LENGTH(TRIM(StableId)) > 0),
            CHECK (Rating IN (0, 1, 2, 3)),
            CHECK (WasTypedAnswer IN (0, 1)),
            CHECK (WasCorrect IN (0, 1)),
            CHECK (IsSessionRepeat IN (0, 1)),
            CHECK (LENGTH(TRIM(ReviewedAtUtc)) > 0),
            CHECK (LENGTH(TRIM(DueAtUtc)) > 0)
        )
        """;

    public const string CreateTargetReviewsStableIdIndex =
        $"CREATE UNIQUE INDEX {TargetReviewsStableIdIndexName} ON {TargetReviewsTableName} (StableId)";

    public const string CreateTargetReviewsTargetIdIndex =
        $"CREATE INDEX {TargetReviewsTargetIdIndexName} ON {TargetReviewsTableName} (TargetId)";

    public const string CreateTargetReviewsSessionIdIndex =
        $"CREATE INDEX {TargetReviewsSessionIdIndexName} ON {TargetReviewsTableName} (SessionId)";

    public const string CreateTargetReviewsTargetReviewedAtIndex =
        $"CREATE INDEX {TargetReviewsTargetReviewedAtIndexName} ON {TargetReviewsTableName} (TargetId, ReviewedAtUtc)";
}
