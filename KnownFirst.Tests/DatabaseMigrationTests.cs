using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Core.Learning;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DatabaseMigrationTests
{
    [TestMethod]
    public async Task Migration_OldSchemaToNewSchema_PreservesDataAndAddsDefaults()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"knownfirst-migration-{Guid.NewGuid():N}.db3");
        SQLiteAsyncConnection? asyncConnection = null;
        try
        {
            using (var connection = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache))
            {
                connection.Execute("CREATE TABLE Words (Id INTEGER PRIMARY KEY AUTOINCREMENT, Language TEXT, CanonicalTerm TEXT, NormalizedTerm TEXT, Status INTEGER, TotalOccurrenceCount INTEGER, DocumentCount INTEGER, CreatedAt INTEGER, UpdatedAt INTEGER)");
                connection.Execute("INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, Status, TotalOccurrenceCount, DocumentCount, CreatedAt, UpdatedAt) VALUES ('en', 'network', 'network', 0, 1, 1, 0, 0)");
            }

            asyncConnection = new SQLiteAsyncConnection(tempPath);
            await DatabaseSchema.InitializeAsync(asyncConnection);

            var words = await asyncConnection.Table<WordEntity>().ToListAsync();
            Assert.AreEqual(1, words.Count);
            var word = words[0];
            Assert.AreEqual("network", word.CanonicalTerm);
            
            Assert.AreEqual(LearningInteractionMode.Reading, word.AutomaticInteractionMode);
            Assert.AreEqual(0, word.ConsecutiveRecallSuccessCount);

            Console.WriteLine($"Test used isolated temp database at: {tempPath}");
        }
        finally
        {
            if (asyncConnection != null)
            {
                await asyncConnection.CloseAsync();
            }
            SQLiteAsyncConnection.ResetPool();
            
            var filesToDelete = new[]
            {
                tempPath,
                tempPath + "-wal",
                tempPath + "-shm"
            };
            
            foreach (var file in filesToDelete)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
