using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.Lexical;
using SQLite;
using System.Diagnostics;
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
    private readonly object _prefetchSync = new();
    private CancellationTokenSource? _prefetchCancellation;
    private Task<PrefetchedLookup?>? _prefetchTask;
    private int? _prefetchOriginCandidateId;
#if DEBUG
    private const int MaximumTimingMeasurements = 200;
    private readonly object _timingSync = new();
    private readonly List<PreparationTimingMeasurement> _timingMeasurements = [];
    private long _timingSequence;
#endif

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

        var queryStarted = Stopwatch.GetTimestamp();
        var candidate = await FindCurrentCandidateAsync(connection, session.Id);
        RecordTiming(candidate?.Id, "Get current", PreparationTimingPhase.NextCandidateQuery, queryStarted);
        if (candidate is null)
        {
            return null;
        }

        var contextStarted = Stopwatch.GetTimestamp();
        var item = await CreateItemAsync(connection, session, candidate);
        RecordTiming(candidate.Id, "Get current", PreparationTimingPhase.ContextLoading, contextStarted);
        return item;
    });

    public async Task<PreparationItem?> LookupCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var item = await GetLookupItemAsync();
            if (item is null)
            {
                return null;
            }

            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(item.CandidateId)
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

            var result = await TryConsumePrefetchAsync(item.CandidateId, cancellationToken);
            if (result is null)
            {
                var documentContent = await GetDocumentContentAsync(item.WordId);
                var networkStarted = Stopwatch.GetTimestamp();
                result = await lexicalEnrichment.EnrichAsync(
                    CreateLookupRequest(item),
                    documentContent,
                    item.Contexts.FirstOrDefault()?.Text,
                    cancellationToken);
                RecordTiming(
                    item.CandidateId,
                    "Lookup",
                    PreparationTimingPhase.NetworkWork,
                    networkStarted);
            }
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(item.CandidateId)
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

            var updated = item with
            {
                Status = result.HasUsableData
                    ? PreparationCandidateStatus.ResultReady
                    : PreparationCandidateStatus.Failed,
                Result = result,
                SelectedMeaningIndex = 0,
                LastErrorCode = result.ErrorCode
            };
            if (result.HasUsableData)
            {
                BeginPrefetch(item);
            }

            return updated;
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
        var validationStarted = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Definition))
        {
            throw new ArgumentException("A definition is required.", nameof(input));
        }

        RecordTiming(candidateId, "Accept", PreparationTimingPhase.Validation, validationStarted);

        await _operationGate.WaitAsync();
        try
        {
            var transactionStarted = Stopwatch.GetTimestamp();
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

                var contextStarted = Stopwatch.GetTimestamp();
                var contextData = BuildContextData(connection, word.Id);
                RecordTiming(candidateId, "Accept", PreparationTimingPhase.ContextLoading, contextStarted);
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
                var meaningSaveStarted = Stopwatch.GetTimestamp();
                var meaning = new MeaningEntity
                {
                    WordId = word.Id,
                    SourceLanguage = word.Language,
                    ExplanationLanguage = explanationLanguage,
                    DisplayTerm = string.IsNullOrWhiteSpace(input.CanonicalLearningTerm)
                        ? word.CanonicalTerm
                        : input.CanonicalLearningTerm.Trim(),
                    EncounteredSurfaceForm = input.EncounteredSurfaceForm?.Trim() ?? string.Empty,
                    GrammaticalRelationship = input.GrammaticalRelationship?.Trim() ?? string.Empty,
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

                RecordTiming(
                    candidateId,
                    "Accept",
                    PreparationTimingPhase.PreparedMeaningSave,
                    meaningSaveStarted);

                var cardCreationStarted = Stopwatch.GetTimestamp();
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

                RecordTiming(
                    candidateId,
                    "Accept",
                    PreparationTimingPhase.LearningCardCreation,
                    cardCreationStarted);

                var sessionUpdateStarted = Stopwatch.GetTimestamp();
                word.TokenKind = preparedTokenKind;
                word.Status = WordStatus.Prepared;
                word.PreparationState = PreparationState.Prepared;
                word.UpdatedAt = now;
                connection.Update(word);
                CompleteCandidate(connection, session, candidate, PreparationCandidateStatus.Prepared, now);
                RecordTiming(
                    candidateId,
                    "Accept",
                    PreparationTimingPhase.SessionUpdate,
                    sessionUpdateStarted);
                return true;
            });
            RecordTiming(
                candidateId,
                "Accept",
                PreparationTimingPhase.DatabaseTransaction,
                transactionStarted);
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

    public Task MarkKnownAsync(int candidateId) =>
        CompleteWithoutLearningAsync(
            candidateId,
            WordStatus.Known,
            PreparationCandidateStatus.MarkedKnown);

    public Task ExcludeAsync(int candidateId) =>
        CompleteWithoutLearningAsync(
            candidateId,
            WordStatus.Ignored,
            PreparationCandidateStatus.Excluded);

    public async Task CancelPrefetchAsync()
    {
        CancellationTokenSource? cancellation;
        Task<PrefetchedLookup?>? task;
        lock (_prefetchSync)
        {
            cancellation = _prefetchCancellation;
            task = _prefetchTask;
            _prefetchCancellation = null;
            _prefetchTask = null;
            _prefetchOriginCandidateId = null;
        }

        cancellation?.Cancel();
        if (task is not null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

#if DEBUG
    public IReadOnlyList<PreparationTimingMeasurement> GetTimingDiagnostics()
    {
        lock (_timingSync)
        {
            return _timingMeasurements.ToArray();
        }
    }

    public void RecordUiTransition(int? candidateId, TimeSpan elapsed) => RecordTiming(
        candidateId,
        "Accept to next item",
        PreparationTimingPhase.UiTransition,
        elapsed);
#endif

    private async Task CompleteWithoutLearningAsync(
        int candidateId,
        WordStatus finalWordStatus,
        PreparationCandidateStatus finalCandidateStatus)
    {
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
                if (word.Status != WordStatus.UnknownBacklog
                    || connection.Table<MeaningEntity>().Any(meaning => meaning.WordId == word.Id)
                    || connection.Table<LearningCardEntity>().Any(card => card.WordId == word.Id))
                {
                    throw new InvalidOperationException("Only unprepared Unknown vocabulary can be completed without learning.");
                }

                connection.Execute("DELETE FROM ContextSnapshots WHERE WordId = ?", word.Id);
                connection.Execute("DELETE FROM WordOccurrences WHERE WordId = ?", word.Id);
                connection.Execute("DELETE FROM WordForms WHERE WordId = ?", word.Id);
                connection.Execute("DELETE FROM ReviewStates WHERE WordId = ?", word.Id);

                var now = clock.UtcNow;
                word.Status = finalWordStatus;
                word.PreparationState = PreparationState.Unprepared;
                word.TotalOccurrenceCount = 0;
                word.DocumentCount = 0;
                word.UpdatedAt = now;
                connection.Update(word);

                candidate.ResultJson = string.Empty;
                candidate.SelectedMeaningIndex = 0;
                candidate.LastErrorCode = string.Empty;
                CompleteCandidate(connection, session, candidate, finalCandidateStatus, now);
                DocumentCleanupOperations.CleanupEligibleDocuments(connection);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Task<PreparationItem?> GetLookupItemAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<PreparationSessionEntity>()
            .Where(item => item.Status == PreparationSessionStatus.Active)
            .FirstOrDefaultAsync();
        if (session is null)
        {
            return null;
        }

        var queryStarted = Stopwatch.GetTimestamp();
        var candidate = await FindCurrentCandidateAsync(connection, session.Id);
        RecordTiming(candidate?.Id, "Lookup", PreparationTimingPhase.NextCandidateQuery, queryStarted);
        if (candidate is null)
        {
            return null;
        }

        var contextStarted = Stopwatch.GetTimestamp();
        var item = await CreateItemAsync(connection, session, candidate);
        RecordTiming(candidate.Id, "Lookup", PreparationTimingPhase.ContextLoading, contextStarted);
        return item;
    });

    private Task<string> GetDocumentContentAsync(int wordId) => database.ReadAsync(
        connection => LoadDocumentContentAsync(connection, wordId));

    private static async Task<PreparationLookupSource> CreateLookupSourceAsync(
        SQLiteAsyncConnection connection,
        PreparationSessionEntity session,
        PreparationCandidateEntity candidate)
    {
        var item = await CreateItemAsync(connection, session, candidate);
        var documentContent = await LoadDocumentContentAsync(connection, candidate.WordId);
        return new PreparationLookupSource(item, documentContent);
    }

    private static async Task<string> LoadDocumentContentAsync(
        SQLiteAsyncConnection connection,
        int wordId)
    {
        var documentIds = (await connection.Table<WordOccurrenceEntity>()
                .Where(occurrence => occurrence.WordId == wordId)
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

        return string.Join('\n', documentContents);
    }

    private void BeginPrefetch(PreparationItem currentItem)
    {
        lock (_prefetchSync)
        {
            if (_prefetchOriginCandidateId == currentItem.CandidateId && _prefetchTask is not null)
            {
                return;
            }

            _prefetchCancellation?.Cancel();
            _prefetchCancellation?.Dispose();
            _prefetchCancellation = new CancellationTokenSource();
            _prefetchOriginCandidateId = currentItem.CandidateId;
            _prefetchTask = PrefetchNextAsync(
                currentItem.SessionId,
                currentItem.Position - 1,
                _prefetchCancellation.Token);
        }
    }

    private async Task<PrefetchedLookup?> PrefetchNextAsync(
        int sessionId,
        int currentOrder,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = await database.ReadAsync(async connection =>
            {
                var session = await connection.FindAsync<PreparationSessionEntity>(sessionId);
                if (session?.Status != PreparationSessionStatus.Active)
                {
                    return null;
                }

                var candidate = await connection.Table<PreparationCandidateEntity>()
                    .Where(item => item.SessionId == sessionId
                        && item.Order > currentOrder
                        && item.Status == PreparationCandidateStatus.Pending)
                    .OrderBy(item => item.Order)
                    .FirstOrDefaultAsync();
                return candidate is null
                    ? null
                    : await CreateLookupSourceAsync(connection, session, candidate);
            });
            if (source is null)
            {
                return null;
            }

            var networkStarted = Stopwatch.GetTimestamp();
            var result = await lexicalEnrichment.EnrichAsync(
                CreateLookupRequest(source.Item),
                source.DocumentContent,
                source.Item.Contexts.FirstOrDefault()?.Text,
                cancellationToken);
            RecordTiming(
                source.Item.CandidateId,
                "Prefetch",
                PreparationTimingPhase.NetworkWork,
                networkStarted);
            return new PrefetchedLookup(source.Item.CandidateId, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<LexicalResult?> TryConsumePrefetchAsync(
        int candidateId,
        CancellationToken cancellationToken)
    {
        Task<PrefetchedLookup?>? task;
        lock (_prefetchSync)
        {
            task = _prefetchTask;
        }

        if (task is null)
        {
            return null;
        }

        var prefetched = await task.WaitAsync(cancellationToken);
        if (prefetched?.CandidateId != candidateId)
        {
            return null;
        }

        lock (_prefetchSync)
        {
            if (ReferenceEquals(task, _prefetchTask))
            {
                _prefetchCancellation?.Dispose();
                _prefetchCancellation = null;
                _prefetchTask = null;
                _prefetchOriginCandidateId = null;
            }
        }

        return prefetched.Result;
    }

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
            .Where(item => item.SessionId == session.Id
                && (item.Status == PreparationCandidateStatus.Pending
                    || item.Status == PreparationCandidateStatus.ResultReady
                    || item.Status == PreparationCandidateStatus.Failed))
            .OrderBy(item => item.Order)
            .FirstOrDefault();
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

    private static async Task<PreparationCandidateEntity?> FindCurrentCandidateAsync(
        SQLiteAsyncConnection connection,
        int sessionId) => (PreparationCandidateEntity?)await connection.Table<PreparationCandidateEntity>()
            .Where(item => item.SessionId == sessionId
                && (item.Status == PreparationCandidateStatus.Pending
                    || item.Status == PreparationCandidateStatus.ResultReady
                    || item.Status == PreparationCandidateStatus.Failed))
            .OrderBy(item => item.Order)
            .FirstOrDefaultAsync();

#if DEBUG
    private void RecordTiming(
        int? candidateId,
        string operation,
        PreparationTimingPhase phase,
        long startedTimestamp) => RecordTiming(
            candidateId,
            operation,
            phase,
            Stopwatch.GetElapsedTime(startedTimestamp));

    private void RecordTiming(
        int? candidateId,
        string operation,
        PreparationTimingPhase phase,
        TimeSpan elapsed)
    {
        var measurement = new PreparationTimingMeasurement(
            Interlocked.Increment(ref _timingSequence),
            candidateId,
            operation,
            phase,
            elapsed.TotalMilliseconds,
            clock.UtcNow);
        lock (_timingSync)
        {
            _timingMeasurements.Add(measurement);
            if (_timingMeasurements.Count > MaximumTimingMeasurements)
            {
                _timingMeasurements.RemoveRange(
                    0,
                    _timingMeasurements.Count - MaximumTimingMeasurements);
            }
        }
    }
#else
    private static void RecordTiming(
        int? candidateId,
        string operation,
        PreparationTimingPhase phase,
        long startedTimestamp)
    {
    }
#endif

    private static string NormalizeLemma(string value) =>
        value.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static LexicalLookupRequest CreateLookupRequest(PreparationItem item) => new(
        item.Term,
        NormalizeLemma(item.Term),
        item.TokenKind,
        item.SourceLanguage,
        item.ExplanationLanguage);

    private static string NormalizeContext(string value) =>
        WhitespaceRegex().Replace(value.Replace("\r\n", "\n").Replace('\r', '\n').Trim(), " ")
            .Normalize(NormalizationForm.FormC);

    private static string CreateFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record PreparationLookupSource(PreparationItem Item, string DocumentContent);

    private sealed record PrefetchedLookup(int CandidateId, LexicalResult Result);

    private sealed record ContextData(
        int DocumentId,
        string DocumentTitle,
        string ExplanationLanguage,
        string Text,
        int TargetStart,
        int TargetLength);
}
