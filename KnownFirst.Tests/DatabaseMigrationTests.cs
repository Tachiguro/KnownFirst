using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Core.Learning;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class DatabaseMigrationTests
{
    [TestMethod]
    public async Task Migration_OldSchemaToNewSchema_PreservesDataAndAddsDefaults()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"knownfirst-migration-{Guid.NewGuid():N}.db3");
        try
        {
            using (var connection = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache))
            {
                connection.Execute("CREATE TABLE Word (Id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentId INTEGER, TokenKind INTEGER, SurfaceForm TEXT, CanonicalTerm TEXT, Identity TEXT, Status INTEGER, PreparationState INTEGER, CreatedAt INTEGER, UpdatedAt INTEGER)");
                connection.Execute("INSERT INTO Word (DocumentId, TokenKind, SurfaceForm, CanonicalTerm, Identity, Status, PreparationState, CreatedAt, UpdatedAt) VALUES (1, 0, 'network', 'network', 'W:network', 0, 0, 0, 0)");
            }

            var asyncConnection = new SQLiteAsyncConnection(tempPath);
            await DatabaseSchema.InitializeAsync(asyncConnection);

            var words = await asyncConnection.Table<WordEntity>().ToListAsync();
            Assert.AreEqual(1, words.Count);
            var word = words[0];
            Assert.AreEqual("network", word.CanonicalTerm);
            
            Assert.AreEqual(LearningInteractionMode.Reading, word.AutomaticInteractionMode);
            Assert.AreEqual(0, word.ConsecutiveRecallSuccessCount);

            Console.WriteLine($"Test used isolated temp database at: {tempPath}");
            await asyncConnection.CloseAsync();
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int retries = 5;
            while (retries > 0)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                    break;
                }
                catch (IOException)
                {
                    retries--;
                    if (retries == 0) throw;
                    await Task.Delay(200);
                }
            }
        }
    }
}
