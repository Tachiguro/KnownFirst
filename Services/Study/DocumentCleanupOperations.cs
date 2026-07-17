using KnownFirst.Data.Entities;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Services.Study;

internal static class DocumentCleanupOperations
{
    public static int CleanupEligibleDocuments(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var deletedCount = 0;
        foreach (var document in connection.Table<DocumentEntity>().ToList())
        {
            var hasActiveReview = connection.Table<ReviewSessionEntity>()
                .Any(session => session.DocumentId == document.Id
                    && session.Status == ReviewSessionStatus.Active);
            var hasOccurrences = connection.Table<WordOccurrenceEntity>()
                .Any(occurrence => occurrence.DocumentId == document.Id);
            var hasActiveSnapshots = connection.Table<ContextSnapshotEntity>()
                .Where(snapshot => snapshot.SourceDocumentId == document.Id)
                .ToList()
                .Any(snapshot =>
                {
                    var word = connection.Find<WordEntity>(snapshot.WordId);
                    return word is not null && word.Status != WordStatus.Known;
                });
            if (hasActiveReview || hasOccurrences || hasActiveSnapshots)
            {
                continue;
            }

            connection.Execute("DELETE FROM ContextSnapshots WHERE SourceDocumentId = ?", document.Id);
            connection.Execute("DELETE FROM SentenceSpans WHERE DocumentId = ?", document.Id);
            foreach (var session in connection.Table<ReviewSessionEntity>()
                         .Where(item => item.DocumentId == document.Id)
                         .ToList())
            {
                connection.Execute("DELETE FROM ReviewCandidates WHERE SessionId = ?", session.Id);
                connection.Delete(session);
            }

            connection.Delete(document);
            deletedCount++;
        }

        foreach (var sentence in connection.Table<SentenceSpanEntity>().ToList())
        {
            if (!connection.Table<WordOccurrenceEntity>()
                .Any(occurrence => occurrence.SentenceSpanId == sentence.Id))
            {
                connection.Delete(sentence);
            }
        }

        return deletedCount;
    }
}
