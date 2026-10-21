using KnownFirst.Data;
using KnownFirst.Models;

namespace KnownFirst.Services;

public sealed class VocabularyOverviewService(IKnownFirstDatabase database) : IVocabularyOverviewService
{
    public const int DefaultLimit = IVocabularyOverviewService.DefaultLimit;
    public const int MaxLimit = IVocabularyOverviewService.MaxLimit;

    public Task<IReadOnlyList<VocabularyOverviewItem>> GetRecentVocabularyAsync(int limit = DefaultLimit)
    {
        if (limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<VocabularyOverviewItem>>(Array.Empty<VocabularyOverviewItem>());
        }

        var effectiveLimit = Math.Min(limit, MaxLimit);

        return database.ExecuteSnapshotAsync<IReadOnlyList<VocabularyOverviewItem>>(connection =>
        {
            var rows = connection.Query<WordOverviewRow>(
                """
                SELECT Id, CanonicalTerm, Language, CreatedAt
                FROM Words
                ORDER BY CreatedAt DESC, Id DESC
                LIMIT ?
                """,
                effectiveLimit);

            return rows.Select(r => new VocabularyOverviewItem(
                WordId: r.Id,
                CanonicalTerm: r.CanonicalTerm,
                SourceLanguage: r.Language,
                CreatedAt: r.CreatedAt)).ToList();
        });
    }

    private sealed class WordOverviewRow
    {
        public int Id { get; set; }
        public string CanonicalTerm { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
