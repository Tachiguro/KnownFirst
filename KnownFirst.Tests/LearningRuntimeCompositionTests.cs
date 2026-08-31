using System.Reflection;
using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Schema13;
using KnownFirst.Services.Study;
using Microsoft.Extensions.DependencyInjection;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LearningRuntimeCompositionTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task AddKnownFirstLearningRuntime_ResolvesFsrsOnlyProductionServiceAndRatesSchema13Card()
    {
        await using var database = new DatabaseSchema13ProductionCutoverTests.ProductionInitializedDatabase();
        await database.InitializeAsync();
        var cardId = await SeedReadingCardAsync(database);

        var services = new ServiceCollection();
        services.AddSingleton<IKnownFirstDatabase>(database);
        services.AddSingleton(new SpellingAnswerComparer());
        services.AddSingleton<IClock>(new FakeClock(Now));
        InvokeProductionRegistration(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var concrete = provider.GetRequiredService<LearningService>();
        var abstraction = provider.GetRequiredService<ILearningService>();
        Assert.AreSame(concrete, abstraction);
        Assert.IsNotNull(provider.GetRequiredService<IFsrs6SchedulingService>());
        Assert.IsNull(provider.GetService<ISpacedRepetitionScheduler>());
        Assert.IsNull(provider.GetService<SimpleSpacedRepetitionScheduler>());

        var loaded = await abstraction.GetOrStartAsync();
        Assert.IsNotNull(loaded.Card);
        Assert.AreEqual(cardId, loaded.Card.CardId);
        await abstraction.RevealAnswerAsync(loaded.Card.QueueItemId);
        var rated = await abstraction.RateAsync(loaded.Card.QueueItemId, ReviewRating.Good);

        Assert.IsNull(rated.Card);
        Assert.IsNotNull(rated.CompletedSummary);
        Assert.AreEqual(1, rated.CompletedSummary.GoodCount);
        Assert.AreEqual(
            1,
            await database.ReadAsync(connection => connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM FsrsReviewHistoryEntries WHERE CardId = ?",
                cardId)));
    }

    [TestMethod]
    public void MauiProgram_UsesProductionLearningBoundaryWithoutLegacySchedulerRegistration()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "MauiProgram.cs"));

        Assert.Contains("AddKnownFirstLearningRuntime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<ISpacedRepetitionScheduler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<ILearningService, LearningService>", source, StringComparison.Ordinal);
    }

    private static void InvokeProductionRegistration(IServiceCollection services)
    {
        var extensionType = typeof(LearningService).Assembly.GetType(
            "KnownFirst.Services.Study.LearningRuntimeServiceCollectionExtensions");
        Assert.IsNotNull(extensionType, "The production learning registration boundary is missing.");
        var method = extensionType.GetMethod(
            "AddKnownFirstLearningRuntime",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(IServiceCollection)],
            modifiers: null);
        Assert.IsNotNull(method, "The production learning registration method is missing.");
        method.Invoke(null, [services]);
    }

    private static async Task<int> SeedReadingCardAsync(IKnownFirstDatabase database)
    {
        return await database.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO Words (
                    Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                    TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                    ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                    MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
                VALUES ('en', 'cutover', 'cutover', 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
                """,
                Now,
                Now);
            var wordId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO Senses (
                    StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('cutover-sense', ?, 'en', 'en', 0, ?, ?)
                """,
                wordId,
                Now,
                Now);
            var senseId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO Meanings (
                    WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                    GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                    AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution,
                    ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
                VALUES (?, ?, 'de', 'en', 'cutover', 'cutover', '', 0, 'Schnitt', 'a production cutover', '', '',
                        '[]', 'Schnitt', 'test', 'test', 'test', 'test', 1, ?, ?, ?, 'cutover-meaning')
                """,
                wordId,
                senseId,
                Now,
                Now,
                Now);
            var meaningId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

            connection.Execute(
                """
                INSERT INTO LearningCards (
                    WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                    EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, 0, 0, ?, 0, 2.5, 0, 0, ?, ?)
                """,
                wordId,
                senseId,
                meaningId,
                Now,
                Now,
                Now);
            var cardId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            connection.Execute(
                """
                INSERT INTO AnswerVariants (
                    StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                    CreatedAtUtc, UpdatedAtUtc)
                VALUES ('cutover-answer', ?, 'de', 'Schnitt', 'schnitt', ?, ?, ?)
                """,
                senseId,
                meaningId,
                Now,
                Now);
            var variantId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            connection.Execute(
                """
                INSERT INTO SenseAnswerVariantAssignments (
                    StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                    RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('cutover-assignment', ?, 0, ?, 0, 1, ?, ?, ?)
                """,
                senseId,
                variantId,
                Now,
                Now,
                Now);
            Schema13LearningRepository.InsertCleanNewState(connection, cardId);
            return cardId;
        });
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the KnownFirst repository root.");
    }
}
