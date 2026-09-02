using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;

namespace KnownFirst.Services.Study;

public sealed class WorkflowStateService(
    IKnownFirstDatabase database,
    IClock clock) : IWorkflowStateService
{
    public Task<WorkflowSnapshot> GetSnapshotAsync() => database.ExecuteSnapshotAsync(connection =>
    {
        var hasReview = connection.Table<ReviewSessionEntity>()
            .Count(session => session.Status == ReviewSessionStatus.Active) > 0;
        var hasPreparation = connection.Table<PreparationSessionEntity>()
            .Count(session => session.Status == PreparationSessionStatus.Active) > 0;
        var hasLearning = connection.Table<LearningSessionEntity>()
            .Count(session => session.Status == LearningSessionStatus.Active) > 0;
        var capability = LearningSchemaCapability.Resolve(connection);
        var dueCards = capability is LearningSchema13CapabilityResult
            ? Schema13LearningRepository.CountDueCards(connection, new DateTimeOffset(clock.UtcNow))
            : connection.Table<LearningCardEntity>().Count(card => card.State != CardState.New
                && card.State != CardState.Suspended
                && card.State != CardState.Retired
                && card.DueAtUtc <= clock.UtcNow);
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
            action);
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
