using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.Lexical;
using SQLite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KnownFirst.Services.Study;

public sealed partial class PreparationService(
    IKnownFirstDatabase database,
    ILexicalEnrichmentService lexicalEnrichment,
    IClock clock) : IPreparationService
{
    private const int MaximumContextSnapshots = 3;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public Task<PreparationOverview> GetOverviewAsync() => database.ReadAsync(async connection =>
    {
        var now = clock.UtcNow;
        var words = await connection.Table<WordEntity>().ToListAsync();
        var active = await connection.Table<PreparationSessionEntity>()
            .Where(session => session.Status == PreparationSessionStatus.Active)
            .FirstOrDefaultAsync();
        var latestCompleted = await connection.Table<PreparationSessionEntity>()
            .Where(session => session.Status == PreparationSessionStatus.Completed)
            .OrderByDescending(session => session.Id)
            .FirstOrDefaultAsync();
        var lastCompletedPreparedItems = latestCompleted is null
            ? 0
            : await connection.Table<PreparationCandidateEntity>()
                .Where(candidate => candidate.SessionId == latestCompleted.Id
                    && candidate.Status == PreparationCandidateStatus.Prepared)
                .CountAsync();
        var dueCardCount = await connection.Table<LearningCardEntity>()
            .Where(card => card.State != CardState.New
                && card.State != CardState.Suspended
                && card.State != CardState.Retired
                && card.DueAtUtc <= now)
            .CountAsync();
        var preparedNewWordIds = (await connection.Table<LearningCardEntity>()
                .Where(card => card.State == CardState.New)
                .ToListAsync())
            .Select(card => card.WordId)
            .Distinct()
            .Count();
        var unprepared = words.Count(word => word.Status == WordStatus.UnknownBacklog
            && word.PreparationState != PreparationState.Prepared);
        return new PreparationOverview(
            unprepared,
            preparedNewWordIds,
            dueCardCount,
            active?.Id,
            active?.CompletedItems ?? 0,
            active?.TotalItems ?? 0,
            active?.Method,
            lastCompletedPreparedItems);
    });

    public async Task<int> StartAsync(PreparationMethod method, int requestedLimit)
    {
        await _operationGate.WaitAsync();
        try
        {
            return await database.RunInTransactionAsync(connection =>
            {
                if (connection.Table<ReviewSessionEntity>()
                    .Any(session => session.Status == ReviewSessionStatus.Active))
                {
                    throw new ActiveReviewExistsException();
                }

                var active = connection.Table<PreparationSessionEntity>()
                    .FirstOrDefault(session => session.Status == PreparationSessionStatus.Active);
                if (active is not null)
                {
                    return active.Id;
                }

                var preparedWordIds = connection.Table<MeaningEntity>()
                    .Where(meaning => meaning.ConfirmedByUser)
                    .ToList()
                    .Select(meaning => meaning.WordId)
                    .ToHashSet();
                var selected = PreparationSelectionPolicy.Select(
                    connection.Table<WordEntity>()
                        .ToList()
                        .Select(word => new PreparationSelectionCandidate(
                            word.Id,
                            word.CanonicalTerm,
                            word.TotalOccurrenceCount,
                            word.CreatedAt,
                            word.Status == WordStatus.UnknownBacklog,
                            word.PreparationState,
                            ReviewIsResolved(connection, word.Id),
                            preparedWordIds.Contains(word.Id))),
                    requestedLimit);
                if (selected.Count == 0)
                {
                    return 0;
                }

                var now = clock.UtcNow;
                var session = new PreparationSessionEntity
                {
                    Status = PreparationSessionStatus.Active,
                    Method = method,
                    TotalItems = selected.Count,
                    StartedAtUtc = now,
                    UpdatedAtUtc = now
                };
                connection.Insert(session);
                for (var index = 0; index < selected.Count; index++)
                {
                    var selectedWord = selected[index];
                    connection.Insert(new PreparationCandidateEntity
                    {
                        SessionId = session.Id,
                        WordId = selectedWord.WordId,
                        Order = index,
                        Status = PreparationCandidateStatus.Pending,
                        UpdatedAtUtc = now
                    });
                    var word = connection.Find<WordEntity>(selectedWord.WordId)
                        ?? throw new InvalidOperationException("A selected preparation word is missing.");
                    word.PreparationState = PreparationState.Preparing;
                    word.UpdatedAt = now;
                    connection.Update(word);
                }

                return session.Id;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<PreparationItem?> GetCurrentAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<PreparationSessionEntity>()
            .Where(item => item.Status == PreparationSessionStatus.Active)
            .FirstOrDefaultAsync();
        if (session is null)
        {
            return null;
        }

        var candidate = (await connection.Table<PreparationCandidateEntity>()
                .Where(item => item.SessionId == session.Id)
                .OrderBy(item => item.Order)
                .ToListAsync())
            .FirstOrDefault(item => item.Status is PreparationCandidateStatus.Pending
                or PreparationCandidateStatus.ResultReady
                or PreparationCandidateStatus.Failed);
        return candidate is null
            ? null
            : await CreateItemAsync(connection, session, candidate);
    });

    public async Task<PreparationItem?> LookupCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var source = await GetLookupSourceAsync();
            if (source is null)
            {
                return null;
            }

            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(source.Item.CandidateId)
                    ?? throw new InvalidOperationException("The preparation candidate no longer exists.");
                EnsureCurrentCandidate(connection, candidate);
                candidate.Status = PreparationCandidateStatus.Pending;
                candidate.LookupAttemptCount++;
                candidate.LastErrorCode = string.Empty;
                candidate.UpdatedAtUtc = clock.UtcNow;
                connection.Update(candidate);
                var word = connection.Find<WordEntity>(candidate.WordId)!;
                word.PreparationState = PreparationState.Preparing;
                connection.Update(word);
                return true;
            });

            var request = new LexicalLookupRequest(
                source.Item.Term,
                NormalizeLemma(source.Item.Term),
                source.Item.TokenKind,
                source.Item.SourceLanguage,
                source.Item.ExplanationLanguage);
            var result = await lexicalEnrichment.EnrichAsync(
                request,
                source.DocumentContent,
                source.Item.Contexts.FirstOrDefault()?.Text,
                cancellationToken);
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(source.Item.CandidateId)
                    ?? throw new InvalidOperationException("The preparation candidate no longer exists.");
                EnsureCurrentCandidate(connection, candidate);
                candidate.ResultJson = JsonSerializer.Serialize(result, SerializerOptions);
                candidate.SelectedMeaningIndex = 0;
                candidate.Status = result.HasUsableData
                    ? PreparationCandidateStatus.ResultReady
                    : PreparationCandidateStatus.Failed;
                candidate.LastErrorCode = result.ErrorCode ?? string.Empty;
                candidate.UpdatedAtUtc = clock.UtcNow;
                connection.Update(candidate);
                var word = connection.Find<WordEntity>(candidate.WordId)!;
                word.PreparationState = result.HasUsableData
                    ? PreparationState.Preparing
                    : PreparationState.PreparationFailed;
                word.UpdatedAt = clock.UtcNow;
                connection.Update(word);
                return true;
            });

            return await GetCurrentAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SelectMeaningAsync(int candidateId, int meaningIndex)
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
                    ?? throw new InvalidOperationException("The preparation candidate does not exist.");
                EnsureCurrentCandidate(connection, candidate);
                var result = DeserializeResult(candidate.ResultJson)
                    ?? throw new InvalidOperationException("The preparation candidate has no lexical result.");
                if (meaningIndex < 0 || meaningIndex >= result.Meanings.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(meaningIndex));
                }

                candidate.SelectedMeaningIndex = meaningIndex;
                candidate.UpdatedAtUtc = clock.UtcNow;
                connection.Update(candidate);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task AcceptAsync(
        int candidateId,
        PreparedMeaningInput input,
        CardDirectionPreference cardDirectionPreference)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Definition))
        {
            throw new ArgumentException("A definition is required.", nameof(input));
        }

        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
                    ?? throw new InvalidOperationException("The preparation candidate does not exist.");
                EnsureCurrentCandidate(connection, candidate);
                var session = connection.Find<PreparationSessionEntity>(candidate.SessionId)
                    ?? throw new InvalidOperationException("The preparation session does not exist.");
                var word = connection.Find<WordEntity>(candidate.WordId)
                    ?? throw new InvalidOperationException("The preparation word does not exist.");
                if (connection.Table<MeaningEntity>()
                    .Any(meaning => meaning.WordId == word.Id && meaning.ConfirmedByUser))
                {
                    throw new InvalidOperationException("This vocabulary item is already prepared.");
                }

                var contextData = BuildContextData(connection, word.Id);
                var explanationLanguage = contextData.FirstOrDefault()?.ExplanationLanguage ?? word.Language;
                var now = clock.UtcNow;
                var preparedTokenKind = !string.IsNullOrWhiteSpace(input.AcronymExpansion)
                    && AcronymExpansionDetector.IsAcronymCandidate(word.CanonicalTerm)
                        ? KnownFirst.Core.Text.TokenKind.Acronym
                        : word.TokenKind;
                var aliases = input.AcceptedAliases
                    .Select(alias => alias.Trim())
                    .Where(alias => alias.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var meaning = new MeaningEntity
                {
                    WordId = word.Id,
                    SourceLanguage = word.Language,
                    ExplanationLanguage = explanationLanguage,
                    DisplayTerm = word.CanonicalTerm,
                    TokenKind = preparedTokenKind,
                    SelectedMeaningId = input.SelectedMeaningId ?? string.Empty,
                    AcronymExpansion = input.AcronymExpansion?.Trim() ?? string.Empty,
                    Translation = input.Translation?.Trim() ?? string.Empty,
                    Definition = input.Definition.Trim(),
                    DictionaryExample = input.DictionaryExample?.Trim() ?? string.Empty,
                    AdditionalNote = input.AdditionalNote?.Trim() ?? string.Empty,
                    AcceptedAliasesJson = JsonSerializer.Serialize(aliases),
                    TranslationOrDefinition = string.IsNullOrWhiteSpace(input.Translation)
                        ? input.Definition.Trim()
                        : input.Translation.Trim(),
                    Source = input.ProviderName,
                    SourceProject = input.SourceProject,
                    SourcePageTitle = input.SourcePageTitle,
                    SourceRevisionId = input.SourceRevisionId,
                    Attribution = input.Attribution,
                    ConfirmedByUser = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    PreparedAt = now
                };
                connection.Insert(meaning);
                foreach (var context in contextData.Take(MaximumContextSnapshots))
                {
                    connection.Insert(new ContextSnapshotEntity
                    {
                        MeaningId = meaning.Id,
                        WordId = word.Id,
                        SourceDocumentId = context.DocumentId,
                        SourceDocumentTitle = context.DocumentTitle,
                        Text = context.Text,
                        TargetStart = context.TargetStart,
                        TargetLength = context.TargetLength,
                        NormalizedFingerprint = CreateFingerprint(NormalizeContext(context.Text)),
                        CreatedAtUtc = now
                    });
                }

                foreach (var direction in CardDirectionPreferencePolicy.GetDirections(cardDirectionPreference))
                {
                    connection.Insert(new LearningCardEntity
                    {
                        WordId = word.Id,
                        MeaningId = meaning.Id,
                        Direction = direction,
                        State = CardState.New,
                        DueAtUtc = now,
                        EaseFactor = SimpleSpacedRepetitionScheduler.DefaultEaseFactor,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }

                word.TokenKind = preparedTokenKind;
                word.Status = WordStatus.Prepared;
                word.PreparationState = PreparationState.Prepared;
                word.UpdatedAt = now;
                connection.Update(word);
                CompleteCandidate(connection, session, candidate, PreparationCandidateStatus.Prepared, now);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SkipAsync(int candidateId)
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
                    ?? throw new InvalidOperationException("The preparation candidate does not exist.");
                EnsureCurrentCandidate(connection, candidate);
                var session = connection.Find<PreparationSessionEntity>(candidate.SessionId)!;
                var word = connection.Find<WordEntity>(candidate.WordId)!;
                word.PreparationState = PreparationState.Unprepared;
                word.UpdatedAt = clock.UtcNow;
                connection.Update(word);
                CompleteCandidate(
                    connection,
                    session,
                    candidate,
                    PreparationCandidateStatus.Skipped,
                    clock.UtcNow);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Task<PreparationLookupSource?> GetLookupSourceAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<PreparationSessionEntity>()
            .Where(item => item.Status == PreparationSessionStatus.Active)
            .FirstOrDefaultAsync();
        if (session is null)
        {
            return null;
        }

        var candidate = (await connection.Table<PreparationCandidateEntity>()
                .Where(item => item.SessionId == session.Id)
                .OrderBy(item => item.Order)
                .ToListAsync())
            .FirstOrDefault(item => item.Status is PreparationCandidateStatus.Pending
                or PreparationCandidateStatus.ResultReady
                or PreparationCandidateStatus.Failed);
        if (candidate is null)
        {
            return null;
        }

        var item = await CreateItemAsync(connection, session, candidate);
        var documentIds = (await connection.Table<WordOccurrenceEntity>()
                .Where(occurrence => occurrence.WordId == candidate.WordId)
                .OrderBy(occurrence => occurrence.DocumentId)
                .ToListAsync())
            .Select(occurrence => occurrence.DocumentId)
            .Distinct()
            .ToArray();
        var documentContents = new List<string>(documentIds.Length);
        foreach (var documentId in documentIds)
        {
            var document = await connection.FindAsync<DocumentEntity>(documentId);
            if (document is not null)
            {
                documentContents.Add(document.Content);
            }
        }

        return new PreparationLookupSource(item, string.Join('\n', documentContents));
    });

    private static async Task<PreparationItem> CreateItemAsync(
        SQLiteAsyncConnection connection,
        PreparationSessionEntity session,
        PreparationCandidateEntity candidate)
    {
        var word = await connection.FindAsync<WordEntity>(candidate.WordId)
            ?? throw new InvalidOperationException("The preparation word does not exist.");
        var occurrences = await connection.Table<WordOccurrenceEntity>()
            .Where(item => item.WordId == word.Id)
            .OrderBy(item => item.DocumentId)
            .ThenBy(item => item.Order)
            .ToListAsync();
        var contexts = new List<PreparationContext>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        string explanationLanguage = word.Language;
        foreach (var occurrence in occurrences)
        {
            var document = await connection.FindAsync<DocumentEntity>(occurrence.DocumentId);
            var sentence = await connection.FindAsync<SentenceSpanEntity>(occurrence.SentenceSpanId);
            if (document is null || sentence is null || !TryCreateContext(document, sentence, occurrence, out var context))
            {
                continue;
            }

            explanationLanguage = document.ExplanationLanguage;
            if (fingerprints.Add(CreateFingerprint(NormalizeContext(context.Text))))
            {
                contexts.Add(context);
            }

            if (contexts.Count == MaximumContextSnapshots)
            {
                break;
            }
        }

        return new PreparationItem(
            session.Id,
            candidate.Id,
            word.Id,
            word.CanonicalTerm,
            word.TokenKind,
            word.Language,
            explanationLanguage,
            word.TotalOccurrenceCount,
            candidate.Order + 1,
            session.TotalItems,
            session.Method,
            candidate.Status,
            contexts,
            DeserializeResult(candidate.ResultJson),
            candidate.SelectedMeaningIndex,
            string.IsNullOrWhiteSpace(candidate.LastErrorCode) ? null : candidate.LastErrorCode);
    }

    private static List<ContextData> BuildContextData(SQLiteConnection connection, int wordId)
    {
        var result = new List<ContextData>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = connection.Table<WordOccurrenceEntity>()
            .Where(item => item.WordId == wordId)
            .OrderBy(item => item.DocumentId)
            .ThenBy(item => item.Order)
            .ToList();
        foreach (var occurrence in occurrences)
        {
            var document = connection.Find<DocumentEntity>(occurrence.DocumentId);
            var sentence = connection.Find<SentenceSpanEntity>(occurrence.SentenceSpanId);
            if (document is null || sentence is null || !TryCreateContext(document, sentence, occurrence, out var context))
            {
                continue;
            }

            var fingerprint = CreateFingerprint(NormalizeContext(context.Text));
            if (fingerprints.Add(fingerprint))
            {
                result.Add(new ContextData(
                    context.DocumentId,
                    context.DocumentTitle,
                    document.ExplanationLanguage,
                    context.Text,
                    context.TargetStart,
                    context.TargetLength));
            }

            if (result.Count == MaximumContextSnapshots)
            {
                break;
            }
        }

        return result;
    }

    private static bool TryCreateContext(
        DocumentEntity document,
        SentenceSpanEntity sentence,
        WordOccurrenceEntity occurrence,
        out PreparationContext context)
    {
        var relativeStart = occurrence.StartPosition - sentence.StartPosition;
        if (relativeStart < 0
            || occurrence.Length < 0
            || relativeStart + occurrence.Length > sentence.Length
            || sentence.StartPosition + sentence.Length > document.Content.Length)
        {
            context = null!;
            return false;
        }

        var text = document.Content.Substring(sentence.StartPosition, sentence.Length);
        if (!string.Equals(
            text.Substring(relativeStart, occurrence.Length),
            occurrence.SurfaceForm,
            StringComparison.Ordinal))
        {
            context = null!;
            return false;
        }

        context = new PreparationContext(
            document.Id,
            document.Title,
            text,
            relativeStart,
            occurrence.Length);
        return true;
    }

    private static bool ReviewIsResolved(SQLiteConnection connection, int wordId) =>
        !connection.Table<ReviewCandidateEntity>()
            .Any(candidate => candidate.WordId == wordId && candidate.Status == WordStatus.Unreviewed);

    private static void EnsureCurrentCandidate(
        SQLiteConnection connection,
        PreparationCandidateEntity candidate)
    {
        var session = connection.Find<PreparationSessionEntity>(candidate.SessionId);
        if (session?.Status != PreparationSessionStatus.Active)
        {
            throw new InvalidOperationException("The preparation session is not active.");
        }

        var current = connection.Table<PreparationCandidateEntity>()
            .Where(item => item.SessionId == session.Id)
            .OrderBy(item => item.Order)
            .ToList()
            .FirstOrDefault(item => item.Status is PreparationCandidateStatus.Pending
                or PreparationCandidateStatus.ResultReady
                or PreparationCandidateStatus.Failed);
        if (current?.Id != candidate.Id)
        {
            throw new InvalidOperationException("The submitted item is not the current preparation candidate.");
        }
    }

    private static void CompleteCandidate(
        SQLiteConnection connection,
        PreparationSessionEntity session,
        PreparationCandidateEntity candidate,
        PreparationCandidateStatus status,
        DateTime now)
    {
        candidate.Status = status;
        candidate.UpdatedAtUtc = now;
        connection.Update(candidate);
        session.CompletedItems++;
        session.UpdatedAtUtc = now;
        if (session.CompletedItems >= session.TotalItems)
        {
            session.Status = PreparationSessionStatus.Completed;
            session.CompletedAtUtc = now;
        }

        connection.Update(session);
    }

    private static LexicalResult? DeserializeResult(string resultJson) =>
        string.IsNullOrWhiteSpace(resultJson)
            ? null
            : JsonSerializer.Deserialize<LexicalResult>(resultJson, SerializerOptions);

    private static string NormalizeLemma(string value) =>
        value.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static string NormalizeContext(string value) =>
        WhitespaceRegex().Replace(value.Replace("\r\n", "\n").Replace('\r', '\n').Trim(), " ")
            .Normalize(NormalizationForm.FormC);

    private static string CreateFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record PreparationLookupSource(PreparationItem Item, string DocumentContent);

    private sealed record ContextData(
        int DocumentId,
        string DocumentTitle,
        string ExplanationLanguage,
        string Text,
        int TargetStart,
        int TargetLength);
}
