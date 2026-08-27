using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Time;
using SQLite;
using System.Text.Json;

namespace KnownFirst.Services.Study;

public sealed class LearningService : ILearningService
{
    private readonly IKnownFirstDatabase database;
    private readonly ISpacedRepetitionScheduler scheduler;
    private readonly SpellingAnswerComparer spellingComparer;
    private readonly IClock clock;
    private readonly IAppSettingsService? appSettings;
    private readonly ISchema8LearningFailureInjector? schema8FailureInjector;
    private readonly ILearningTimezoneResolver _timezoneResolver;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    /// <summary>
    /// The one in-memory Schema-8 answer-variant handoff between a correct typed check and the rating that
    /// consumes it (KF-MEANING-001 Slice 4). Protected by <see cref="_operationGate"/>, which serialises every
    /// public operation of this instance. Two service instances deliberately do not share it: an instance
    /// without a valid same-queue handoff fails closed rather than fabricating target credit.
    /// </summary>
    private Schema8PendingMatch? _schema8PendingMatch;

    public LearningService(
        IKnownFirstDatabase database,
        ISpacedRepetitionScheduler scheduler,
        SpellingAnswerComparer spellingComparer,
        IClock clock)
        : this(database, scheduler, spellingComparer, clock, null, null, null)
    {
    }

    public LearningService(
        IKnownFirstDatabase database,
        ISpacedRepetitionScheduler scheduler,
        SpellingAnswerComparer spellingComparer,
        IClock clock,
        IAppSettingsService? appSettings,
        ISchema8LearningFailureInjector? schema8FailureInjector = null,
        ILearningTimezoneResolver? timezoneResolver = null)
    {
        this.database = database;
        this.scheduler = scheduler;
        this.spellingComparer = spellingComparer;
        this.clock = clock;
        this.appSettings = appSettings;
        this.schema8FailureInjector = schema8FailureInjector;
        _timezoneResolver = timezoneResolver ?? new LearningTimezoneResolver();
    }

    private static bool IsSchema8OrNewer(LearningSchemaCapabilityResult capability) =>
        capability is LearningSchema8CapabilityResult
            or LearningSchema9CapabilityResult
            or LearningSchema10CapabilityResult
            or LearningSchema11CapabilityResult
            or LearningSchema12CapabilityResult;

    public async Task<LearningLoadResult> GetOrStartAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            // Schema 8 computes the complete result — including any session normalisation/finalisation — inside
            // one transaction and never performs a second read. Schema 7 keeps its existing two-step shape.
            var schema8Result = await database.RunInTransactionAsync<LearningLoadResult?>(connection =>
            {
                if (IsSchema8OrNewer(LearningSchemaCapability.Resolve(connection)))
                {
                    return GetOrStartSchema8(connection);
                }

                EnsureActiveSession(connection);
                return null;
            });
            return schema8Result ?? await LoadAsync();
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
                if (IsSchema8OrNewer(LearningSchemaCapability.Resolve(connection)))
                {
                    RevealAnswerSchema8(connection, queueItemId);
                    return true;
                }

                var queueItem = RequireCurrentQueueItem(connection, queueItemId);
                var card = connection.Find<LearningCardEntity>(queueItem.CardId)
                    ?? throw new InvalidOperationException("The learning card does not exist.");
                var word = connection.Find<WordEntity>(card.WordId)
                    ?? throw new InvalidOperationException("The learning word does not exist.");
                if (ResolveInteraction(word, card.Direction) != LearningInteractionMode.Reading)
                {
                    throw new InvalidOperationException("Only reading-mode cards reveal an answer directly.");
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
            // One transaction for both schemas. The Schema-8 pending-match handoff is stored only after the
            // transaction has committed successfully.
            var outcome = await database.RunInTransactionAsync(connection =>
                IsSchema8OrNewer(LearningSchemaCapability.Resolve(connection))
                    ? CheckSpellingSchema8(connection, queueItemId, enteredAnswer)
                    : new Schema8SpellingOutcome(
                        CheckSpellingSchema7(connection, queueItemId, enteredAnswer), null, IsSchema8: false));

            if (outcome.IsSchema8)
            {
                if (outcome.PendingMatch is not null)
                {
                    // Stored only now, i.e. only after the transaction has committed successfully.
                    _schema8PendingMatch = outcome.PendingMatch;
                }
                else
                {
                    // The incorrect typed path persisted Again: clear the handoff of THIS queue row only, never
                    // one belonging to another queue row.
                    ClearSchema8PendingMatch(queueItemId);
                }
            }

            return outcome.Result;
        }
        catch (Schema8LearningDataException)
        {
            ClearSchema8PendingMatch(queueItemId);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// The unchanged Schema-7 typed-answer path, extracted verbatim so the capability dispatch above can call
    /// it. Every database write, comparison input and thrown message is identical to the pre-Slice-4 body.
    /// </summary>
    private SpellingSubmissionResult CheckSpellingSchema7(
        SQLiteConnection connection,
        int queueItemId,
        string enteredAnswer)
    {
        {
            {
                var queueItem = RequireCurrentQueueItem(connection, queueItemId);
                var card = connection.Find<LearningCardEntity>(queueItem.CardId)
                    ?? throw new InvalidOperationException("The learning card does not exist.");
                var word = connection.Find<WordEntity>(card.WordId)
                    ?? throw new InvalidOperationException("The learning word does not exist.");
                var interaction = ResolveInteraction(word, card.Direction);
                if (interaction != LearningInteractionMode.Typing)
                {
                    throw new InvalidOperationException("Only typing-mode cards accept a typed answer.");
                }

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
                        word,
                        ReviewRating.Again,
                        interaction,
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
            }
        }
    }

    public async Task<LearningLoadResult> RateAsync(int queueItemId, ReviewRating rating)
    {
        await _operationGate.WaitAsync();
        try
        {
            var schema8Outcome = await database.RunInTransactionAsync<Schema8RatingOutcome?>(connection =>
            {
                if (IsSchema8OrNewer(LearningSchemaCapability.Resolve(connection)))
                {
                    return PersistRatingSchema8(
                        connection, queueItemId, rating, fromIncorrectSpellingCheck: false);
                }

                var queueItem = RequireCurrentQueueItem(connection, queueItemId);
                var card = connection.Find<LearningCardEntity>(queueItem.CardId)
                    ?? throw new InvalidOperationException("The learning card does not exist.");
                var word = connection.Find<WordEntity>(card.WordId)
                    ?? throw new InvalidOperationException("The learning word does not exist.");
                var interaction = ResolveInteraction(word, card.Direction);
                if (interaction == LearningInteractionMode.Reading && !queueItem.AnswerRevealed)
                {
                    throw new InvalidOperationException("The answer must be revealed before rating.");
                }

                if (interaction == LearningInteractionMode.Typing)
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
                    word,
                    rating,
                    interaction,
                    interaction == LearningInteractionMode.Typing,
                    wasCorrect: rating != ReviewRating.Again);
                return null;
            });

            if (schema8Outcome is not null)
            {
                // A committed Schema-8 rating consumes and clears the handoff for that queue row.
                ClearSchema8PendingMatch(queueItemId);
                return schema8Outcome.Result;
            }

            return await LoadAsync();
        }
        catch (Schema8LearningDataException)
        {
            // Permanent data/state rejection: the handoff for that queue row is discarded so a later call can
            // never consume stale evidence. An injected rollback or transient failure surfaces as a different
            // exception type and therefore preserves the handoff for an identical retry.
            ClearSchema8PendingMatch(queueItemId);
            throw;
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
                if (IsSchema8OrNewer(LearningSchemaCapability.Resolve(connection)))
                {
                    return MarkPermanentlyKnownSchema8(connection, wordId);
                }

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
                word.AutomaticInteractionMode = LearningInteractionMode.Reading;
                word.ConsecutiveRecallSuccessCount = 0;
                word.ConsecutiveTypingSuccessCount = 0;
                word.ConsecutiveTypingFailureCount = 0;
                word.MasteryReviewExtensionScheduled = false;
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

    private async Task<LearningCardView> CreateCardViewAsync(
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
            ResolveInteraction(word, card.Direction),
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
            meaning.SourceRevisionId,
            queueItem.IsAgainRepeat);
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
        WordEntity word,
        ReviewRating rating,
        LearningInteractionMode interaction,
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
        var reviewedAtUtc = clock.UtcNow;
        var automaticState = ReadAutomaticState(word);
        var isAutomatic = appSettings?.LearningMode == LearningMode.Automatic;
        if (isAutomatic)
        {
            automaticState = interaction == LearningInteractionMode.Reading
                ? AutomaticLearningPolicy.RecordRecallAssessment(
                    automaticState,
                    rating != ReviewRating.Again)
                : AutomaticLearningPolicy.RecordTypingAssessment(automaticState, wasCorrect);
        }

        var isMasteryReview = isAutomatic
            && AutomaticLearningPolicy.IsMasteryReview(currentSchedule);
        var masteryAchieved = isMasteryReview
            && wasTypedAnswer
            && wasCorrect
            && AutomaticLearningPolicy.HasTypingMastery(automaticState);
        var next = scheduler.Schedule(currentSchedule, rating, reviewedAtUtc);
        if (isMasteryReview
            && rating != ReviewRating.Again
            && !masteryAchieved
            && !automaticState.MasteryReviewExtensionScheduled)
        {
            next = next with
            {
                DueAtUtc = reviewedAtUtc.AddDays(AutomaticLearningPolicy.MaximumReviewIntervalDays),
                IntervalDays = AutomaticLearningPolicy.MaximumReviewIntervalDays
            };
            automaticState = automaticState with { MasteryReviewExtensionScheduled = true };
        }

        if (masteryAchieved)
        {
            next = next with { State = CardState.Retired };
        }

        card.State = next.State;
        card.DueAtUtc = next.DueAtUtc;
        card.IntervalDays = next.IntervalDays;
        card.EaseFactor = next.EaseFactor;
        card.SuccessfulReviewCount = next.SuccessfulReviewCount;
        card.LapseCount = next.LapseCount;
        card.LastReviewedAtUtc = next.LastReviewedAtUtc;
        card.LastRating = next.LastRating;
        card.UpdatedAtUtc = reviewedAtUtc;
        connection.Update(card);

        connection.Insert(new LearningReviewEntity
        {
            CardId = card.Id,
            SessionId = session.Id,
            Rating = rating,
            WasTypedAnswer = wasTypedAnswer,
            WasCorrect = wasCorrect,
            ReviewedAtUtc = reviewedAtUtc,
            DueAtUtc = next.DueAtUtc,
            IntervalDays = next.IntervalDays,
            EaseFactor = next.EaseFactor
        });
        queueItem.IsCompleted = true;
        queueItem.Rating = rating;
        queueItem.CompletedAtUtc = reviewedAtUtc;
        connection.Update(queueItem);

        session.CompletedCards++;
        session.UpdatedAtUtc = reviewedAtUtc;
        IncrementRating(session, rating);
        if (rating == ReviewRating.Again)
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

        if (masteryAchieved)
        {
            RetireWordCards(connection, session, queueItem, word.Id, reviewedAtUtc);
        }

        var hasRemaining = connection.Table<LearningSessionCardEntity>()
            .Any(item => item.SessionId == session.Id && !item.IsCompleted);
        if (!hasRemaining)
        {
            session.Status = LearningSessionStatus.Completed;
            session.CompletedAtUtc = reviewedAtUtc;
        }

        connection.Update(session);
        if (isAutomatic)
        {
            ApplyAutomaticState(word, automaticState);
        }

        if (masteryAchieved)
        {
            word.Status = WordStatus.Mastered;
            word.UpdatedAt = reviewedAtUtc;
            connection.Update(word);
        }
        else if (word.Status != WordStatus.Known)
        {
            word.Status = WordStatus.Learning;
            word.UpdatedAt = reviewedAtUtc;
            connection.Update(word);
        }
    }

    private LearningInteractionMode ResolveInteraction(WordEntity word, CardDirection direction)
    {
        if (appSettings is null)
        {
            return direction == CardDirection.MeaningToTerm
                ? LearningInteractionMode.Typing
                : LearningInteractionMode.Reading;
        }

        return AutomaticLearningPolicy.ResolveInteraction(
            appSettings.LearningMode,
            ReadAutomaticState(word));
    }

    private static AutomaticLearningState ReadAutomaticState(WordEntity word) => new(
        word.AutomaticInteractionMode == LearningInteractionMode.Typing
            ? LearningInteractionMode.Typing
            : LearningInteractionMode.Reading,
        Math.Clamp(
            word.ConsecutiveRecallSuccessCount,
            0,
            AutomaticLearningPolicy.RequiredConsecutiveAssessments),
        Math.Clamp(
            word.ConsecutiveTypingSuccessCount,
            0,
            AutomaticLearningPolicy.RequiredConsecutiveAssessments),
        Math.Clamp(
            word.ConsecutiveTypingFailureCount,
            0,
            AutomaticLearningPolicy.RequiredConsecutiveAssessments),
        word.MasteryReviewExtensionScheduled);

    private static void ApplyAutomaticState(
        WordEntity word,
        AutomaticLearningState state)
    {
        word.AutomaticInteractionMode = state.InteractionMode;
        word.ConsecutiveRecallSuccessCount = state.ConsecutiveRecallSuccesses;
        word.ConsecutiveTypingSuccessCount = state.ConsecutiveTypingSuccesses;
        word.ConsecutiveTypingFailureCount = state.ConsecutiveTypingFailures;
        word.MasteryReviewExtensionScheduled = state.MasteryReviewExtensionScheduled;
    }

    private static void RetireWordCards(
        SQLiteConnection connection,
        LearningSessionEntity session,
        LearningSessionCardEntity completedQueueItem,
        int wordId,
        DateTime retiredAtUtc)
    {
        var wordCards = connection.Table<LearningCardEntity>()
            .Where(item => item.WordId == wordId)
            .ToList();
        var wordCardIds = wordCards.Select(item => item.Id).ToHashSet();
        foreach (var wordCard in wordCards)
        {
            if (wordCard.State == CardState.Retired)
            {
                continue;
            }

            wordCard.State = CardState.Retired;
            wordCard.UpdatedAtUtc = retiredAtUtc;
            connection.Update(wordCard);
        }

        var redundantQueueItems = connection.Table<LearningSessionCardEntity>()
            .Where(item => item.SessionId == session.Id && !item.IsCompleted)
            .ToList()
            .Where(item => item.Id != completedQueueItem.Id && wordCardIds.Contains(item.CardId))
            .ToArray();
        foreach (var redundantQueueItem in redundantQueueItems)
        {
            connection.Delete(redundantQueueItem);
            session.TotalCards--;
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

    // ================= KF-MEANING-001 Slice 4: Schema-8 learning paths =================
    // Every method below runs on the caller-owned synchronous SQLiteConnection of exactly one
    // RunInTransactionAsync callback. ReadAsync and ExecuteSnapshotAsync are never used for Schema 8, no
    // LearningCardEntity is ORM-mapped, the frozen Word automatic-learning columns are never written, and
    // whole-word retirement (the Schema-7 behaviour) never happens — only the affected card and its own Sense
    // may roll up.

    private sealed record Schema8PendingMatch(int QueueItemId, string EnteredAnswer, int MatchedAnswerVariantId);

    private sealed record Schema8SpellingOutcome(
        SpellingSubmissionResult Result,
        Schema8PendingMatch? PendingMatch,
        bool IsSchema8);

    private sealed record Schema8RatingOutcome(int SessionId, LearningLoadResult Result);

    private sealed record Schema8QueueSelection(Schema8CardRow Card, int TargetAnswerVariantId, bool IsDueCard);

    /// <summary>Everything the Schema-8 paths need about one queue row, validated before any mutation.</summary>
    private sealed record Schema8Graph(
        Schema8QueueTargetRow Queue,
        Schema8CardRow Card,
        Schema8SessionCounterRow Session,
        Schema8SenseStatusRow Sense,
        WordEntity Word,
        int SenseId,
        int TargetAnswerVariantId,
        IReadOnlyList<Schema8AttributionCandidateRow> Assignments,
        Schema8AttributionCandidateRow TargetAssignment);

    private void ClearSchema8PendingMatch(int queueItemId)
    {
        if (_schema8PendingMatch?.QueueItemId == queueItemId)
        {
            _schema8PendingMatch = null;
        }
    }

    private void TripSchema8(Schema8LearningMutationCheckpoint checkpoint) =>
        schema8FailureInjector?.AtCheckpoint(checkpoint);

    private static Schema8LearningDataException Reject(Schema8LearningDataErrorCode code, string detail) =>
        Schema8LearningDataException.Create(code, detail);

    /// <summary>
    /// Loads and validates the complete queue/card/Sense/assignment graph. Performs no mutation and no replay,
    /// so <see cref="RevealAnswerSchema8"/> can reuse it unchanged.
    /// </summary>
    private static Schema8Graph LoadSchema8Graph(SQLiteConnection connection, int queueItemId)
    {
        var queue = Schema8LearningRepository.LoadQueueRow(connection, queueItemId)
            ?? throw Reject(Schema8LearningDataErrorCode.QueueItemNotFound, $"Queue row {queueItemId} does not exist.");

        var session = Schema8LearningRepository.LoadSession(connection, queue.SessionId)
            ?? throw Reject(Schema8LearningDataErrorCode.SessionNotFound,
                $"Queue row {queueItemId} references missing session {queue.SessionId}.");

        if (session.Status != LearningSessionStatus.Active)
        {
            throw Reject(Schema8LearningDataErrorCode.SessionNotActive,
                $"Session {session.Id} is {session.Status}, not Active.");
        }

        var card = Schema8LearningRepository.LoadCard(connection, queue.CardId)
            ?? throw Reject(Schema8LearningDataErrorCode.CardNotFound,
                $"Queue row {queueItemId} references missing card {queue.CardId}.");
        ValidateSchema8CardState(card);

        if (card.SenseId is null)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph, $"Card {card.Id} has no SenseId.");
        }

        if (card.Direction is not (CardDirection.TermToMeaning or CardDirection.MeaningToTerm))
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} has undefined CardDirection value {(int)card.Direction}.");
        }

        var senseId = card.SenseId.Value;
        var sense = Schema8LearningRepository.LoadSense(connection, senseId)
            ?? throw Reject(Schema8LearningDataErrorCode.SenseNotFound, $"Sense {senseId} does not exist.");

        var cardsForDirection =
            Schema8LearningRepository.CountCardsForSenseDirection(connection, senseId, card.Direction);
        if (cardsForDirection != 1)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Sense {senseId} has {cardsForDirection} cards for direction {card.Direction}; exactly one is required.");
        }

        // The Words table is untouched by the Schema-7 -> 8 migration, so the live entity mapping stays valid.
        var word = connection.Find<WordEntity>(card.WordId)
            ?? throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} references missing word {card.WordId}.");
        if (sense.WordId != card.WordId)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} and Sense {senseId} belong to different Words.");
        }

        var assignments =
            Schema8LearningRepository.LoadAssignmentsForSenseDirection(connection, senseId, card.Direction);
        ValidateSchema8AssignmentGraph(assignments, senseId, card.Direction);
        var rawAssignmentCount = Schema8LearningRepository.CountAssignmentRowsForSenseDirection(
            connection, senseId, card.Direction);
        var invalidVariantReferences = Schema8LearningRepository.CountInvalidVariantReferencesForSenseDirection(
            connection, senseId, card.Direction);
        if (invalidVariantReferences != 0 || rawAssignmentCount != assignments.Count)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Sense {senseId}/{card.Direction} has an assignment whose variant is missing or belongs to another Sense.");
        }

        if (queue.TargetAnswerVariantId is null)
        {
            throw Reject(Schema8LearningDataErrorCode.MissingTarget,
                $"Queue row {queueItemId} carries no TargetAnswerVariantId.");
        }

        var targetVariantId = queue.TargetAnswerVariantId.Value;
        var targetVariant = Schema8LearningRepository.LoadAnswerVariant(connection, targetVariantId)
            ?? throw Reject(Schema8LearningDataErrorCode.InvalidTarget,
                $"Target variant {targetVariantId} does not exist.");

        if (targetVariant.SenseId != senseId)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidTarget,
                $"Target variant {targetVariantId} belongs to Sense {targetVariant.SenseId}, not {senseId}.");
        }

        var targetAssignment = assignments.SingleOrDefault(row => row.AnswerVariantId == targetVariantId)
            ?? throw Reject(Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Target variant {targetVariantId} has no assignment for Sense {senseId}/{card.Direction}.");

        return new Schema8Graph(
            queue, card, session, sense, word, senseId, targetVariantId, assignments, targetAssignment);
    }

    private static void ValidateSchema8AssignmentGraph(
        IReadOnlyList<Schema8AttributionCandidateRow> assignments, int senseId, CardDirection direction)
    {
        if (assignments.GroupBy(row => row.AnswerVariantId).Any(group => group.Count() > 1))
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Sense {senseId}/{direction} has a duplicated assignment triple.");
        }

        if (assignments.Count(row => row.IsPreferred) > 1)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Sense {senseId}/{direction} has more than one preferred assignment.");
        }

        foreach (var row in assignments)
        {
            if (row.Requirement is not (AnswerVariantRequirement.Required or AnswerVariantRequirement.AcceptedOnly))
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                    $"Assignment {row.AssignmentId} has undefined AnswerVariantRequirement value {(int)row.Requirement}.");
            }

            if (row.IsRequired != row.RequiredSinceUtc.HasValue)
            {
                throw Reject(Schema8LearningDataErrorCode.RequirementBoundaryViolation,
                    $"Assignment {row.AssignmentId} violates 'Requirement = Required if and only if RequiredSinceUtc is not null'.");
            }
        }
    }

    private static void ValidateSchema8CardState(Schema8CardRow card)
    {
        if (card.State is not (CardState.New
            or CardState.Learning
            or CardState.Review
            or CardState.Relearning
            or CardState.Suspended
            or CardState.Retired))
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} has undefined CardState value {(int)card.State}.");
        }
    }

    private static void ValidateSchema8PreferredMeaning(
        SQLiteConnection connection, Schema8CardRow card, int senseId)
    {
        var meaning = Schema8LearningRepository.LoadMeaning(connection, card.PreferredMeaningId)
            ?? throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} references missing preferred Meaning {card.PreferredMeaningId}.");
        if (meaning.SenseId != senseId || meaning.WordId != card.WordId)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Preferred Meaning {meaning.Id} does not belong to card {card.Id}'s Sense and Word.");
        }
    }

    /// <summary>Schema-8 reveal: validate the Required target and Reading mode before one queue-column write.</summary>
    private void RevealAnswerSchema8(SQLiteConnection connection, int queueItemId)
    {
        var (graph, _, interaction, _) = LoadSchema8RatingState(connection, queueItemId);
        if (graph.Queue.IsCompleted)
        {
            throw Reject(Schema8LearningDataErrorCode.DuplicateSubmission,
                $"Queue row {queueItemId} was already submitted.");
        }

        if (interaction != LearningInteractionMode.Reading)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                "Only reading-mode cards reveal an answer directly.");
        }

        Schema8LearningRepository.SetQueueAnswerRevealed(connection, queueItemId);
    }

    /// <summary>
    /// Loads the graph, requires the target to be currently Required, replays the prior eligible history and
    /// resolves the interaction mode from the target's freshly replayed current-epoch state.
    /// </summary>
    private (Schema8Graph Graph, Schema8ReplayResult PriorReplay, LearningInteractionMode Interaction,
        IReadOnlyList<AnswerVariantProgressRow> PersistedProgress) LoadSchema8RatingState(
            SQLiteConnection connection, int queueItemId)
    {
        var graph = LoadSchema8Graph(connection, queueItemId);
        if (!graph.TargetAssignment.IsRequired)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidTarget,
                $"Target variant {graph.TargetAnswerVariantId} is currently AcceptedOnly and cannot be rated.");
        }

        if (graph.Card.State is CardState.Retired or CardState.Suspended)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                $"Card {graph.Card.Id} is {graph.Card.State} and cannot be scheduled.");
        }

        var events = Schema8LearningRepository.LoadReviewsForCard(connection, graph.Card.Id)
            .Select(Schema8LearningReviewReplayPolicy.ToReplayEvent)
            .ToList();
        var persistedProgress = Schema8LearningRepository.LoadProgressForCard(connection, graph.Card.Id);
        var priorReplay = Schema8LearningReviewReplayPolicy.Replay(
            graph.Card, graph.Assignments, events, persistedProgress);

        var targetOutcome = priorReplay.FindOutcome(graph.TargetAnswerVariantId)
            ?? throw Reject(Schema8LearningDataErrorCode.ProgressRowInvalid,
                $"No replayed outcome exists for Required target variant {graph.TargetAnswerVariantId}.");

        var interaction = Schema8LearningReviewReplayPolicy.ResolveInteraction(
            appSettings?.LearningMode, targetOutcome);

        return (graph, priorReplay, interaction, persistedProgress);
    }

    /// <summary>Schema-8 typed-answer check. The incorrect branch persists the Again rating in this same transaction.</summary>
    private Schema8SpellingOutcome CheckSpellingSchema8(
        SQLiteConnection connection, int queueItemId, string enteredAnswer)
    {
        var (graph, _, interaction, _) = LoadSchema8RatingState(connection, queueItemId);
        if (graph.Queue.IsCompleted)
        {
            throw Reject(Schema8LearningDataErrorCode.DuplicateSubmission,
                $"Queue row {queueItemId} was already submitted.");
        }

        if (graph.Card.Direction != CardDirection.MeaningToTerm)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                "Only MeaningToTerm cards accept a typed answer.");
        }

        if (interaction != LearningInteractionMode.Typing)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                "Only typing-mode cards accept a typed answer.");
        }

        var match = Schema8AnswerMatchPolicy.Resolve(
            spellingComparer, enteredAnswer, graph.TargetAnswerVariantId, graph.Assignments,
            graph.Word.TokenKind, graph.Word.Language);

        if (match.IsCorrect)
        {
            Schema8LearningRepository.SetQueueSpellingResult(connection, queueItemId, spellingCorrect: true);
            var result = new SpellingSubmissionResult(
                true, match.EnteredAnswer, match.ExpectedAnswer, string.Empty, null,
                RatingWasPersisted: false, match.MatchedAnswerVariantId);
            return new Schema8SpellingOutcome(
                result,
                new Schema8PendingMatch(queueItemId, enteredAnswer ?? string.Empty, match.MatchedAnswerVariantId!.Value),
                IsSchema8: true);
        }

        Schema8LearningRepository.SetQueueSpellingResult(connection, queueItemId, spellingCorrect: false);
        PersistRatingSchema8(connection, queueItemId, ReviewRating.Again, fromIncorrectSpellingCheck: true);
        var failed = new SpellingSubmissionResult(
            false, match.EnteredAnswer, match.ExpectedAnswer, match.Difference, null,
            RatingWasPersisted: true, MatchedAnswerVariantId: null);
        return new Schema8SpellingOutcome(failed, null, IsSchema8: true);
    }

    /// <summary>
    /// The complete Schema-8 rating transaction. One mastery authority: the current-event decision is taken
    /// once (before any write) and then verified against the full replay including the inserted review; a
    /// mismatch rolls the whole transaction back with <see cref="Schema8LearningDataErrorCode.ReplayDivergence"/>.
    /// The card is written exactly once, with the final post-review schedule including any extension, so an
    /// intermediate schedule is never persisted.
    /// </summary>
    private Schema8RatingOutcome PersistRatingSchema8(
        SQLiteConnection connection, int queueItemId, ReviewRating rating, bool fromIncorrectSpellingCheck)
    {
        // 1-10
        var (graph, priorReplay, interaction, persistedProgress) =
            LoadSchema8RatingState(connection, queueItemId);
        if (graph.Queue.IsCompleted)
        {
            throw Reject(Schema8LearningDataErrorCode.DuplicateSubmission,
                $"Queue row {queueItemId} was already submitted.");
        }

        // 11-12: queue-state gates and current-event attribution from fresh data only.
        var (wasTypedAnswer, wasCorrect, matchedVariantId) =
            EnforceSchema8Gates(connection, graph, interaction, rating, fromIncorrectSpellingCheck);

        // The credited variant: a matched AcceptedOnly variant is semantically correct but grants nothing.
        var creditedVariantId = matchedVariantId ?? graph.TargetAnswerVariantId;
        var creditedAssignment = graph.Assignments.Single(row => row.AnswerVariantId == creditedVariantId);
        var creditsProgress = creditedAssignment.IsRequired;

        // 13: the true current schedule comes from the card row, never from replay.
        var currentSchedule = new CardSchedule(
            graph.Card.State, Schema8Utc.Normalize(graph.Card.DueAtUtc), graph.Card.IntervalDays,
            graph.Card.EaseFactor, graph.Card.SuccessfulReviewCount, graph.Card.LapseCount,
            Schema8Utc.Normalize(graph.Card.LastReviewedAtUtc), graph.Card.LastRating);
        var reviewedAtUtc = Schema8Utc.Normalize(clock.UtcNow);

        // 14-15: apply the current event to the credited variant's current-epoch state.
        var creditedOutcome = creditsProgress ? priorReplay.FindOutcome(creditedVariantId) : null;
        var provisionalEvent = new Schema8ReplayReviewEvent(
            0, graph.Card.Id, rating, wasTypedAnswer, wasCorrect, reviewedAtUtc,
            currentSchedule.DueAtUtc, currentSchedule.IntervalDays, currentSchedule.EaseFactor,
            graph.TargetAnswerVariantId, matchedVariantId);

        var isMasteryReview = AutomaticLearningPolicy.IsMasteryReview(currentSchedule);
        AutomaticLearningState? expectedState = null;
        var masteryAchieved = false;
        if (creditedOutcome is not null)
        {
            var (nextState, achieved) = Schema8LearningReviewReplayPolicy.ApplyEvent(
                creditedOutcome.State, currentSchedule, provisionalEvent, wasCorrect);
            expectedState = nextState;
            masteryAchieved = achieved || creditedOutcome.IsMastered;
        }

        var extensionScheduled = expectedState?.MasteryReviewExtensionScheduled == true
            && creditedOutcome?.State.MasteryReviewExtensionScheduled != true;

        // 16-17: final scheduler result plus the 365-day extension when the policy demands it.
        var next = scheduler.Schedule(currentSchedule, rating, reviewedAtUtc);
        if (extensionScheduled)
        {
            next = next with
            {
                DueAtUtc = reviewedAtUtc.AddDays(AutomaticLearningPolicy.MaximumReviewIntervalDays),
                IntervalDays = AutomaticLearningPolicy.MaximumReviewIntervalDays
            };
        }

        // 18: retirement is decided over the COMPLETE current Required set, using the post-event mastery of the
        // credited variant and the prior mastery of every other Required variant.
        var requiredVariantIds = graph.Assignments.Where(row => row.IsRequired)
            .Select(row => row.AnswerVariantId).ToList();
        var retirementEligible = requiredVariantIds.Count > 0 && requiredVariantIds.All(variantId =>
            variantId == creditedVariantId
                ? masteryAchieved
                : priorReplay.FindOutcome(variantId)?.IsMastered == true);
        if (retirementEligible)
        {
            next = next with { State = CardState.Retired };
        }

        // 19: exactly one card write, already carrying the final schedule and state.
        Schema8LearningRepository.UpdateCardSchedule(connection, graph.Card.Id, next, reviewedAtUtc);

        // 20-23: review insert, then attribution, each followed by its checkpoint.
        var reviewId = Schema8LearningRepository.InsertReview(
            connection, graph.Card.Id, graph.Session.Id, rating, wasTypedAnswer, wasCorrect, reviewedAtUtc, next);
        TripSchema8(Schema8LearningMutationCheckpoint.AfterReviewInsert);

        Schema8LearningRepository.UpdateReviewAttribution(
            connection, reviewId, graph.TargetAnswerVariantId, matchedVariantId);
        TripSchema8(Schema8LearningMutationCheckpoint.AfterReviewTargetMatchedUpdate);

        // 24-25: queue completion, session counters and one tail repeat for every committed Again.
        Schema8LearningRepository.CompleteQueueRow(connection, queueItemId, rating, reviewedAtUtc);

        var session = Schema8LearningRepository.LoadSession(connection, graph.Session.Id)
            ?? throw Reject(Schema8LearningDataErrorCode.SessionMissingForRatedQueueItem,
                $"Session {graph.Session.Id} vanished during the rating transaction.");
        session.CompletedCards++;
        session.UpdatedAtUtc = reviewedAtUtc;
        switch (rating)
        {
            case ReviewRating.Again: session.AgainCount++; break;
            case ReviewRating.Hard: session.HardCount++; break;
            case ReviewRating.Good: session.GoodCount++; break;
            case ReviewRating.Easy: session.EasyCount++; break;
            default: throw new ArgumentOutOfRangeException(nameof(rating));
        }

        if (rating == ReviewRating.Again)
        {
            var nextOrder = Schema8LearningRepository.MaxQueueOrder(connection, session.Id) + 1;
            Schema8LearningRepository.InsertAgainRepeatQueueRow(connection, graph.Queue.Id, nextOrder);
            session.TotalCards++;
        }

        Schema8LearningRepository.UpdateSessionCounters(connection, session);

        // 26-28: full replay including the inserted review must reproduce the pre-write decision.
        var allEvents = Schema8LearningRepository.LoadReviewsForCard(connection, graph.Card.Id)
            .Select(Schema8LearningReviewReplayPolicy.ToReplayEvent)
            .ToList();
        var fullReplay = Schema8LearningReviewReplayPolicy.Replay(
            graph.Card, graph.Assignments, allEvents, persistedProgress);
        if (expectedState is not null)
        {
            var verified = fullReplay.FindOutcome(creditedVariantId)
                ?? throw Reject(Schema8LearningDataErrorCode.ReplayDivergence,
                    $"Full replay produced no outcome for credited variant {creditedVariantId}.");
            if (verified.State != expectedState
                || verified.IsMastered != masteryAchieved
                || !Schema8Utc.AreSameInstant(verified.LastAssessedAtUtc, reviewedAtUtc))
            {
                throw Reject(Schema8LearningDataErrorCode.ReplayDivergence,
                    $"Pre-write calculation and full replay disagree for card {graph.Card.Id}/variant {creditedVariantId}.");
            }
        }

        // 29-32: plan the complete replacement, then apply only Required replay-owned rows.
        var plan = Schema8LearningReviewReplayPolicy.PlanProgressReplacement(
            graph.Assignments, persistedProgress, fullReplay);
        TripSchema8(Schema8LearningMutationCheckpoint.DuringProgressReplacement);
        Schema8LearningReviewReplayPolicy.ApplyProgressPlan(connection, graph.Card.Id, plan);

        // 33-36: retirement cleanup, pruning only incomplete rows of this card.
        TripSchema8(Schema8LearningMutationCheckpoint.BeforeCardRetirement);
        if (retirementEligible)
        {
            Schema8CardRetirementPolicy.PruneIncompleteQueueRowsForCard(connection, graph.Card.Id, reviewedAtUtc);
        }

        // 37-38: affected Sense only.
        TripSchema8(Schema8LearningMutationCheckpoint.BeforeSenseRollup);
        Schema8CardRetirementPolicy.RecomputeSenseStatus(
            connection, graph.SenseId, graph.Sense.Status, reviewedAtUtc);

        // 39: finalise the owning session when nothing incomplete remains.
        var finalSession = Schema8LearningRepository.LoadSession(connection, graph.Session.Id)
            ?? throw Reject(Schema8LearningDataErrorCode.SessionMissingForRatedQueueItem,
                $"Session {graph.Session.Id} vanished during the rating transaction.");
        if (finalSession.Status == LearningSessionStatus.Active
            && Schema8LearningRepository.CountIncompleteQueueRows(connection, finalSession.Id) == 0)
        {
            finalSession.Status = LearningSessionStatus.Completed;
            finalSession.CompletedAtUtc ??= reviewedAtUtc;
            finalSession.UpdatedAtUtc = reviewedAtUtc;
            Schema8LearningRepository.UpdateSessionCounters(connection, finalSession);
        }

        // 40-41: the owning session's result is captured inside the transaction and returned after commit.
        var dayState = EnsureDayStateSchema12(connection, reviewedAtUtc);
        var limitN = appSettings is not null
            ? PreparationLimitPolicy.Normalize(appSettings.PreparationLimit)
            : PreparationLimitPolicy.DefaultLimit;
        return new Schema8RatingOutcome(
            finalSession.Id, BuildSchema8ResultForSession(connection, finalSession.Id, dayState, limitN, reviewedAtUtc));
    }

    /// <summary>
    /// The queue-state gates plus the current-event attribution. Every rejection happens before any mutation of
    /// this call. Returns the durable review facts for the event.
    /// </summary>
    private (bool WasTypedAnswer, bool WasCorrect, int? MatchedAnswerVariantId) EnforceSchema8Gates(
        SQLiteConnection connection,
        Schema8Graph graph,
        LearningInteractionMode interaction,
        ReviewRating rating,
        bool fromIncorrectSpellingCheck)
    {
        var pending = _schema8PendingMatch?.QueueItemId == graph.Queue.Id ? _schema8PendingMatch : null;

        if (fromIncorrectSpellingCheck)
        {
            // The incorrect typed path has just written SpellingChecked = 1 / SpellingCorrect = 0 in this same
            // transaction and never carries matched evidence.
            if (interaction != LearningInteractionMode.Typing)
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                    "An incorrect typed answer can only be recorded for a typing-mode card.");
            }

            if (rating != ReviewRating.Again)
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                    "An incorrect typed answer is always rated Again.");
            }

            return (true, false, null);
        }

        if (interaction == LearningInteractionMode.Reading)
        {
            if (!graph.Queue.AnswerRevealed)
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                    $"Queue row {graph.Queue.Id} must reveal the answer before a reading rating.");
            }

            if (pending is not null)
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidMatchEvidence,
                    $"Queue row {graph.Queue.Id} is a reading submission but carries matched-variant evidence.");
            }

            return (false, rating != ReviewRating.Again, null);
        }

        // Typing.
        if (!graph.Queue.SpellingChecked)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                $"Queue row {graph.Queue.Id} requires a spelling check before a typing rating.");
        }

        if (!graph.Queue.SpellingCorrect)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                $"Queue row {graph.Queue.Id} records an incorrect typed answer that was never completed.");
        }

        if (rating == ReviewRating.Again)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidQueueState,
                "A correct typed answer allows Hard, Good, or Easy.");
        }

        if (pending is null)
        {
            throw Reject(Schema8LearningDataErrorCode.MissingMatchEvidence,
                $"Queue row {graph.Queue.Id} has no valid pending match handoff.");
        }

        // Re-resolve the stored answer against the freshly loaded assignment graph: a target that was removed
        // or demoted, or an assignment whose text changed, must not silently keep its old attribution.
        var reResolved = Schema8AnswerMatchPolicy.Resolve(
            spellingComparer, pending.EnteredAnswer, graph.TargetAnswerVariantId, graph.Assignments,
            graph.Word.TokenKind, graph.Word.Language);
        if (!reResolved.IsCorrect || reResolved.MatchedAnswerVariantId != pending.MatchedAnswerVariantId)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidMatchEvidence,
                $"The stored answer for queue row {graph.Queue.Id} no longer resolves to variant {pending.MatchedAnswerVariantId}.");
        }

        return (true, true, reResolved.MatchedAnswerVariantId);
    }

    private bool MarkPermanentlyKnownSchema8(SQLiteConnection connection, int wordId)
    {
        var word = connection.Find<WordEntity>(wordId);
        if (word is null)
        {
            return false;
        }

        var senses = Schema8LearningRepository.LoadSensesForWord(connection, wordId);
        var senseIds = senses.Select(sense => sense.Id).ToHashSet();
        var meanings = Schema8LearningRepository.LoadMeaningsForWord(connection, wordId);
        var meaningsById = meanings.ToDictionary(meaning => meaning.Id);
        foreach (var meaning in meanings)
        {
            if (meaning.SenseId is null || !senseIds.Contains(meaning.SenseId.Value))
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                    $"Meaning {meaning.Id} does not belong to a Sense of Word {wordId}.");
            }

            if (Schema8LearningRepository.LoadContextsForMeaning(connection, meaning.Id)
                .Any(context => context.SenseId != meaning.SenseId || context.WordId != wordId))
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                    $"A context of Meaning {meaning.Id} does not belong to its Sense and Word.");
            }
        }

        foreach (var sense in senses)
        {
            foreach (var direction in new[] { CardDirection.TermToMeaning, CardDirection.MeaningToTerm })
            {
                var assignments = Schema8LearningRepository.LoadAssignmentsForSenseDirection(
                    connection, sense.Id, direction);
                ValidateSchema8AssignmentGraph(assignments, sense.Id, direction);
                if (Schema8LearningRepository.CountInvalidVariantReferencesForSenseDirection(
                        connection, sense.Id, direction) != 0
                    || Schema8LearningRepository.CountAssignmentRowsForSenseDirection(
                        connection, sense.Id, direction) != assignments.Count)
                {
                    throw Reject(Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                        $"Sense {sense.Id}/{direction} has an invalid AnswerVariant reference.");
                }
            }
        }

        var cards = Schema8LearningRepository.LoadCardsForWord(connection, wordId);
        foreach (var card in cards)
        {
            if (card.SenseId is null || !senseIds.Contains(card.SenseId.Value))
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                    $"Card {card.Id} does not belong to a Sense of Word {wordId}.");
            }

            if (card.Direction is not (CardDirection.TermToMeaning or CardDirection.MeaningToTerm)
                || Schema8LearningRepository.CountCardsForSenseDirection(
                    connection, card.SenseId.Value, card.Direction) != 1)
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                    $"Card {card.Id} has an invalid direction or duplicate Sense/direction identity.");
            }

            if (!meaningsById.TryGetValue(card.PreferredMeaningId, out var preferred)
                || preferred.SenseId != card.SenseId)
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                    $"Card {card.Id} has an invalid PreferredMeaningId.");
            }

            if (Schema8LearningRepository.CountInvalidProgressRowsForCard(connection, card.Id) != 0)
            {
                throw Reject(Schema8LearningDataErrorCode.ProgressRowInvalid,
                    $"Card {card.Id} has invalid progress rows.");
            }

            var assignments = Schema8LearningRepository.LoadAssignmentsForSenseDirection(
                connection, card.SenseId.Value, card.Direction);
            foreach (var queue in Schema8LearningRepository.LoadQueueRowsForCard(connection, card.Id))
            {
                _ = Schema8LearningRepository.LoadSession(connection, queue.SessionId)
                    ?? throw Reject(Schema8LearningDataErrorCode.SessionNotFound,
                        $"Queue row {queue.Id} references missing session {queue.SessionId}.");
                if (queue.TargetAnswerVariantId is null)
                {
                    throw Reject(Schema8LearningDataErrorCode.MissingTarget,
                        $"Queue row {queue.Id} has no frozen target.");
                }

                var assignment = assignments.SingleOrDefault(
                    row => row.AnswerVariantId == queue.TargetAnswerVariantId.Value);
                if (assignment is null || (!queue.IsCompleted && !assignment.IsRequired))
                {
                    throw Reject(Schema8LearningDataErrorCode.InvalidTarget,
                        $"Queue row {queue.Id} has an invalid target for card {card.Id}.");
                }
            }

            foreach (var review in Schema8LearningRepository.LoadReviewsForCard(connection, card.Id))
            {
                foreach (var variantId in new[] { review.TargetAnswerVariantId, review.MatchedAnswerVariantId })
                {
                    if (variantId.HasValue
                        && Schema8LearningRepository.LoadAnswerVariant(connection, variantId.Value)?.SenseId
                            != card.SenseId.Value)
                    {
                        throw Reject(Schema8LearningDataErrorCode.InvalidTarget,
                            $"Review {review.Id} references a variant outside card {card.Id}'s Sense.");
                    }
                }
            }
        }

        var affectedLearningSessionIds = Schema8LearningRepository
            .LoadAffectedSessionIdsForWord(connection, wordId).ToHashSet();
        Schema8LearningRepository.DeleteWordLearningGraph(connection, wordId);

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

        connection.Execute("DELETE FROM ReviewStates WHERE WordId = ?", wordId);

        word.Status = WordStatus.Known;
        word.PreparationState = PreparationState.Unprepared;
        word.AutomaticInteractionMode = LearningInteractionMode.Reading;
        word.ConsecutiveRecallSuccessCount = 0;
        word.ConsecutiveTypingSuccessCount = 0;
        word.ConsecutiveTypingFailureCount = 0;
        word.MasteryReviewExtensionScheduled = false;
        word.UpdatedAt = clock.UtcNow;
        connection.Update(word);

        NormalizePreparationSessions(connection, affectedPreparationSessionIds, clock.UtcNow);
        NormalizeLearningSessions(connection, affectedLearningSessionIds, clock.UtcNow);
        DocumentCleanupOperations.CleanupEligibleDocuments(connection);
        return true;
    }

    private Schema12LearningDayStateRow? EnsureDayStateSchema12(SQLiteConnection connection, DateTime nowUtc)
    {
        if (LearningSchemaCapability.Resolve(connection) is not LearningSchema12CapabilityResult)
        {
            return null;
        }

        var mode = appSettings?.LearningTimezoneMode ?? LearningTimezoneMode.System;
        var explicitId = appSettings?.ExplicitLearningTimezoneId;
        var cutoff = appSettings?.LearningDayCutoffMinutes ?? LearningDayConfiguration.DefaultCutoffMinutes;

        var requestedTz = _timezoneResolver.ResolveEffectiveTimeZone(mode, explicitId);
        var requestedCutoff = LearningDayConfiguration.NormalizeCutoffMinutes(cutoff);

        var dayState = Schema8LearningRepository.LoadLearningDayState(connection);
        if (dayState is null)
        {
            var (startUtc, endUtc, _) = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(nowUtc, requestedTz, requestedCutoff);
            dayState = new Schema12LearningDayStateRow
            {
                Id = 1,
                Phase = LearningDayPhase.ActiveBudgetDay,
                DayOrdinal = 1,
                ActiveDayStartUtc = startUtc,
                ActiveDayEndUtc = endUtc,
                FrozenTimeZoneId = requestedTz.Id,
                FrozenCutoffMinutes = requestedCutoff,
                BridgeStartedUtc = null,
                BridgeTargetTimeZoneId = null,
                BridgeTargetCutoffMinutes = null,
                BridgeTargetUtc = null,
                UpdatedAtUtc = nowUtc
            };
            Schema8LearningRepository.UpsertLearningDayState(connection, dayState);
            return dayState;
        }

        if (dayState.Phase == LearningDayPhase.ActiveBudgetDay)
        {
            if (nowUtc >= dayState.ActiveDayEndUtc)
            {
                var nextStartUtc = LearningDayBoundaryPolicy.CalculateNextDayStartAtOrAfter(
                    dayState.ActiveDayEndUtc, requestedTz, requestedCutoff);

                if (nextStartUtc == dayState.ActiveDayEndUtc || nowUtc >= nextStartUtc)
                {
                    var (startUtc, endUtc, _) = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(nowUtc, requestedTz, requestedCutoff);
                    dayState.Phase = LearningDayPhase.ActiveBudgetDay;
                    dayState.DayOrdinal++;
                    dayState.ActiveDayStartUtc = startUtc;
                    dayState.ActiveDayEndUtc = endUtc;
                    dayState.FrozenTimeZoneId = requestedTz.Id;
                    dayState.FrozenCutoffMinutes = requestedCutoff;
                    dayState.BridgeStartedUtc = null;
                    dayState.BridgeTargetTimeZoneId = null;
                    dayState.BridgeTargetCutoffMinutes = null;
                    dayState.BridgeTargetUtc = null;
                    dayState.UpdatedAtUtc = nowUtc;
                    Schema8LearningRepository.UpsertLearningDayState(connection, dayState);
                }
                else
                {
                    dayState.Phase = LearningDayPhase.Bridge;
                    dayState.BridgeStartedUtc = dayState.ActiveDayEndUtc;
                    dayState.BridgeTargetTimeZoneId = requestedTz.Id;
                    dayState.BridgeTargetCutoffMinutes = requestedCutoff;
                    dayState.BridgeTargetUtc = nextStartUtc;
                    dayState.UpdatedAtUtc = nowUtc;
                    Schema8LearningRepository.UpsertLearningDayState(connection, dayState);
                }
            }

            return dayState;
        }

        if (dayState.Phase == LearningDayPhase.Bridge)
        {
            var bridgeAnchor = dayState.BridgeStartedUtc ?? dayState.ActiveDayEndUtc;
            var bridgeTargetUtc = LearningDayBoundaryPolicy.CalculateNextDayStartAtOrAfter(
                bridgeAnchor, requestedTz, requestedCutoff);

            if (nowUtc >= bridgeTargetUtc)
            {
                var (startUtc, endUtc, _) = LearningDayBoundaryPolicy.CalculateDayBoundariesUtc(nowUtc, requestedTz, requestedCutoff);
                dayState.Phase = LearningDayPhase.ActiveBudgetDay;
                dayState.DayOrdinal++;
                dayState.ActiveDayStartUtc = startUtc;
                dayState.ActiveDayEndUtc = endUtc;
                dayState.FrozenTimeZoneId = requestedTz.Id;
                dayState.FrozenCutoffMinutes = requestedCutoff;
                dayState.BridgeStartedUtc = null;
                dayState.BridgeTargetTimeZoneId = null;
                dayState.BridgeTargetCutoffMinutes = null;
                dayState.BridgeTargetUtc = null;
                dayState.UpdatedAtUtc = nowUtc;
                Schema8LearningRepository.UpsertLearningDayState(connection, dayState);
            }
            else
            {
                dayState.BridgeTargetTimeZoneId = requestedTz.Id;
                dayState.BridgeTargetCutoffMinutes = requestedCutoff;
                dayState.BridgeTargetUtc = bridgeTargetUtc;
                dayState.UpdatedAtUtc = nowUtc;
                Schema8LearningRepository.UpsertLearningDayState(connection, dayState);
            }

            return dayState;
        }

        return dayState;
    }

    /// <summary>
    /// The Schema-8/12 <c>GetOrStartAsync</c> loader.
    /// </summary>
    private LearningLoadResult GetOrStartSchema8(SQLiteConnection connection)
    {
        var selectionNow = Schema8Utc.Normalize(clock.UtcNow);
        var dayState = EnsureDayStateSchema12(connection, selectionNow);
        var limitN = appSettings is not null
            ? PreparationLimitPolicy.Normalize(appSettings.PreparationLimit)
            : PreparationLimitPolicy.DefaultLimit;

        var activeSessions = Schema8LearningRepository.LoadActiveSessions(connection);
        if (activeSessions.Count > 1)
        {
            throw Reject(Schema8LearningDataErrorCode.SessionNotActive,
                $"{activeSessions.Count} active learning sessions exist; exactly one is permitted.");
        }

        if (activeSessions.Count == 1)
        {
            var active = activeSessions[0];
            var incompleteRows = Schema8LearningRepository.LoadIncompleteQueueRowsForSession(connection, active.Id);
            if (incompleteRows.Count == 0)
            {
                var totalQueueRows = Schema8LearningRepository.CountQueueRows(connection, active.Id);
                var reviewCount = Schema8LearningRepository.CountReviewsForSession(connection, active.Id);
                if (totalQueueRows == 0 && reviewCount == 0)
                {
                    Schema8LearningRepository.DeleteSession(connection, active.Id);
                }
                else
                {
                    active.TotalCards = totalQueueRows;
                    active.CompletedCards = Schema8LearningRepository
                        .LoadQueueRowsForSession(connection, active.Id).Count(row => row.IsCompleted);
                    active.AgainCount = Schema8LearningRepository.CountReviewsWithRating(connection, active.Id, ReviewRating.Again);
                    active.HardCount = Schema8LearningRepository.CountReviewsWithRating(connection, active.Id, ReviewRating.Hard);
                    active.GoodCount = Schema8LearningRepository.CountReviewsWithRating(connection, active.Id, ReviewRating.Good);
                    active.EasyCount = Schema8LearningRepository.CountReviewsWithRating(connection, active.Id, ReviewRating.Easy);
                    active.Status = LearningSessionStatus.Completed;
                    active.CompletedAtUtc ??= selectionNow;
                    active.UpdatedAtUtc = selectionNow;
                    Schema8LearningRepository.UpdateSessionCounters(connection, active);
                    return new LearningLoadResult(null, BuildSchema8Summary(connection, active));
                }
            }
            else
            {
                if (dayState is not null)
                {
                    ReconcileActiveSessionSchema8(connection, active, dayState, limitN, selectionNow);
                }
                return BuildSchema8ResultForSession(connection, active.Id, dayState, limitN, selectionNow);
            }
        }

        return CreateNewSessionSchema8(connection, dayState, limitN, selectionNow);
    }

    private void ReconcileActiveSessionSchema8(
        SQLiteConnection connection,
        Schema8SessionCounterRow active,
        Schema12LearningDayStateRow dayState,
        int limitN,
        DateTime selectionNow)
    {
        var initialTotalCards = active.TotalCards;
        var cards = Schema8LearningRepository.LoadAllCards(connection);
        foreach (var card in cards)
        {
            ValidateSchema8CardState(card);
        }
        var cardsById = cards.ToDictionary(c => c.Id);
        var wordsById = Schema8LearningRepository.LoadQueueWords(connection)
            .ToDictionary(w => w.Id);

        var incompleteRows = Schema8LearningRepository.LoadIncompleteQueueRowsForSession(connection, active.Id);
        var incompleteCardIds = incompleteRows.Select(r => r.CardId).ToHashSet();

        // Phase A: Carry-over grant bootstrap for genuinely-new words
        var existingGrants = Schema8LearningRepository.LoadGrantsForDay(connection, dayState.DayOrdinal);
        var existingGrantedWordIds = existingGrants.Select(g => g.WordId).ToHashSet();
        var nextSlotOrdinal = existingGrants.Count > 0 ? existingGrants.Max(g => g.SlotOrdinal) + 1 : 0;

        var distinctIncompleteWordIds = incompleteRows
            .Select(r => cardsById.GetValueOrDefault(r.CardId)?.WordId)
            .Where(wid => wid.HasValue)
            .Select(wid => wid!.Value)
            .Distinct()
            .ToList();

        foreach (var wordId in distinctIncompleteWordIds)
        {
            if (!existingGrantedWordIds.Contains(wordId) && !Schema8LearningRepository.HasEverBeenLearned(connection, wordId))
            {
                Schema8LearningRepository.InsertDayGrant(connection, dayState.DayOrdinal, wordId, nextSlotOrdinal++, selectionNow);
                existingGrantedWordIds.Add(wordId);
            }
        }

        // Phase B: Fresh fill for genuinely-new words up to N
        if (dayState.Phase == LearningDayPhase.ActiveBudgetDay)
        {
            var currentGrantCount = Schema8LearningRepository.CountGrantsForDay(connection, dayState.DayOrdinal);
            if (currentGrantCount < limitN)
            {
                var remainingCapacity = limitN - currentGrantCount;
                var candidateWords = wordsById.Values
                    .Where(w => !existingGrantedWordIds.Contains(w.Id) && !Schema8LearningRepository.HasEverBeenLearned(connection, w.Id))
                    .OrderByDescending(w => w.TotalOccurrenceCount)
                    .ThenBy(w => w.CreatedAt)
                    .ThenBy(w => w.CanonicalTerm, StringComparer.Ordinal)
                    .Take(remainingCapacity)
                    .ToList();

                var sessionQueueRows = Schema8LearningRepository.LoadQueueRowsForSession(connection, active.Id);
                var maxQueueOrder = sessionQueueRows.Count > 0 ? sessionQueueRows.Max(r => r.QueueOrder) : -1;

                foreach (var word in candidateWords)
                {
                    Schema8LearningRepository.InsertDayGrant(connection, dayState.DayOrdinal, word.Id, nextSlotOrdinal++, selectionNow);
                    existingGrantedWordIds.Add(word.Id);

                    var newCardsForWord = cards
                        .Where(c => c.WordId == word.Id && c.State == CardState.New && !incompleteCardIds.Contains(c.Id))
                        .OrderBy(c => c.Direction)
                        .ThenBy(c => c.Id);

                    foreach (var card in newCardsForWord)
                    {
                        var target = SelectSchema8QueueTarget(connection, card, wordsById);
                        if (target.HasValue)
                        {
                            ValidateSchema8PreferredMeaning(connection, card, card.SenseId!.Value);
                            maxQueueOrder++;
                            Schema8LearningRepository.InsertQueueRow(connection, active.Id, card.Id, maxQueueOrder, isDueCard: false, target.Value);
                            active.TotalCards++;
                        }
                    }
                }
            }
        }

        // Phase C: Old-work reconciliation (outside due cards and already-learned New sibling cards)
        var dueCards = cards
            .Where(c => c.State is CardState.Learning or CardState.Review or CardState.Relearning
                && Schema8Utc.Normalize(c.DueAtUtc) <= selectionNow
                && !incompleteCardIds.Contains(c.Id))
            .OrderBy(c => Schema8Utc.Normalize(c.DueAtUtc))
            .ThenBy(c => c.Id)
            .ToList();

        var siblingNewCards = cards
            .Where(c => c.State == CardState.New
                && Schema8LearningRepository.HasEverBeenLearned(connection, c.WordId)
                && !incompleteCardIds.Contains(c.Id))
            .OrderByDescending(c => wordsById.GetValueOrDefault(c.WordId)?.TotalOccurrenceCount ?? 0)
            .ThenBy(c => wordsById.GetValueOrDefault(c.WordId)?.CreatedAt ?? DateTime.MaxValue)
            .ThenBy(c => wordsById.GetValueOrDefault(c.WordId)?.CanonicalTerm, StringComparer.Ordinal)
            .ThenBy(c => c.Direction)
            .ThenBy(c => c.Id)
            .ToList();

        var allReconcileCards = dueCards.Concat(siblingNewCards).ToList();
        if (allReconcileCards.Count > 0)
        {
            var sessionQueueRows = Schema8LearningRepository.LoadQueueRowsForSession(connection, active.Id);
            var maxQueueOrder = sessionQueueRows.Count > 0 ? sessionQueueRows.Max(r => r.QueueOrder) : -1;

            foreach (var card in allReconcileCards)
            {
                var target = SelectSchema8QueueTarget(connection, card, wordsById);
                if (target.HasValue)
                {
                    ValidateSchema8PreferredMeaning(connection, card, card.SenseId!.Value);
                    maxQueueOrder++;
                    var isDue = card.State != CardState.New;
                    Schema8LearningRepository.InsertQueueRow(connection, active.Id, card.Id, maxQueueOrder, isDue, target.Value);
                    active.TotalCards++;
                }
            }
        }

        if (active.TotalCards != initialTotalCards)
        {
            active.UpdatedAtUtc = selectionNow;
            Schema8LearningRepository.UpdateSessionCounters(connection, active);
        }
    }

    private LearningLoadResult CreateNewSessionSchema8(
        SQLiteConnection connection,
        Schema12LearningDayStateRow? dayState,
        int limitN,
        DateTime selectionNow)
    {
        var cards = Schema8LearningRepository.LoadAllCards(connection);
        foreach (var card in cards)
        {
            ValidateSchema8CardState(card);
        }

        var wordsById = Schema8LearningRepository.LoadQueueWords(connection)
            .ToDictionary(word => word.Id);

        var dueCards = cards
            .Where(card => card.State is not (CardState.New or CardState.Suspended or CardState.Retired)
                && Schema8Utc.Normalize(card.DueAtUtc) <= selectionNow)
            .OrderBy(card => Schema8Utc.Normalize(card.DueAtUtc))
            .ThenBy(card => card.Id)
            .ToArray();

        var siblingNewCards = cards
            .Where(card => card.State == CardState.New && Schema8LearningRepository.HasEverBeenLearned(connection, card.WordId))
            .OrderByDescending(card => wordsById.GetValueOrDefault(card.WordId)?.TotalOccurrenceCount ?? 0)
            .ThenBy(card => wordsById.GetValueOrDefault(card.WordId)?.CreatedAt ?? DateTime.MaxValue)
            .ThenBy(card => wordsById.GetValueOrDefault(card.WordId)?.CanonicalTerm, StringComparer.Ordinal)
            .ThenBy(card => card.Direction)
            .ThenBy(card => card.Id)
            .ToArray();

        var genuinelyNewWords = wordsById.Values
            .Where(word => !Schema8LearningRepository.HasEverBeenLearned(connection, word.Id))
            .OrderByDescending(word => word.TotalOccurrenceCount)
            .ThenBy(word => word.CreatedAt)
            .ThenBy(word => word.CanonicalTerm, StringComparer.Ordinal)
            .ToList();

        var admittedGenuinelyNewCards = new List<Schema8CardRow>();
        if (dayState is null)
        {
            admittedGenuinelyNewCards.AddRange(
                cards.Where(card => card.State == CardState.New && !Schema8LearningRepository.HasEverBeenLearned(connection, card.WordId))
                    .OrderByDescending(card => wordsById.GetValueOrDefault(card.WordId)?.TotalOccurrenceCount ?? 0)
                    .ThenBy(card => wordsById.GetValueOrDefault(card.WordId)?.CreatedAt ?? DateTime.MaxValue)
                    .ThenBy(card => wordsById.GetValueOrDefault(card.WordId)?.CanonicalTerm, StringComparer.Ordinal)
                    .ThenBy(card => card.Direction)
                    .ThenBy(card => card.Id));
        }
        else if (dayState.Phase == LearningDayPhase.ActiveBudgetDay)
        {
            var existingGrants = Schema8LearningRepository.LoadGrantsForDay(connection, dayState.DayOrdinal);
            var nextSlotOrdinal = existingGrants.Count > 0 ? existingGrants.Max(g => g.SlotOrdinal) + 1 : 0;
            var remainingCapacity = limitN - existingGrants.Count;

            if (remainingCapacity > 0)
            {
                var grantedWordIds = existingGrants.Select(g => g.WordId).ToHashSet();
                var wordsToAdmit = genuinelyNewWords
                    .Where(w => !grantedWordIds.Contains(w.Id))
                    .Take(remainingCapacity)
                    .ToList();

                foreach (var word in wordsToAdmit)
                {
                    Schema8LearningRepository.InsertDayGrant(connection, dayState.DayOrdinal, word.Id, nextSlotOrdinal++, selectionNow);
                    var cardsForWord = cards
                        .Where(c => c.WordId == word.Id && c.State == CardState.New)
                        .OrderBy(c => c.Direction)
                        .ThenBy(c => c.Id);
                    admittedGenuinelyNewCards.AddRange(cardsForWord);
                }
            }
        }

        var dueIds = dueCards.Select(card => card.Id).ToHashSet();
        var selections = new List<Schema8QueueSelection>();

        foreach (var card in dueCards.Concat(siblingNewCards).Concat(admittedGenuinelyNewCards))
        {
            var target = SelectSchema8QueueTarget(connection, card, wordsById);
            if (target.HasValue)
            {
                ValidateSchema8PreferredMeaning(connection, card, card.SenseId!.Value);
                selections.Add(new Schema8QueueSelection(card, target.Value, dueIds.Contains(card.Id)));
            }
        }

        if (selections.Count > 0)
        {
            var sessionId = Schema8LearningRepository.InsertSession(connection, selectionNow, selections.Count);
            for (var index = 0; index < selections.Count; index++)
            {
                var selection = selections[index];
                Schema8LearningRepository.InsertQueueRow(
                    connection, sessionId, selection.Card.Id, index, selection.IsDueCard,
                    selection.TargetAnswerVariantId);
            }

            return BuildSchema8ResultForSession(connection, sessionId, dayState, limitN, selectionNow);
        }

        var latestCompleted = Schema8LearningRepository.LoadLatestCompletedSession(connection);
        return latestCompleted is null
            ? new LearningLoadResult(null, null)
            : new LearningLoadResult(null, BuildSchema8Summary(connection, latestCompleted));
    }

    private static bool IsCardPresentable(
        SQLiteConnection connection,
        Schema8CardRow card,
        bool isActiveSessionAgainRepeat,
        Schema12LearningDayStateRow dayState,
        int limitN,
        DateTime nowUtc)
    {
        if (card.State is CardState.Suspended or CardState.Retired)
        {
            return false;
        }

        if (card.State is CardState.Learning or CardState.Review or CardState.Relearning)
        {
            return isActiveSessionAgainRepeat || Schema8Utc.Normalize(card.DueAtUtc) <= nowUtc;
        }

        if (card.State == CardState.New)
        {
            if (Schema8LearningRepository.HasEverBeenLearned(connection, card.WordId))
            {
                return true;
            }

            if (dayState.Phase == LearningDayPhase.Bridge)
            {
                return false;
            }

            var grant = Schema8LearningRepository.LoadGrantForDayAndWord(connection, dayState.DayOrdinal, card.WordId);
            if (grant is null)
            {
                return false;
            }

            return grant.SlotOrdinal < limitN;
        }

        return false;
    }

    private static int? SelectSchema8QueueTarget(
        SQLiteConnection connection,
        Schema8CardRow card,
        IReadOnlyDictionary<int, Schema8QueueWordRow> wordsById)
    {
        if (card.Direction is not (CardDirection.TermToMeaning or CardDirection.MeaningToTerm))
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} has undefined CardDirection value {(int)card.Direction}.");
        }

        if (!wordsById.ContainsKey(card.WordId))
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} references missing word {card.WordId}.");
        }

        if (card.SenseId is null)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph, $"Card {card.Id} has no SenseId.");
        }

        var senseId = card.SenseId.Value;
        var sense = Schema8LearningRepository.LoadSense(connection, senseId)
            ?? throw Reject(Schema8LearningDataErrorCode.SenseNotFound, $"Sense {senseId} does not exist.");
        if (sense.WordId != card.WordId)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {card.Id} and Sense {senseId} belong to different Words.");
        }

        var cardCount = Schema8LearningRepository.CountCardsForSenseDirection(
            connection, senseId, card.Direction);
        if (cardCount != 1)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Sense {senseId} has {cardCount} cards for direction {card.Direction}; exactly one is required.");
        }

        var assignments = Schema8LearningRepository.LoadAssignmentsForSenseDirection(
            connection, senseId, card.Direction);
        ValidateSchema8AssignmentGraph(assignments, senseId, card.Direction);
        var rawAssignmentCount = Schema8LearningRepository.CountAssignmentRowsForSenseDirection(
            connection, senseId, card.Direction);
        var invalidVariantReferences = Schema8LearningRepository.CountInvalidVariantReferencesForSenseDirection(
            connection, senseId, card.Direction);
        if (invalidVariantReferences != 0 || rawAssignmentCount != assignments.Count)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Sense {senseId}/{card.Direction} has an assignment whose variant is missing or belongs to another Sense.");
        }

        var events = Schema8LearningRepository.LoadReviewsForCard(connection, card.Id)
            .Select(Schema8LearningReviewReplayPolicy.ToReplayEvent)
            .ToList();
        var progress = Schema8LearningRepository.LoadProgressForCard(connection, card.Id);
        if (Schema8LearningRepository.CountInvalidProgressRowsForCard(connection, card.Id) != 0)
        {
            throw Reject(Schema8LearningDataErrorCode.ProgressRowInvalid,
                $"Card {card.Id} has progress that cannot be attributed to its Sense and direction.");
        }

        var replay = Schema8LearningReviewReplayPolicy.Replay(card, assignments, events, progress);

        var candidates = new List<(Schema8AttributionCandidateRow Assignment, DateTime RequiredSinceUtc)>();
        foreach (var assignment in assignments.Where(row => row.IsRequired))
        {
            var outcome = replay.FindOutcome(assignment.AnswerVariantId)
                ?? throw Reject(Schema8LearningDataErrorCode.ProgressRowInvalid,
                    $"No replayed outcome exists for Required variant {assignment.AnswerVariantId} on card {card.Id}.");
            if (!outcome.IsMastered)
            {
                candidates.Add((assignment, Schema8Utc.Normalize(assignment.RequiredSinceUtc!.Value)));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Assignment.IsPreferred)
            .ThenBy(candidate => candidate.RequiredSinceUtc)
            .ThenBy(candidate => candidate.Assignment.AssignmentId)
            .ThenBy(candidate => candidate.Assignment.AnswerVariantId)
            .Select(candidate => (int?)candidate.Assignment.AnswerVariantId)
            .FirstOrDefault();
    }

    /// <summary>The result of exactly the owning session — never another session's summary.</summary>
    private LearningLoadResult BuildSchema8ResultForSession(
        SQLiteConnection connection,
        int sessionId,
        Schema12LearningDayStateRow? dayState = null,
        int? limitN = null,
        DateTime? selectionNow = null)
    {
        var session = Schema8LearningRepository.LoadSession(connection, sessionId)
            ?? throw Reject(Schema8LearningDataErrorCode.SessionMissingForRatedQueueItem,
                $"Session {sessionId} does not exist at result time.");

        if (session.Status == LearningSessionStatus.Completed)
        {
            return new LearningLoadResult(null, BuildSchema8Summary(connection, session));
        }

        var incompleteRows = Schema8LearningRepository.LoadIncompleteQueueRowsForSession(connection, session.Id);
        if (incompleteRows.Count == 0)
        {
            return new LearningLoadResult(null, null);
        }

        if (dayState is null)
        {
            return new LearningLoadResult(BuildSchema8CardView(connection, incompleteRows[0].Id), null);
        }

        var now = selectionNow ?? Schema8Utc.Normalize(clock.UtcNow);
        var effectiveLimit = limitN ?? (appSettings is not null
            ? PreparationLimitPolicy.Normalize(appSettings.PreparationLimit)
            : PreparationLimitPolicy.DefaultLimit);

        foreach (var row in incompleteRows)
        {
            var card = Schema8LearningRepository.LoadCard(connection, row.CardId);
            if (card is null)
            {
                continue;
            }

            if (IsCardPresentable(connection, card, row.IsAgainRepeat, dayState, effectiveLimit, now))
            {
                return new LearningLoadResult(BuildSchema8CardView(connection, row.Id), null);
            }
        }

        return new LearningLoadResult(null, null);
    }

    private LearningCardView BuildSchema8CardView(SQLiteConnection connection, int queueItemId)
    {
        var (graph, _, interaction, _) = LoadSchema8RatingState(connection, queueItemId);
        var meaning = Schema8LearningRepository.LoadMeaning(connection, graph.Card.PreferredMeaningId)
            ?? throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Card {graph.Card.Id} references missing preferred Meaning {graph.Card.PreferredMeaningId}.");
        if (meaning.SenseId != graph.SenseId || meaning.WordId != graph.Word.Id)
        {
            throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                $"Preferred Meaning {meaning.Id} does not belong to card {graph.Card.Id}'s Sense and Word.");
        }

        var contextRows = Schema8LearningRepository.LoadContextsForMeaning(connection, meaning.Id);
        foreach (var snapshot in contextRows)
        {
            if (snapshot.SenseId != graph.SenseId
                || snapshot.WordId != graph.Word.Id
                || snapshot.TargetStart < 0
                || snapshot.TargetLength < 0
                || snapshot.TargetStart > snapshot.Text.Length
                || snapshot.TargetLength > snapshot.Text.Length - snapshot.TargetStart)
            {
                throw Reject(Schema8LearningDataErrorCode.InvalidCardGraph,
                    $"Context {snapshot.Id} has invalid ownership or target coordinates for card {graph.Card.Id}.");
            }
        }

        var contexts = contextRows
            .Select(snapshot => new LearningContext(
                snapshot.SourceDocumentTitle,
                snapshot.Text[..snapshot.TargetStart],
                snapshot.Text.Substring(snapshot.TargetStart, snapshot.TargetLength),
                snapshot.Text[(snapshot.TargetStart + snapshot.TargetLength)..]))
            .ToArray();

        return new LearningCardView(
            graph.Session.Id,
            graph.Queue.Id,
            graph.Card.Id,
            graph.Word.Id,
            graph.Card.Direction,
            interaction,
            graph.Card.State,
            meaning.DisplayTerm,
            graph.Word.TokenKind,
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
            graph.Word.TotalOccurrenceCount,
            graph.Queue.AnswerRevealed,
            graph.Session.CompletedCards,
            graph.Session.TotalCards,
            EmptyToNull(meaning.EncounteredSurfaceForm),
            EmptyToNull(meaning.GrammaticalRelationship),
            meaning.SourceRevisionId,
            graph.Queue.IsAgainRepeat);
    }

    private static LearningSessionSummary BuildSchema8Summary(
        SQLiteConnection connection, Schema8SessionCounterRow session) => new(
        session.Id,
        session.CompletedCards,
        session.AgainCount,
        session.HardCount,
        session.GoodCount,
        session.EasyCount,
        Schema8LearningRepository.SelectNextDueAtUtc(connection),
        Schema8LearningRepository.CountRemainingUnprepared(connection));

    private static LearningSessionSummary BuildSchema8Summary(
        SQLiteConnection connection, LearningSessionEntity session) => new(
        session.Id,
        session.CompletedCards,
        session.AgainCount,
        session.HardCount,
        session.GoodCount,
        session.EasyCount,
        Schema8LearningRepository.SelectNextDueAtUtc(connection),
        Schema8LearningRepository.CountRemainingUnprepared(connection));

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
                : JsonSerializer.Deserialize(
                    json,
                    LexicalJsonSerializerContext.Default.StringArray) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
