using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using SQLite;
using System.Text.Json;

namespace KnownFirst.Services.Study;

public sealed class LearningService(
    IKnownFirstDatabase database,
    ISpacedRepetitionScheduler scheduler,
    SpellingAnswerComparer spellingComparer,
    IClock clock) : ILearningService
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public async Task<LearningLoadResult> GetOrStartAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                EnsureActiveSession(connection);
                return true;
            });
            return await LoadAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RevealAnswerAsync(int queueItemId)
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var queueItem = RequireCurrentQueueItem(connection, queueItemId);
                var card = connection.Find<LearningCardEntity>(queueItem.CardId)
                    ?? throw new InvalidOperationException("The learning card does not exist.");
                if (card.Direction != CardDirection.TermToMeaning)
                {
                    throw new InvalidOperationException("Only recognition cards reveal an answer directly.");
                }

                queueItem.AnswerRevealed = true;
                connection.Update(queueItem);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<SpellingSubmissionResult> CheckSpellingAsync(
        int queueItemId,
        string enteredAnswer)
    {
        await _operationGate.WaitAsync();
        try
        {
            return await database.RunInTransactionAsync(connection =>
            {
                var queueItem = RequireCurrentQueueItem(connection, queueItemId);
                var card = connection.Find<LearningCardEntity>(queueItem.CardId)
                    ?? throw new InvalidOperationException("The learning card does not exist.");
                if (card.Direction != CardDirection.MeaningToTerm)
                {
                    throw new InvalidOperationException("Only spelling cards accept a typed answer.");
                }

                var word = connection.Find<WordEntity>(card.WordId)
                    ?? throw new InvalidOperationException("The learning word does not exist.");
                var meaning = connection.Find<MeaningEntity>(card.MeaningId)
                    ?? throw new InvalidOperationException("The prepared meaning does not exist.");
                var aliases = DeserializeAliases(meaning.AcceptedAliasesJson);
                var comparison = spellingComparer.Compare(
                    enteredAnswer,
                    meaning.DisplayTerm,
                    aliases,
                    word.TokenKind,
                    word.Language);
                queueItem.SpellingChecked = true;
                queueItem.SpellingCorrect = comparison.IsCorrect;
                queueItem.AnswerRevealed = true;
                connection.Update(queueItem);

                var ratingPersisted = false;
                if (!comparison.IsCorrect)
                {
                    PersistRating(
                        connection,
                        queueItem,
                        card,
                        ReviewRating.Again,
                        wasTypedAnswer: true,
                        wasCorrect: false);
                    ratingPersisted = true;
                }

                return new SpellingSubmissionResult(
                    comparison.IsCorrect,
                    comparison.EnteredAnswer,
                    comparison.ExpectedAnswer,
                    comparison.Difference,
                    comparison.MatchedAlias,
                    ratingPersisted);
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<LearningLoadResult> RateAsync(int queueItemId, ReviewRating rating)
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var queueItem = RequireCurrentQueueItem(connection, queueItemId);
                var card = connection.Find<LearningCardEntity>(queueItem.CardId)
                    ?? throw new InvalidOperationException("The learning card does not exist.");
                if (card.Direction == CardDirection.TermToMeaning && !queueItem.AnswerRevealed)
                {
                    throw new InvalidOperationException("The answer must be revealed before rating.");
                }

                if (card.Direction == CardDirection.MeaningToTerm)
                {
                    if (!queueItem.SpellingChecked || !queueItem.SpellingCorrect)
                    {
                        throw new InvalidOperationException("A correct typed answer is required before rating.");
                    }

                    if (rating == ReviewRating.Again)
                    {
                        throw new InvalidOperationException("A correct spelling answer allows Hard, Good, or Easy.");
                    }
                }

                PersistRating(
                    connection,
                    queueItem,
                    card,
                    rating,
                    card.Direction == CardDirection.MeaningToTerm,
                    wasCorrect: true);
                return true;
            });
            return await LoadAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> MarkPermanentlyKnownAsync(int wordId, bool confirmed)
    {
        if (!confirmed)
        {
            return false;
        }

        await _operationGate.WaitAsync();
        try
        {
            return await database.RunInTransactionAsync(connection =>
            {
                var word = connection.Find<WordEntity>(wordId);
                if (word is null)
                {
                    return false;
                }

                var cardIds = connection.Table<LearningCardEntity>()
                    .Where(card => card.WordId == wordId)
                    .ToList()
                    .Select(card => card.Id)
                    .ToHashSet();
                var queueItemsToDelete = connection.Table<LearningSessionCardEntity>()
                    .ToList()
                    .Where(item => cardIds.Contains(item.CardId))
                    .ToArray();
                var reviewsToDelete = connection.Table<LearningReviewEntity>()
                    .ToList()
                    .Where(item => cardIds.Contains(item.CardId))
                    .ToArray();
                var affectedLearningSessionIds = queueItemsToDelete
                    .Select(item => item.SessionId)
                    .Concat(reviewsToDelete.Select(item => item.SessionId))
                    .ToHashSet();
                foreach (var queueItem in queueItemsToDelete)
                {
                    connection.Delete(queueItem);
                }

                foreach (var review in reviewsToDelete)
                {
                    connection.Delete(review);
                }

                foreach (var card in connection.Table<LearningCardEntity>()
                             .Where(item => item.WordId == wordId)
                             .ToList())
                {
                    connection.Delete(card);
                }

                var meaningIds = connection.Table<MeaningEntity>()
                    .Where(item => item.WordId == wordId)
                    .ToList()
                    .Select(item => item.Id)
                    .ToHashSet();
                foreach (var snapshot in connection.Table<ContextSnapshotEntity>()
                             .ToList()
                             .Where(item => meaningIds.Contains(item.MeaningId)))
                {
                    connection.Delete(snapshot);
                }

                connection.Execute("DELETE FROM Meanings WHERE WordId = ?", wordId);
                var preparationCandidatesToDelete = connection.Table<PreparationCandidateEntity>()
                    .Where(item => item.WordId == wordId)
                    .ToList();
                var affectedPreparationSessionIds = preparationCandidatesToDelete
                    .Select(item => item.SessionId)
                    .ToHashSet();
                foreach (var candidate in preparationCandidatesToDelete)
                {
                    connection.Delete(candidate);
                }
                connection.Execute("DELETE FROM WordOccurrences WHERE WordId = ?", wordId);
                connection.Execute("DELETE FROM WordForms WHERE WordId = ?", wordId);
                connection.Execute("DELETE FROM ReviewStates WHERE WordId = ?", wordId);

                word.Status = WordStatus.Known;
                word.PreparationState = PreparationState.Unprepared;
                word.TotalOccurrenceCount = 0;
                word.DocumentCount = 0;
                word.UpdatedAt = clock.UtcNow;
                connection.Update(word);

                NormalizePreparationSessions(connection, affectedPreparationSessionIds, clock.UtcNow);
                NormalizeLearningSessions(connection, affectedLearningSessionIds, clock.UtcNow);
                DocumentCleanupOperations.CleanupEligibleDocuments(connection);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<int> RunMaintenanceAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            return await database.RunInTransactionAsync(DocumentCleanupOperations.CleanupEligibleDocuments);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void EnsureActiveSession(SQLiteConnection connection)
    {
        if (connection.Table<LearningSessionEntity>()
            .Any(session => session.Status == LearningSessionStatus.Active))
        {
            return;
        }

        if (connection.Table<ReviewSessionEntity>()
            .Any(session => session.Status == ReviewSessionStatus.Active))
        {
            throw new ActiveReviewExistsException();
        }

        var now = clock.UtcNow;
        var cards = connection.Table<LearningCardEntity>().ToList();
        var dueCards = cards
            .Where(card => card.State is not (CardState.New or CardState.Suspended or CardState.Retired)
                && card.DueAtUtc <= now)
            .OrderBy(card => card.DueAtUtc)
            .ThenBy(card => card.Id)
            .ToArray();
        var wordsById = connection.Table<WordEntity>()
            .ToList()
            .ToDictionary(word => word.Id);
        var newCards = cards
            .Where(card => card.State == CardState.New)
            .OrderByDescending(card => wordsById.GetValueOrDefault(card.WordId)?.TotalOccurrenceCount ?? 0)
            .ThenBy(card => wordsById.GetValueOrDefault(card.WordId)?.CreatedAt ?? DateTime.MaxValue)
            .ThenBy(card => wordsById.GetValueOrDefault(card.WordId)?.CanonicalTerm, StringComparer.Ordinal)
            .ThenBy(card => card.Direction)
            .ThenBy(card => card.Id)
            .ToArray();
        var selectedCards = dueCards.Concat(newCards)
            .GroupBy(card => card.Id)
            .Select(group => group.First())
            .ToArray();
        if (selectedCards.Length == 0)
        {
            return;
        }

        var session = new LearningSessionEntity
        {
            Status = LearningSessionStatus.Active,
            TotalCards = selectedCards.Length,
            StartedAtUtc = now,
            UpdatedAtUtc = now
        };
        connection.Insert(session);
        var dueIds = dueCards.Select(card => card.Id).ToHashSet();
        for (var index = 0; index < selectedCards.Length; index++)
        {
            connection.Insert(new LearningSessionCardEntity
            {
                SessionId = session.Id,
                CardId = selectedCards[index].Id,
                QueueOrder = index,
                IsDueCard = dueIds.Contains(selectedCards[index].Id)
            });
        }
    }

    private Task<LearningLoadResult> LoadAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<LearningSessionEntity>()
            .Where(item => item.Status == LearningSessionStatus.Active)
            .FirstOrDefaultAsync();
        if (session is not null)
        {
            var queueItem = (await connection.Table<LearningSessionCardEntity>()
                    .Where(item => item.SessionId == session.Id && !item.IsCompleted)
                    .OrderBy(item => item.QueueOrder)
                    .ToListAsync())
                .FirstOrDefault();
            if (queueItem is not null)
            {
                return new LearningLoadResult(
                    await CreateCardViewAsync(connection, session, queueItem),
                    null);
            }
        }

        var completed = await connection.Table<LearningSessionEntity>()
            .Where(item => item.Status == LearningSessionStatus.Completed)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync();
        return new LearningLoadResult(
            null,
            completed is null ? null : await CreateSummaryAsync(connection, completed));
    });

    private static async Task<LearningCardView> CreateCardViewAsync(
        SQLiteAsyncConnection connection,
        LearningSessionEntity session,
        LearningSessionCardEntity queueItem)
    {
        var card = await connection.FindAsync<LearningCardEntity>(queueItem.CardId)
            ?? throw new InvalidOperationException("The queued card does not exist.");
        var word = await connection.FindAsync<WordEntity>(card.WordId)
            ?? throw new InvalidOperationException("The queued word does not exist.");
        var meaning = await connection.FindAsync<MeaningEntity>(card.MeaningId)
            ?? throw new InvalidOperationException("The queued prepared meaning does not exist.");
        var snapshots = await connection.Table<ContextSnapshotEntity>()
            .Where(item => item.MeaningId == meaning.Id)
            .OrderBy(item => item.Id)
            .ToListAsync();
        var contexts = snapshots
            .Where(IsValidSnapshot)
            .Select(snapshot => new LearningContext(
                snapshot.SourceDocumentTitle,
                snapshot.Text[..snapshot.TargetStart],
                snapshot.Text.Substring(snapshot.TargetStart, snapshot.TargetLength),
                snapshot.Text[(snapshot.TargetStart + snapshot.TargetLength)..]))
            .ToArray();
        return new LearningCardView(
            session.Id,
            queueItem.Id,
            card.Id,
            word.Id,
            card.Direction,
            card.State,
            meaning.DisplayTerm,
            word.TokenKind,
            meaning.SourceLanguage,
            meaning.ExplanationLanguage,
            EmptyToNull(meaning.AcronymExpansion),
            EmptyToNull(meaning.Translation),
            meaning.Definition,
            EmptyToNull(meaning.DictionaryExample),
            meaning.Source,
            meaning.SourceProject,
            meaning.SourcePageTitle,
            meaning.Attribution,
            DeserializeAliases(meaning.AcceptedAliasesJson),
            contexts,
            word.TotalOccurrenceCount,
            queueItem.AnswerRevealed,
            session.CompletedCards,
            session.TotalCards,
            string.IsNullOrWhiteSpace(meaning.EncounteredSurfaceForm)
                ? null
                : meaning.EncounteredSurfaceForm,
            string.IsNullOrWhiteSpace(meaning.GrammaticalRelationship)
                ? null
                : meaning.GrammaticalRelationship,
            meaning.SourceRevisionId);
    }

    private static async Task<LearningSessionSummary> CreateSummaryAsync(
        SQLiteAsyncConnection connection,
        LearningSessionEntity session)
    {
        var nextDue = (await connection.Table<LearningCardEntity>()
                .Where(card => card.State != CardState.Retired && card.State != CardState.Suspended)
                .OrderBy(card => card.DueAtUtc)
                .ToListAsync())
            .Select(card => (DateTime?)card.DueAtUtc)
            .FirstOrDefault();
        var remaining = await connection.Table<WordEntity>()
            .Where(word => word.Status == WordStatus.UnknownBacklog
                && word.PreparationState != PreparationState.Prepared)
            .CountAsync();
        return new LearningSessionSummary(
            session.Id,
            session.CompletedCards,
            session.AgainCount,
            session.HardCount,
            session.GoodCount,
            session.EasyCount,
            nextDue,
            remaining);
    }

    private void PersistRating(
        SQLiteConnection connection,
        LearningSessionCardEntity queueItem,
        LearningCardEntity card,
        ReviewRating rating,
        bool wasTypedAnswer,
        bool wasCorrect)
    {
        if (queueItem.IsCompleted)
        {
            throw new InvalidOperationException("This card was already submitted.");
        }

        var session = connection.Find<LearningSessionEntity>(queueItem.SessionId)
            ?? throw new InvalidOperationException("The learning session does not exist.");
        var currentSchedule = new CardSchedule(
            card.State,
            card.DueAtUtc,
            card.IntervalDays,
            card.EaseFactor,
            card.SuccessfulReviewCount,
            card.LapseCount,
            card.LastReviewedAtUtc,
            card.LastRating);
        var next = scheduler.Schedule(currentSchedule, rating, clock.UtcNow);
        card.State = next.State;
        card.DueAtUtc = next.DueAtUtc;
        card.IntervalDays = next.IntervalDays;
        card.EaseFactor = next.EaseFactor;
        card.SuccessfulReviewCount = next.SuccessfulReviewCount;
        card.LapseCount = next.LapseCount;
        card.LastReviewedAtUtc = next.LastReviewedAtUtc;
        card.LastRating = next.LastRating;
        card.UpdatedAtUtc = clock.UtcNow;
        connection.Update(card);

        connection.Insert(new LearningReviewEntity
        {
            CardId = card.Id,
            SessionId = session.Id,
            Rating = rating,
            WasTypedAnswer = wasTypedAnswer,
            WasCorrect = wasCorrect,
            ReviewedAtUtc = clock.UtcNow,
            DueAtUtc = next.DueAtUtc,
            IntervalDays = next.IntervalDays,
            EaseFactor = next.EaseFactor
        });
        queueItem.IsCompleted = true;
        queueItem.Rating = rating;
        queueItem.CompletedAtUtc = clock.UtcNow;
        connection.Update(queueItem);

        session.CompletedCards++;
        session.UpdatedAtUtc = clock.UtcNow;
        IncrementRating(session, rating);
        if (rating == ReviewRating.Again
            && !queueItem.IsAgainRepeat
            && !connection.Table<LearningSessionCardEntity>()
                .Any(item => item.SessionId == session.Id
                    && item.CardId == card.Id
                    && item.IsAgainRepeat))
        {
            var nextOrder = connection.Table<LearningSessionCardEntity>()
                .Where(item => item.SessionId == session.Id)
                .ToList()
                .Select(item => item.QueueOrder)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            connection.Insert(new LearningSessionCardEntity
            {
                SessionId = session.Id,
                CardId = card.Id,
                QueueOrder = nextOrder,
                IsAgainRepeat = true
            });
            session.TotalCards++;
        }

        var hasRemaining = connection.Table<LearningSessionCardEntity>()
            .Any(item => item.SessionId == session.Id && !item.IsCompleted);
        if (!hasRemaining)
        {
            session.Status = LearningSessionStatus.Completed;
            session.CompletedAtUtc = clock.UtcNow;
        }

        connection.Update(session);
        var word = connection.Find<WordEntity>(card.WordId);
        if (word is not null && word.Status != WordStatus.Known)
        {
            word.Status = WordStatus.Learning;
            word.UpdatedAt = clock.UtcNow;
            connection.Update(word);
        }
    }

    private static LearningSessionCardEntity RequireCurrentQueueItem(
        SQLiteConnection connection,
        int queueItemId)
    {
        var session = connection.Table<LearningSessionEntity>()
            .FirstOrDefault(item => item.Status == LearningSessionStatus.Active)
            ?? throw new InvalidOperationException("There is no active learning session.");
        var current = connection.Table<LearningSessionCardEntity>()
            .Where(item => item.SessionId == session.Id && !item.IsCompleted)
            .OrderBy(item => item.QueueOrder)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The learning session has no current card.");
        if (current.Id != queueItemId)
        {
            throw new InvalidOperationException("The submitted card is not the current learning card.");
        }

        return current;
    }

    private static void NormalizeLearningSessions(
        SQLiteConnection connection,
        IReadOnlySet<int> sessionIds,
        DateTime now)
    {
        foreach (var sessionId in sessionIds)
        {
            var session = connection.Find<LearningSessionEntity>(sessionId);
            if (session is null)
            {
                continue;
            }

            var rows = connection.Table<LearningSessionCardEntity>()
                .Where(item => item.SessionId == session.Id)
                .ToList();
            var reviews = connection.Table<LearningReviewEntity>()
                .Where(item => item.SessionId == session.Id)
                .ToList();
            if (rows.Count == 0 && reviews.Count == 0)
            {
                connection.Delete(session);
                continue;
            }

            session.TotalCards = rows.Count;
            session.CompletedCards = rows.Count(row => row.IsCompleted);
            session.AgainCount = reviews.Count(review => review.Rating == ReviewRating.Again);
            session.HardCount = reviews.Count(review => review.Rating == ReviewRating.Hard);
            session.GoodCount = reviews.Count(review => review.Rating == ReviewRating.Good);
            session.EasyCount = reviews.Count(review => review.Rating == ReviewRating.Easy);
            session.UpdatedAtUtc = now;
            if (rows.Count > 0 && rows.All(row => row.IsCompleted))
            {
                session.Status = LearningSessionStatus.Completed;
                session.CompletedAtUtc ??= now;
            }
            else
            {
                session.Status = LearningSessionStatus.Active;
                session.CompletedAtUtc = null;
            }

            connection.Update(session);
        }
    }

    private static void NormalizePreparationSessions(
        SQLiteConnection connection,
        IReadOnlySet<int> sessionIds,
        DateTime now)
    {
        foreach (var sessionId in sessionIds)
        {
            var session = connection.Find<PreparationSessionEntity>(sessionId);
            if (session is null)
            {
                continue;
            }

            if (session.Status == PreparationSessionStatus.Cancelled)
            {
                continue;
            }

            var candidates = connection.Table<PreparationCandidateEntity>()
                .Where(item => item.SessionId == sessionId)
                .OrderBy(item => item.Order)
                .ToList();
            if (candidates.Count == 0)
            {
                connection.Delete(session);
                continue;
            }

            for (var index = 0; index < candidates.Count; index++)
            {
                if (candidates[index].Order == index)
                {
                    continue;
                }

                candidates[index].Order = index;
                connection.Update(candidates[index]);
            }

            session.TotalItems = candidates.Count;
            session.CompletedItems = candidates.Count(candidate =>
                candidate.Status is PreparationCandidateStatus.Prepared
                    or PreparationCandidateStatus.Skipped
                    or PreparationCandidateStatus.MarkedKnown
                    or PreparationCandidateStatus.Excluded);
            session.UpdatedAtUtc = now;
            var isComplete = session.CompletedItems == session.TotalItems;
            session.Status = isComplete
                ? PreparationSessionStatus.Completed
                : PreparationSessionStatus.Active;
            session.CompletedAtUtc = isComplete ? session.CompletedAtUtc ?? now : null;
            connection.Update(session);
        }
    }

    private static void IncrementRating(LearningSessionEntity session, ReviewRating rating)
    {
        switch (rating)
        {
            case ReviewRating.Again:
                session.AgainCount++;
                break;
            case ReviewRating.Hard:
                session.HardCount++;
                break;
            case ReviewRating.Good:
                session.GoodCount++;
                break;
            case ReviewRating.Easy:
                session.EasyCount++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rating));
        }
    }

    private static bool IsValidSnapshot(ContextSnapshotEntity snapshot) =>
        snapshot.TargetStart >= 0
        && snapshot.TargetLength >= 0
        && snapshot.TargetStart + snapshot.TargetLength <= snapshot.Text.Length;

    private static string[] DeserializeAliases(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
