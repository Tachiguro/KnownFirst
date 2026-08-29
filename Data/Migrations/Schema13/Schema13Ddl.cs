namespace KnownFirst.Data.Migrations.Schema13;

internal static class Schema13Ddl
{
    public const string FsrsCardStatesTableName = "FsrsCardStates";
    public const string FsrsReviewHistoryEntriesTableName = "FsrsReviewHistoryEntries";
    public const string WordLearningControlsTableName = "WordLearningControls";
    public const string SenseLearningControlsTableName = "SenseLearningControls";

    public const string FsrsCardStatesDueIndexName = "IX_FsrsCardStates_State_DueAtUtc";
    public const string FsrsReviewHistoryEntriesStableIdIndexName = "IX_FsrsReviewHistoryEntries_StableId";
    public const string FsrsReviewHistoryEntriesCardSequenceIndexName = "IX_FsrsReviewHistoryEntries_Card_Sequence";
    public const string FsrsReviewHistoryEntriesReplayIndexName = "IX_FsrsReviewHistoryEntries_Card_Replay";

    public const string CreateFsrsCardStatesTable = """
        CREATE TABLE FsrsCardStates (
            CardId INTEGER PRIMARY KEY,
            State INTEGER NOT NULL,
            Stability REAL,
            Difficulty REAL,
            LastReviewedAtUtc TEXT,
            StepIndex INTEGER,
            DueAtUtc TEXT,
            FOREIGN KEY (CardId) REFERENCES LearningCards(Id) ON DELETE CASCADE,
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

    public const string CreateFsrsCardStatesDueIndex =
        $"CREATE INDEX {FsrsCardStatesDueIndexName} ON {FsrsCardStatesTableName} (State, DueAtUtc)";

    public const string CreateFsrsReviewHistoryEntriesTable = """
        CREATE TABLE FsrsReviewHistoryEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StableId TEXT NOT NULL,
            CardId INTEGER NOT NULL,
            SequenceNumber INTEGER NOT NULL,
            Rating INTEGER NOT NULL,
            ReviewedAtUtc TEXT NOT NULL,
            FOREIGN KEY (CardId) REFERENCES LearningCards(Id) ON DELETE CASCADE,
            CHECK (LENGTH(TRIM(StableId)) > 0),
            CHECK (SequenceNumber > 0),
            CHECK (Rating IN (0, 1, 2, 3))
        )
        """;

    public const string CreateFsrsReviewHistoryEntriesStableIdIndex =
        $"CREATE UNIQUE INDEX {FsrsReviewHistoryEntriesStableIdIndexName} ON {FsrsReviewHistoryEntriesTableName} (StableId)";

    public const string CreateFsrsReviewHistoryEntriesCardSequenceIndex =
        $"CREATE UNIQUE INDEX {FsrsReviewHistoryEntriesCardSequenceIndexName} ON {FsrsReviewHistoryEntriesTableName} (CardId, SequenceNumber)";

    public const string CreateFsrsReviewHistoryEntriesReplayIndex =
        $"CREATE INDEX {FsrsReviewHistoryEntriesReplayIndexName} ON {FsrsReviewHistoryEntriesTableName} (CardId, ReviewedAtUtc, SequenceNumber)";

    public const string CreateWordLearningControlsTable = """
        CREATE TABLE WordLearningControls (
            WordId INTEGER PRIMARY KEY,
            DecidedAtUtc TEXT NOT NULL,
            FOREIGN KEY (WordId) REFERENCES Words(Id) ON DELETE CASCADE,
            CHECK (LENGTH(TRIM(DecidedAtUtc)) > 0)
        )
        """;

    public const string CreateSenseLearningControlsTable = """
        CREATE TABLE SenseLearningControls (
            SenseId INTEGER PRIMARY KEY,
            DecidedAtUtc TEXT NOT NULL,
            FOREIGN KEY (SenseId) REFERENCES Senses(Id) ON DELETE CASCADE,
            CHECK (LENGTH(TRIM(DecidedAtUtc)) > 0)
        )
        """;
}
