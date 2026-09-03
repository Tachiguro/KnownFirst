using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;

namespace KnownFirst.Services.Study;

public sealed class WorkflowStateService(
    IKnownFirstDatabase database,
    IClock clock) : IWorkflowStateService
{
    public Task<WorkflowSnapshot> GetSnapshotAsync() => database.ExecuteSnapshotAsync(connection =>
    {
        var nowUtc = clock.UtcNow;
        var hasReview = connection.Table<ReviewSessionEntity>()
            .Count(session => session.Status == ReviewSessionStatus.Active) > 0;
        var hasPreparation = connection.Table<PreparationSessionEntity>()
            .Count(session => session.Status == PreparationSessionStatus.Active) > 0;
        var activeLearningSession = connection.Table<LearningSessionEntity>()
            .Where(session => session.Status == LearningSessionStatus.Active)
            .OrderBy(session => session.Id)
            .FirstOrDefault();
        var hasLearning = activeLearningSession is not null;
        var capability = LearningSchemaCapability.Resolve(connection);
        var dueCards = capability is LearningSchema13CapabilityResult
            ? Schema13LearningRepository.CountDueCards(connection, new DateTimeOffset(nowUtc))
            : connection.Table<LearningCardEntity>().Count(card => card.State != CardState.New
                && card.State != CardState.Suspended
                && card.State != CardState.Retired
                && card.DueAtUtc <= nowUtc);
        var nextDueAtUtc = capability switch
        {
            LearningSchema13CapabilityResult =>
                Schema13LearningRepository.SelectNextDueAtUtc(connection)?.UtcDateTime,
            LearningSchema8CapabilityResult
                or LearningSchema9CapabilityResult
                or LearningSchema10CapabilityResult
                or LearningSchema11CapabilityResult
                or LearningSchema12CapabilityResult =>
                Schema8LearningRepository.SelectNextDueAtUtc(connection),
            _ => null
        };
        DateTime? activeLearningDayEndUtc = capability is LearningSchema12CapabilityResult
                or LearningSchema13CapabilityResult
            ? Schema8LearningRepository.LoadLearningDayState(connection) is
                { Phase: LearningDayPhase.ActiveBudgetDay } dayState
                ? DateTime.SpecifyKind(dayState.ActiveDayEndUtc, DateTimeKind.Utc)
                : null
            : null;
        var preparedItems = capability is LearningSchema13CapabilityResult
            ? Schema13LearningRepository.CountNewWords(connection)
            : connection.Table<LearningCardEntity>()
                .Where(card => card.State == CardState.New)
                .ToList()
                .Select(card => card.WordId)
                .Distinct()
                .Count();
        var unprepared = connection.Table<WordEntity>()
            .Count(word => word.Status == WordStatus.UnknownBacklog
                && word.PreparationState != PreparationState.Prepared);
        var action = ResolveAction(
            hasReview,
            hasPreparation,
            hasLearning,
            dueCards,
            preparedItems,
            unprepared);
        return new WorkflowSnapshot(
            hasReview,
            hasPreparation,
            hasLearning,
            dueCards,
            preparedItems,
            unprepared,
            action,
            nextDueAtUtc,
            activeLearningSession?.CompletedCards,
            activeLearningSession?.TotalCards,
            activeLearningDayEndUtc);
    });

    private static WorkflowPrimaryAction ResolveAction(
        bool hasReview,
        bool hasPreparation,
        bool hasLearning,
        int dueCards,
        int preparedItems,
        int unprepared)
    {
        if (hasReview)
        {
            return WorkflowPrimaryAction.ContinueReview;
        }

        if (hasPreparation)
        {
            return WorkflowPrimaryAction.ContinuePreparation;
        }

        if (hasLearning)
        {
            return WorkflowPrimaryAction.ContinueLearning;
        }

        if (dueCards > 0)
        {
            return WorkflowPrimaryAction.LearnDueCards;
        }

        if (preparedItems > 0)
        {
            return WorkflowPrimaryAction.StartLearning;
        }

        return unprepared > 0
            ? WorkflowPrimaryAction.PrepareWords
            : WorkflowPrimaryAction.ImportText;
    }
}
