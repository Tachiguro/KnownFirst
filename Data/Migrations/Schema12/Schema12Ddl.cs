namespace KnownFirst.Data.Migrations.Schema12;

internal static class Schema12Ddl
{
    public const string StateTableName = "LearningDayState";
    public const string GrantsTableName = "LearningDayGrants";

    public const string GrantsDayOrdinalIndexName = "IX_LearningDayGrants_DayOrdinal";
    public const string GrantsUniqueWordIndexName = "IX_LearningDayGrants_DayOrdinal_WordId";
    public const string GrantsUniqueSlotIndexName = "IX_LearningDayGrants_DayOrdinal_SlotOrdinal";

    public const string CreateStateTable = """
        CREATE TABLE LearningDayState (
            Id INTEGER PRIMARY KEY,
            Phase INTEGER NOT NULL,
            DayOrdinal INTEGER NOT NULL,
            ActiveDayStartUtc TEXT NOT NULL,
            ActiveDayEndUtc TEXT NOT NULL,
            FrozenTimeZoneId TEXT NOT NULL,
            FrozenCutoffMinutes INTEGER NOT NULL,
            BridgeStartedUtc TEXT,
            BridgeTargetTimeZoneId TEXT,
            BridgeTargetCutoffMinutes INTEGER,
            BridgeTargetUtc TEXT,
            UpdatedAtUtc TEXT NOT NULL,
            CHECK (Id = 1),
            CHECK (
                (Phase = 1 AND BridgeStartedUtc IS NULL AND BridgeTargetTimeZoneId IS NULL AND BridgeTargetCutoffMinutes IS NULL AND BridgeTargetUtc IS NULL)
                OR
                (Phase = 2 AND BridgeStartedUtc IS NOT NULL AND BridgeTargetTimeZoneId IS NOT NULL AND BridgeTargetCutoffMinutes IS NOT NULL AND BridgeTargetUtc IS NOT NULL)
            )
        )
        """;

    public const string CreateGrantsTable = """
        CREATE TABLE LearningDayGrants (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            DayOrdinal INTEGER NOT NULL,
            WordId INTEGER NOT NULL,
            SlotOrdinal INTEGER NOT NULL,
            GrantedAtUtc TEXT NOT NULL,
            UNIQUE(DayOrdinal, WordId),
            UNIQUE(DayOrdinal, SlotOrdinal)
        )
        """;

    public const string CreateGrantsDayOrdinalIndex =
        $"CREATE INDEX {GrantsDayOrdinalIndexName} ON {GrantsTableName} (DayOrdinal)";
}
