using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Services;

public sealed class VocabularySearchService(IKnownFirstDatabase database) : IVocabularySearchService
{
    public const int DefaultPageSize = IVocabularySearchService.DefaultPageSize;
    public const int MaxPageSize = IVocabularySearchService.MaxPageSize;

    private const string RangeQuerySql =
        """
        SELECT Id, CanonicalTerm, NormalizedTerm, Language, TokenKind, CreatedAt
        FROM Words
        WHERE Language = ?
          AND NormalizedTerm >= ?
          AND NormalizedTerm < ?
        ORDER BY NormalizedTerm ASC, Id ASC
        LIMIT ?
        """;

    private const string RangeAfterCursorQuerySql =
        """
        SELECT Id, CanonicalTerm, NormalizedTerm, Language, TokenKind, CreatedAt
        FROM Words
        WHERE Language = ?
          AND NormalizedTerm >= ?
          AND NormalizedTerm < ?
          AND (NormalizedTerm > ? OR Id > ?)
        ORDER BY NormalizedTerm ASC, Id ASC
        LIMIT ?
        """;

    private const string ExactAliasQuerySql =
        """
        SELECT Id, CanonicalTerm, NormalizedTerm, Language, TokenKind, CreatedAt
        FROM Words
        WHERE Language = ?
          AND NormalizedTerm = ?
        ORDER BY NormalizedTerm ASC, Id ASC
        LIMIT ?
        """;

    private const string ExactAliasAfterCursorQuerySql =
        """
        SELECT Id, CanonicalTerm, NormalizedTerm, Language, TokenKind, CreatedAt
        FROM Words
        WHERE Language = ?
          AND NormalizedTerm = ?
          AND Id > ?
        ORDER BY NormalizedTerm ASC, Id ASC
        LIMIT ?
        """;

    private readonly IKnownFirstDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    internal sealed record VocabularySearchSqlCommand(
        string CommandText,
        IReadOnlyList<object> Parameters,
        int FetchLimit,
        TokenKind? RangeTokenKind,
        bool IsExactAlias);

    public Task<VocabularySearchPage> SearchAsync(
        string? sourceLanguage,
        string? rawQuery,
        int pageSize = DefaultPageSize,
        VocabularySearchCursor? cursor = null,
        CancellationToken cancellationToken = default) =>
        SearchAsync(
            new VocabularySearchRequest(sourceLanguage, rawQuery, pageSize, cursor),
            cancellationToken);

    public Task<VocabularySearchPage> SearchAsync(
        VocabularySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = VocabularySearchKeyPolicy.CreatePlan(request.SourceLanguage, request.RawQuery);

        switch (plan.Status)
        {
            case VocabularySearchQueryStatus.InvalidSourceLanguage:
                return Task.FromResult(CreateNonActivePage(
                    VocabularySearchStatus.InvalidSourceLanguage,
                    plan.Status,
                    string.Empty,
                    string.Empty,
                    request.PageSize));

            case VocabularySearchQueryStatus.InvalidUnicode:
                return Task.FromResult(CreateNonActivePage(
                    VocabularySearchStatus.InvalidUnicode,
                    plan.Status,
                    plan.SourceLanguage,
                    string.Empty,
                    request.PageSize));

            case VocabularySearchQueryStatus.QueryTooLong:
                return Task.FromResult(CreateNonActivePage(
                    VocabularySearchStatus.QueryTooLong,
                    plan.Status,
                    plan.SourceLanguage,
                    string.Empty,
                    request.PageSize));
        }

        if (request.PageSize is < 1 or > MaxPageSize)
        {
            return Task.FromResult(CreateNonActivePage(
                VocabularySearchStatus.InvalidPageSize,
                plan.Status,
                plan.SourceLanguage,
                plan.NormalizedQuery,
                request.PageSize));
        }

        if (plan.Status == VocabularySearchQueryStatus.EmptyQuery)
        {
            if (request.Cursor is not null)
            {
                return Task.FromResult(CreateNonActivePage(
                    VocabularySearchStatus.InvalidCursor,
                    plan.Status,
                    plan.SourceLanguage,
                    string.Empty,
                    request.PageSize));
            }

            return Task.FromResult(CreateNonActivePage(
                VocabularySearchStatus.EmptyQuery,
                VocabularySearchQueryStatus.EmptyQuery,
                plan.SourceLanguage,
                string.Empty,
                request.PageSize));
        }

        if (request.Cursor is not null && !IsCursorCompatible(plan, request.PageSize, request.Cursor))
        {
            return Task.FromResult(CreateNonActivePage(
                VocabularySearchStatus.InvalidCursor,
                plan.Status,
                plan.SourceLanguage,
                plan.NormalizedQuery,
                request.PageSize));
        }

        return _database.ExecuteSnapshotAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteSnapshotCore(
                connection,
                plan,
                request.PageSize,
                request.Cursor,
                out _,
                out _);
        });
    }

    public static bool IsCursorCompatible(
        VocabularySearchPlan plan,
        int pageSize,
        VocabularySearchCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(cursor);

        if (!plan.IsSearchActive
            || pageSize is < 1 or > MaxPageSize
            || cursor.PageSize != pageSize
            || cursor.LastWordId <= 0
            || !string.Equals(cursor.SourceLanguage, plan.SourceLanguage, StringComparison.Ordinal)
            || !string.Equals(cursor.NormalizedQuery, plan.NormalizedQuery, StringComparison.Ordinal)
            || string.IsNullOrEmpty(cursor.LastNormalizedTerm)
            || !VocabularySearchKeyPolicy.IsValidUnicode(cursor.LastNormalizedTerm))
        {
            return false;
        }

        return plan.MatchesUtf8(cursor.LastNormalizedTerm);
    }

    internal static IReadOnlyList<VocabularySearchSqlCommand> BuildSqlCommands(
        VocabularySearchPlan plan,
        int pageSize,
        VocabularySearchCursor? cursor = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsSearchActive || pageSize is < 1 or > MaxPageSize)
        {
            return Array.Empty<VocabularySearchSqlCommand>();
        }

        var fetchLimit = pageSize + 1;
        var commands = new List<VocabularySearchSqlCommand>(plan.KeyRanges.Count + plan.ExactAliases.Count);

        for (var i = 0; i < plan.KeyRanges.Count; i++)
        {
            var range = plan.KeyRanges[i];
            if (cursor is null)
            {
                commands.Add(new VocabularySearchSqlCommand(
                    RangeQuerySql,
                    Array.AsReadOnly<object>([
                        plan.SourceLanguage,
                        range.InclusiveLowerBound,
                        range.ExclusiveUpperBound,
                        fetchLimit
                    ]),
                    fetchLimit,
                    range.TokenKind,
                    IsExactAlias: false));
                continue;
            }

            var compareToUpper = VocabularySearchKeyPolicy.CompareUtf8Ordinal(
                cursor.LastNormalizedTerm,
                range.ExclusiveUpperBound);
            if (compareToUpper >= 0)
            {
                // Cursor is already past this entire token-kind range.
                continue;
            }

            var compareToLower = VocabularySearchKeyPolicy.CompareUtf8Ordinal(
                cursor.LastNormalizedTerm,
                range.InclusiveLowerBound);
            if (compareToLower < 0)
            {
                // Cursor precedes this range; query the full range from its inclusive lower bound.
                commands.Add(new VocabularySearchSqlCommand(
                    RangeQuerySql,
                    Array.AsReadOnly<object>([
                        plan.SourceLanguage,
                        range.InclusiveLowerBound,
                        range.ExclusiveUpperBound,
                        fetchLimit
                    ]),
                    fetchLimit,
                    range.TokenKind,
                    IsExactAlias: false));
            }
            else
            {
                // Cursor lies within [InclusiveLowerBound, ExclusiveUpperBound); resume from cursor.
                commands.Add(new VocabularySearchSqlCommand(
                    RangeAfterCursorQuerySql,
                    Array.AsReadOnly<object>([
                        plan.SourceLanguage,
                        cursor.LastNormalizedTerm,
                        range.ExclusiveUpperBound,
                        cursor.LastNormalizedTerm,
                        cursor.LastWordId,
                        fetchLimit
                    ]),
                    fetchLimit,
                    range.TokenKind,
                    IsExactAlias: false));
            }
        }

        for (var i = 0; i < plan.ExactAliases.Count; i++)
        {
            var alias = plan.ExactAliases[i];
            if (IsCoveredByAnyRange(plan.KeyRanges, alias))
            {
                continue;
            }

            if (cursor is null)
            {
                commands.Add(new VocabularySearchSqlCommand(
                    ExactAliasQuerySql,
                    Array.AsReadOnly<object>([
                        plan.SourceLanguage,
                        alias,
                        fetchLimit
                    ]),
                    fetchLimit,
                    RangeTokenKind: null,
                    IsExactAlias: true));
                continue;
            }

            var compareCursorToAlias = VocabularySearchKeyPolicy.CompareUtf8Ordinal(
                cursor.LastNormalizedTerm,
                alias);
            if (compareCursorToAlias < 0)
            {
                commands.Add(new VocabularySearchSqlCommand(
                    ExactAliasQuerySql,
                    Array.AsReadOnly<object>([
                        plan.SourceLanguage,
                        alias,
                        fetchLimit
                    ]),
                    fetchLimit,
                    RangeTokenKind: null,
                    IsExactAlias: true));
            }
            else if (compareCursorToAlias == 0)
            {
                commands.Add(new VocabularySearchSqlCommand(
                    ExactAliasAfterCursorQuerySql,
                    Array.AsReadOnly<object>([
                        plan.SourceLanguage,
                        alias,
                        cursor.LastWordId,
                        fetchLimit
                    ]),
                    fetchLimit,
                    RangeTokenKind: null,
                    IsExactAlias: true));
            }
        }

        return commands.AsReadOnly();
    }

    internal static VocabularySearchPage ExecuteSnapshotCore(
        SQLiteConnection connection,
        VocabularySearchPlan plan,
        int pageSize,
        VocabularySearchCursor? cursor,
        out int executedQueryCount,
        out int materializedCandidateCount)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(plan);

        var commands = BuildSqlCommands(plan, pageSize, cursor);
        var fetchLimit = pageSize + 1;
        var candidates = new List<WordSearchRow>(commands.Count * fetchLimit);

        executedQueryCount = 0;
        materializedCandidateCount = 0;

        for (var i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            var rows = connection.Query<WordSearchRow>(cmd.CommandText, cmd.Parameters.ToArray());
            executedQueryCount++;
            materializedCandidateCount += rows.Count;

            for (var j = 0; j < rows.Count; j++)
            {
                var row = rows[j];
                if (cursor is null || IsStrictlyAfterCursor(row, cursor))
                {
                    candidates.Add(row);
                }
            }
        }

        candidates.Sort(CompareRows);

        var uniqueItems = new List<VocabularySearchItem>(fetchLimit);
        string? previousNormalizedTerm = null;
        int previousWordId = -1;

        for (var i = 0; i < candidates.Count && uniqueItems.Count < fetchLimit; i++)
        {
            var candidate = candidates[i];
            if (candidate.Id == previousWordId
                && string.Equals(candidate.NormalizedTerm, previousNormalizedTerm, StringComparison.Ordinal))
            {
                continue;
            }

            previousNormalizedTerm = candidate.NormalizedTerm;
            previousWordId = candidate.Id;

            uniqueItems.Add(new VocabularySearchItem(
                WordId: candidate.Id,
                CanonicalTerm: candidate.CanonicalTerm,
                NormalizedTerm: candidate.NormalizedTerm,
                SourceLanguage: candidate.Language,
                TokenKind: candidate.TokenKind,
                CreatedAt: candidate.CreatedAt));
        }

        var hasMore = uniqueItems.Count > pageSize;
        if (hasMore)
        {
            uniqueItems.RemoveRange(pageSize, uniqueItems.Count - pageSize);
        }

        VocabularySearchCursor? nextCursor = null;
        if (hasMore && uniqueItems.Count > 0)
        {
            var lastItem = uniqueItems[^1];
            nextCursor = new VocabularySearchCursor(
                SourceLanguage: plan.SourceLanguage,
                NormalizedQuery: plan.NormalizedQuery,
                PageSize: pageSize,
                LastNormalizedTerm: lastItem.NormalizedTerm,
                LastWordId: lastItem.WordId);
        }

        return new VocabularySearchPage(
            Status: VocabularySearchStatus.Active,
            QueryStatus: plan.Status,
            SourceLanguage: plan.SourceLanguage,
            NormalizedQuery: plan.NormalizedQuery,
            PageSize: pageSize,
            Items: uniqueItems.AsReadOnly(),
            HasMore: hasMore,
            NextCursor: nextCursor);
    }

    private static bool IsCoveredByAnyRange(
        IReadOnlyList<VocabularySearchKeyRange> keyRanges,
        string candidateIdentity)
    {
        for (var i = 0; i < keyRanges.Count; i++)
        {
            if (keyRanges[i].MatchesUtf8(candidateIdentity))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStrictlyAfterCursor(WordSearchRow row, VocabularySearchCursor cursor)
    {
        var termComparison = VocabularySearchKeyPolicy.CompareUtf8Ordinal(
            row.NormalizedTerm,
            cursor.LastNormalizedTerm);
        if (termComparison > 0)
        {
            return true;
        }

        return termComparison == 0 && row.Id > cursor.LastWordId;
    }

    private static int CompareRows(WordSearchRow left, WordSearchRow right)
    {
        var termComparison = VocabularySearchKeyPolicy.CompareUtf8Ordinal(
            left.NormalizedTerm,
            right.NormalizedTerm);
        return termComparison != 0
            ? termComparison
            : left.Id.CompareTo(right.Id);
    }

    private static VocabularySearchPage CreateNonActivePage(
        VocabularySearchStatus status,
        VocabularySearchQueryStatus queryStatus,
        string sourceLanguage,
        string normalizedQuery,
        int pageSize) =>
        new(
            Status: status,
            QueryStatus: queryStatus,
            SourceLanguage: sourceLanguage,
            NormalizedQuery: normalizedQuery,
            PageSize: pageSize,
            Items: VocabularySearchPage.EmptyItems,
            HasMore: false,
            NextCursor: null);

    private sealed class WordSearchRow
    {
        public int Id { get; set; }
        public string CanonicalTerm { get; set; } = string.Empty;
        public string NormalizedTerm { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public TokenKind TokenKind { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
