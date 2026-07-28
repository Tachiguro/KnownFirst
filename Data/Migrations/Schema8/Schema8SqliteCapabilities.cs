using SQLite;

namespace KnownFirst.Data.Migrations.Schema8;

/// <summary>
/// Detects whether the bundled SQLite build supports <c>ALTER TABLE ... RENAME COLUMN</c> (added in
/// SQLite 3.25.0), used by <see cref="Schema8DormantMigration"/> to choose between the fast rename and
/// the documented transactional table-rebuild fallback (architecture doc §2.7 "Column rename mechanics").
/// </summary>
public static class Schema8SqliteCapabilities
{
    private static readonly Version MinimumRenameColumnVersion = new(3, 25, 0);

    public static string GetSqliteVersion(SQLiteConnection connection) =>
        connection.ExecuteScalar<string>("SELECT sqlite_version()");

    public static bool SupportsRenameColumn(SQLiteConnection connection) =>
        TryParseSqliteVersion(GetSqliteVersion(connection), out var version)
        && version >= MinimumRenameColumnVersion;

    private static bool TryParseSqliteVersion(string raw, out Version version)
    {
        // sqlite_version() returns e.g. "3.46.1" — sometimes with a trailing build tag SQLite itself
        // never emits, but Version.TryParse tolerates a bare "major.minor.build" triplet directly.
        var core = raw.Split(' ', '-')[0];
        return Version.TryParse(core, out version!);
    }
}
