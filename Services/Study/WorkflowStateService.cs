using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;

namespace KnownFirst.Services.Study;

public sealed class WorkflowStateService(
    IKnownFirstDatabase database,
    IClock clock) : IWorkflowStateService
{
    public Task<WorkflowSnapshot> GetSnapshotAsync() => database.ReadAsync(async connection =>
    {
        var hasReview = await connection.Table<ReviewSessionEntity>()
            .Where(session => session.Status == ReviewSessionStatus.Active)
            .CountAsync() > 0;
        var hasPreparation = await connection.Table<PreparationSessionEntity>()
            .Where(session => session.Status == PreparationSessionStatus.Active)
            .CountAsync() > 0;
        var hasLearning = await connection.Table<LearningSessionEntity>()
            .Where(session => session.Status == LearningSessionStatus.Active)
            .CountAsync() > 0;
        var dueCards = await connection.Table<LearningCardEntity>()
            .Where(card => card.State != CardState.New
                && card.State != CardState.Suspended
                && card.State != CardState.Retired
                && card.DueAtUtc <= clock.UtcNow)
            .CountAsync();
        var preparedCards = await connection.Table<LearningCardEntity>()
            .Where(card => card.State == CardState.New)
            .ToListAsync();
        var preparedItems = preparedCards.Select(card => card.WordId).Distinct().Count();
        var unprepared = await connection.Table<WordEntity>()
            .Where(word => word.Status == WordStatus.UnknownBacklog
                && word.PreparationState != PreparationState.Prepared)
            .CountAsync();
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
