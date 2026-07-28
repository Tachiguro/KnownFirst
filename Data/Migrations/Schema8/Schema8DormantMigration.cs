using System.Globalization;
using System.Text.Json;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema8;

/// <summary>
/// The dormant Schema 7 -&gt; 8 migration engine (KF-MEANING-001 Slice 1, architecture doc §4). Callable
/// only by tests against synthetic Schema-7 fixtures — never referenced by
/// <c>DatabaseSchema.InitializeAsync</c> or any normal service (architecture doc §4.8.1). Runs the
/// complete transformation inside one real SQLite transaction; any failure rolls back the schema,
/// indexes, version, and row data to the original Schema-7 state.
/// </summary>
public static class Schema8DormantMigration
{
    public const int SourceVersion = 7;
    public const int TargetVersion = 8;

    public static async Task<Schema8MigrationResult> ApplyAsync(
        SQLiteAsyncConnection connection,
        Schema8MigrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new Schema8MigrationOptions();

        var sourceVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

        if (sourceVersion > TargetVersion)
        {
            throw Schema8MigrationException.FutureVersion(sourceVersion);
        }

        if (sourceVersion == TargetVersion)
        {
            await connection.RunInTransactionAsync(ValidateAlreadyMigratedShape).ConfigureAwait(false);
            return new Schema8MigrationResult(Schema8MigrationOutcome.AlreadyApplied, sourceVersion, TargetVersion, UsedColumnRebuildFallback: false);
        }

        if (sourceVersion != SourceVersion)
        {
            throw Schema8MigrationException.UnsupportedSourceVersion(sourceVersion);
        }

        var usedFallback = false;
        await connection.RunInTransactionAsync(conn => usedFallback = RunMigration(conn, options)).ConfigureAwait(false);

        return new Schema8MigrationResult(Schema8MigrationOutcome.Migrated, sourceVersion, TargetVersion, usedFallback);
    }

    private sealed class MigrationContext
    {
        public Dictionary<int, int> SenseIdByMeaningId { get; } = [];
        public Dictionary<int, int> SenseIdByCardId { get; } = [];
        public Dictionary<(int SenseId, CardDirection Direction), int> DirectionVariantId { get; } = [];
    }

    private static bool RunMigration(SQLiteConnection connection, Schema8MigrationOptions options)
    {
        var preMigrationCardCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards");
        var preMigrationMeaningCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Meanings");

        var usedFallback = Step1_CreateTablesColumnsAndRename(connection, options);
        Trip(options, "after-table-creation");

        var context = Step2_GenerateSensesVariantsAssignments(connection);
        Trip(options, "after-generation");

        Step3_Backfill(connection, context);
        Trip(options, "after-backfill");

        Step4_ValidateForwardReferences(connection, preMigrationCardCount, preMigrationMeaningCount);
        Trip(options, "after-forward-validation");

        Step5_DropOldCardIndex(connection);
        Trip(options, "after-old-index-drop");

        Step6_CreateNewCardIndex(connection);
        Trip(options, "after-new-index-create");

        Step7_ValidateFinalInvariants(connection);
        Trip(options, "after-final-validation");

        Step8_SetVersion(connection);

        return usedFallback;
    }

    private static void Trip(Schema8MigrationOptions options, string checkpoint) =>
        options.FaultInjectionHook?.Invoke(checkpoint);

    // ---- Step 1: new tables/columns, partial unique index, MeaningId -> PreferredMeaningId ----

    private static bool Step1_CreateTablesColumnsAndRename(SQLiteConnection connection, Schema8MigrationOptions options)
    {
        connection.Execute(Schema8Ddl.CreateSenses);
        connection.Execute(Schema8Ddl.IndexSensesStableId);
        connection.Execute(Schema8Ddl.IndexSensesWordId);

        connection.Execute(Schema8Ddl.CreateAnswerVariants);
        connection.Execute(Schema8Ddl.IndexAnswerVariantsStableId);
        connection.Execute(Schema8Ddl.IndexAnswerVariantsSenseLanguageText);

        connection.Execute(Schema8Ddl.CreateSenseAnswerVariantAssignments);
        connection.Execute(Schema8Ddl.IndexAssignmentsStableId);
        connection.Execute(Schema8Ddl.IndexAssignmentsSenseDirectionVariant);
        connection.Execute(Schema8Ddl.IndexAssignmentsSenseDirectionPreferred);

        connection.Execute(Schema8Ddl.CreateAnswerVariantProgress);
        connection.Execute(Schema8Ddl.IndexProgressCardVariant);

        connection.Execute("ALTER TABLE Meanings ADD COLUMN SenseId INTEGER NULL");
        connection.Execute("ALTER TABLE Meanings ADD COLUMN StableId TEXT NULL");
        connection.Execute("ALTER TABLE ContextSnapshots ADD COLUMN SenseId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningReviews ADD COLUMN TargetAnswerVariantId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningReviews ADD COLUMN MatchedAnswerVariantId INTEGER NULL");
        connection.Execute("ALTER TABLE LearningSessionCards ADD COLUMN TargetAnswerVariantId INTEGER NULL");

        var useFallback = options.ForceColumnRebuildFallback || !Schema8SqliteCapabilities.SupportsRenameColumn(connection);
        if (useFallback)
        {
            RebuildLearningCardsWithPreferredMeaningId(connection);
        }
        else
        {
            connection.Execute("ALTER TABLE LearningCards ADD COLUMN SenseId INTEGER NULL");
            connection.Execute("ALTER TABLE LearningCards RENAME COLUMN MeaningId TO PreferredMeaningId");
        }

        return useFallback;
    }

    private static void RebuildLearningCardsWithPreferredMeaningId(SQLiteConnection connection)
    {
        connection.Execute("""
            CREATE TABLE LearningCards_Schema8Rebuild (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WordId INTEGER NOT NULL,
                PreferredMeaningId INTEGER NOT NULL,
                SenseId INTEGER NULL,
                Direction INTEGER NOT NULL,
                State INTEGER NOT NULL,
                DueAtUtc TEXT NOT NULL,
                IntervalDays INTEGER NOT NULL,
                EaseFactor REAL NOT NULL,
                SuccessfulReviewCount INTEGER NOT NULL,
                LapseCount INTEGER NOT NULL,
                LastReviewedAtUtc TEXT NULL,
                LastRating INTEGER NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            )
            """);

        connection.Execute("""
            INSERT INTO LearningCards_Schema8Rebuild
                (Id, WordId, PreferredMeaningId, SenseId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            SELECT
                Id, WordId, MeaningId, NULL, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc
            FROM LearningCards
            """);

        connection.Execute("DROP TABLE LearningCards");
        connection.Execute("ALTER TABLE LearningCards_Schema8Rebuild RENAME TO LearningCards");
    }

    // ---- Step 2: generate Senses / AnswerVariants / SenseAnswerVariantAssignments (no backfill yet) ----

    private static MigrationContext Step2_GenerateSensesVariantsAssignments(SQLiteConnection connection)
    {
        var context = new MigrationContext();
        var now = DateTime.UtcNow;

        var words = connection.Query<LegacyWordRow>("SELECT Id, Language, CanonicalTerm, TokenKind, Status FROM Words");
        var cardsByWord = connection
            .Query<LegacyCardRow>("SELECT Id, WordId, PreferredMeaningId, Direction FROM LearningCards")
            .GroupBy(c => c.WordId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var meaningsByWord = connection
            .Query<LegacyMeaningRow>("SELECT * FROM Meanings ORDER BY Id")
            .GroupBy(m => m.WordId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var word in words)
        {
            if (!meaningsByWord.TryGetValue(word.Id, out var meanings) || meanings.Count == 0)
            {
                continue;
            }

            var vocabularyIdentity = ComputeVocabularyIdentity(word);
            var groups = GroupMeaningsIntoSenses(meanings, vocabularyIdentity);
            var initialSenseStatus = TranslateInitialSenseStatus(word.Status);

            if (word.Status is WordStatus.Prepared or WordStatus.Learning or WordStatus.Mastered)
            {
                connection.Execute("UPDATE Words SET Status = ? WHERE Id = ?", (int)WordStatus.UnknownBacklog, word.Id);
            }

            cardsByWord.TryGetValue(word.Id, out var wordCards);

            foreach (var rawGroup in groups)
            {
                // Focused invariant 1 (determinism): never rely on database-return order, dictionary/
                // grouping insertion order, or incidental loop order. Ascending legacy Meaning.Id is the
                // sole, explicit ordering key for every Meaning-provenance decision below. The Sense's
                // representative/default Meaning (group[0] after this sort) is by construction always the
                // group's lowest-Id Meaning, so "the representative wins when it contributed" and
                // "otherwise the lowest legacy Meaning.Id wins" collapse into one uniform rule: process
                // Meanings ascending by Id and let first-writer-wins dedup (GetOrCreateAnswerVariant, which
                // never overwrites an already-selected SourceMeaningId) pick the winner every time.
                var group = rawGroup.OrderBy(m => m.Id).ToList();
                var representative = group[0];
                var senseId = InsertSense(connection, word.Id, representative, initialSenseStatus, now);

                foreach (var meaning in group)
                {
                    context.SenseIdByMeaningId[meaning.Id] = senseId;
                }

                var groupMeaningIds = group.Select(m => m.Id).ToHashSet();
                var cardsForGroup = wordCards?.Where(c => groupMeaningIds.Contains(c.PreferredMeaningId)).ToList()
                    ?? [];
                var directionsPresent = cardsForGroup.Select(c => c.Direction).Distinct().ToHashSet();

                foreach (var card in cardsForGroup)
                {
                    context.SenseIdByCardId[card.Id] = senseId;
                }

                // Focused invariant 2: for every existing card direction, exactly one assignment must be
                // preferred, chosen deterministically from whichever Meanings in the group actually carry
                // an expression for that direction — never invented when none do.
                if (directionsPresent.Contains(CardDirection.MeaningToTerm))
                {
                    CreatePreferredAssignmentForDirection(
                        connection, senseId, CardDirection.MeaningToTerm, group,
                        static m => m.DisplayTerm, static m => m.SourceLanguage, context, now);
                }

                if (directionsPresent.Contains(CardDirection.TermToMeaning))
                {
                    CreatePreferredAssignmentForDirection(
                        connection, senseId, CardDirection.TermToMeaning, group,
                        static m => m.Translation, static m => m.ExplanationLanguage, context, now);
                }

                // Losslessness: every remaining distinct answer expression — every Meaning's DisplayTerm,
                // Translation, and every accepted alias (Focused invariant 3: aliases are always
                // MeaningToTerm-only, AcceptedOnly, and never preferred — Decision 12: never Required) —
                // becomes a real, usable AnswerVariant with an AcceptedOnly, non-preferred assignment.
                // GetOrCreateAnswerVariant deduplicates by the exact (SenseId, AnswerLanguage,
                // NormalizedText) triple and never overwrites an already-selected SourceMeaningId on a
                // dedup hit; EnsureAssignment never touches a row that already exists, so it can never
                // downgrade or duplicate the preferred assignment created above. Iterating the same
                // ascending-Id `group` guarantees the first writer for any duplicate expression — here and
                // above — is always the lowest-Id contributing Meaning.
                foreach (var meaning in group)
                {
                    if (directionsPresent.Contains(CardDirection.MeaningToTerm) && !string.IsNullOrEmpty(meaning.DisplayTerm))
                    {
                        var termVariantId = GetOrCreateAnswerVariant(connection, senseId, meaning.SourceLanguage, meaning.DisplayTerm, meaning.Id, now);
                        EnsureAssignment(
                            connection, senseId, CardDirection.MeaningToTerm, termVariantId,
                            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, now);
                    }

                    if (directionsPresent.Contains(CardDirection.TermToMeaning) && !string.IsNullOrEmpty(meaning.Translation))
                    {
                        var explanationVariantId = GetOrCreateAnswerVariant(connection, senseId, meaning.ExplanationLanguage, meaning.Translation, meaning.Id, now);
                        EnsureAssignment(
                            connection, senseId, CardDirection.TermToMeaning, explanationVariantId,
                            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, now);
                    }

                    if (directionsPresent.Contains(CardDirection.MeaningToTerm))
                    {
                        foreach (var alias in DeserializeAliases(meaning.AcceptedAliasesJson).Distinct(StringComparer.Ordinal))
                        {
                            if (string.IsNullOrWhiteSpace(alias))
                            {
                                continue;
                            }

                            var aliasVariantId = GetOrCreateAnswerVariant(connection, senseId, meaning.SourceLanguage, alias, meaning.Id, now);
                            EnsureAssignment(
                                connection, senseId, CardDirection.MeaningToTerm, aliasVariantId,
                                AnswerVariantRequirement.AcceptedOnly, isPreferred: false, now);
                        }
                    }
                }
            }
        }

        return context;
    }

    private static List<List<LegacyMeaningRow>> GroupMeaningsIntoSenses(
        List<LegacyMeaningRow> meanings, VocabularyIdentity vocabularyIdentity)
    {
        var groups = new List<List<LegacyMeaningRow>>();
        var groupKeys = new List<string?>();

        foreach (var meaning in meanings)
        {
            var preparedItem = BuildPreparedItem(meaning);
            var hasDiscriminator = SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(preparedItem);
            var identity = SemanticMeaningIdentityPolicy.Compute(preparedItem, vocabularyIdentity);

            if (hasDiscriminator)
            {
                var existingGroupIndex = groupKeys.FindIndex(k => k == identity.Value);
                if (existingGroupIndex >= 0)
                {
                    groups[existingGroupIndex].Add(meaning);
                    continue;
                }
            }

            groups.Add([meaning]);
            groupKeys.Add(hasDiscriminator ? identity.Value : null);
        }

        return groups;
    }

    private static VocabularyIdentity ComputeVocabularyIdentity(LegacyWordRow word)
    {
        var resolution = KnownFirst.Core.Text.VocabularyIdentityPolicy.Resolve(word.CanonicalTerm, word.TokenKind, word.Language);
        return VocabularyMergeIdentityPolicy.Compute(word.Language, resolution.Identity);
    }

    private static BackupPreparedItem BuildPreparedItem(LegacyMeaningRow meaning) =>
        new(
            Id: meaning.Id.ToString(CultureInfo.InvariantCulture),
            VocabularyId: string.Empty,
            SourceLanguage: meaning.SourceLanguage,
            ExplanationLanguage: meaning.ExplanationLanguage,
            DisplayTerm: meaning.DisplayTerm,
            EncounteredSurfaceForm: meaning.EncounteredSurfaceForm,
            GrammaticalRelationship: meaning.GrammaticalRelationship,
            TokenKind: (BackupTokenKind)(int)meaning.TokenKind,
            ProviderMeaningId: meaning.SelectedMeaningId,
            AcronymExpansion: meaning.AcronymExpansion,
            Translation: meaning.Translation,
            Definition: meaning.Definition,
            DictionaryExample: meaning.DictionaryExample,
            AdditionalNote: meaning.AdditionalNote,
            LegacyAnswerText: null,
            AcceptedAliases: DeserializeAliases(meaning.AcceptedAliasesJson),
            ConfirmedByUser: meaning.ConfirmedByUser,
            Source: new BackupSourceReference(meaning.Source, meaning.SourceProject, meaning.SourcePageTitle, meaning.SourceRevisionId, meaning.Attribution),
            CreatedAtUtc: meaning.CreatedAt,
            UpdatedAtUtc: meaning.UpdatedAt,
            PreparedAtUtc: meaning.CreatedAt,
            Contexts: []);

    private static SenseStatus TranslateInitialSenseStatus(WordStatus status) => status switch
    {
        WordStatus.Prepared => SenseStatus.Prepared,
        WordStatus.Learning => SenseStatus.Learning,
        WordStatus.Mastered => SenseStatus.Mastered,
        _ => SenseStatus.Prepared
    };

    private static string[] DeserializeAliases(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize(json, LexicalJsonSerializerContext.Default.StringArray) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NewStableId() => Guid.NewGuid().ToString("N");

    private static int InsertSense(
        SQLiteConnection connection, int wordId, LegacyMeaningRow representative, SenseStatus status, DateTime now)
    {
        connection.Execute(
            """
            INSERT INTO Senses
                (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                 PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, '', '', ?, ?, ?, ?, ?, ?)
            """,
            NewStableId(), wordId, representative.SourceLanguage, representative.ExplanationLanguage,
            representative.SelectedMeaningId, representative.GrammaticalRelationship, representative.AcronymExpansion,
            representative.Id, (int)status, now, now);

        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    private static int GetOrCreateAnswerVariant(
        SQLiteConnection connection, int senseId, string answerLanguage, string displayText, int sourceMeaningId, DateTime now)
    {
        var normalized = CanonicalText.NormalizeOptional(displayText);
        var existingId = connection.ExecuteScalar<int?>(
            "SELECT Id FROM AnswerVariants WHERE SenseId = ? AND AnswerLanguage = ? AND NormalizedText = ?",
            senseId, answerLanguage, normalized);
        if (existingId is int found)
        {
            return found;
        }

        connection.Execute(
            """
            INSERT INTO AnswerVariants (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            NewStableId(), senseId, answerLanguage, displayText, normalized, sourceMeaningId, now, now);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    private static void CreateAssignment(
        SQLiteConnection connection, int senseId, CardDirection direction, int answerVariantId,
        AnswerVariantRequirement requirement, bool isPreferred, DateTime now)
    {
        connection.Execute(
            """
            INSERT INTO SenseAnswerVariantAssignments
                (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            NewStableId(), senseId, (int)direction, answerVariantId, (int)requirement, isPreferred, now, now);
    }

    private static void EnsureAssignment(
        SQLiteConnection connection, int senseId, CardDirection direction, int answerVariantId,
        AnswerVariantRequirement requirement, bool isPreferred, DateTime now)
    {
        var exists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ? AND AnswerVariantId = ?",
            senseId, (int)direction, answerVariantId) > 0;
        if (exists)
        {
            // Never downgrade or duplicate an existing assignment (e.g. the preferred term assignment
            // already created for the same variant text) — Decision 12 only ever adds AcceptedOnly rows.
            return;
        }

        CreateAssignment(connection, senseId, direction, answerVariantId, requirement, isPreferred, now);
    }

    /// <summary>
    /// Focused invariant 2: creates the single preferred (AcceptedOnly, <c>IsPreferred = true</c>)
    /// assignment for one <paramref name="direction"/> of a Sense, or does nothing if no Meaning in the
    /// group carries an assignable expression for that direction (never invents one). The candidate is
    /// chosen deterministically — lowest legacy Meaning.Id first, then lowest normalized text (ordinal) —
    /// which always selects the representative Meaning when it contributes, since the representative is
    /// itself always the group's lowest-Id Meaning (Focused invariant 1). Must be called before any other
    /// assignment is created for this exact (SenseId, CardDirection) pair.
    /// </summary>
    private static void CreatePreferredAssignmentForDirection(
        SQLiteConnection connection,
        int senseId,
        CardDirection direction,
        List<LegacyMeaningRow> group,
        Func<LegacyMeaningRow, string> textSelector,
        Func<LegacyMeaningRow, string> languageSelector,
        MigrationContext context,
        DateTime now)
    {
        var candidates = group.Where(m => !string.IsNullOrEmpty(textSelector(m))).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var chosen = candidates
            .OrderBy(m => m.Id)
            .ThenBy(m => CanonicalText.NormalizeOptional(textSelector(m)), StringComparer.Ordinal)
            .First();

        var variantId = GetOrCreateAnswerVariant(connection, senseId, languageSelector(chosen), textSelector(chosen), chosen.Id, now);
        CreateAssignment(connection, senseId, direction, variantId, AnswerVariantRequirement.AcceptedOnly, isPreferred: true, now);
        context.DirectionVariantId[(senseId, direction)] = variantId;
    }

    // ---- Step 3: backfill Meaning / Card / Context / Review / Queue / Progress references ----

    private static void Step3_Backfill(SQLiteConnection connection, MigrationContext context)
    {
        var now = DateTime.UtcNow;

        foreach (var (meaningId, senseId) in context.SenseIdByMeaningId)
        {
            connection.Execute("UPDATE Meanings SET SenseId = ?, StableId = ? WHERE Id = ?", senseId, NewStableId(), meaningId);
        }

        foreach (var (cardId, senseId) in context.SenseIdByCardId)
        {
            connection.Execute("UPDATE LearningCards SET SenseId = ? WHERE Id = ?", senseId, cardId);
        }

        var contexts = connection.Query<LegacyContextRow>("SELECT Id, MeaningId FROM ContextSnapshots");
        foreach (var snapshot in contexts)
        {
            if (context.SenseIdByMeaningId.TryGetValue(snapshot.MeaningId, out var senseId))
            {
                connection.Execute("UPDATE ContextSnapshots SET SenseId = ? WHERE Id = ?", senseId, snapshot.Id);
            }
        }

        var cardDirectionById = connection
            .Query<LegacyCardRow>("SELECT Id, WordId, PreferredMeaningId, Direction FROM LearningCards")
            .ToDictionary(c => c.Id, c => c.Direction);

        var reviews = connection.Query<LegacyReviewRow>("SELECT Id, CardId, WasTypedAnswer, WasCorrect FROM LearningReviews");
        foreach (var review in reviews)
        {
            if (!context.SenseIdByCardId.TryGetValue(review.CardId, out var senseId)
                || !cardDirectionById.TryGetValue(review.CardId, out var direction)
                || !context.DirectionVariantId.TryGetValue((senseId, direction), out var variantId))
            {
                continue;
            }

            var matched = review.WasCorrect && review.WasTypedAnswer ? variantId : (int?)null;
            connection.Execute(
                "UPDATE LearningReviews SET TargetAnswerVariantId = ?, MatchedAnswerVariantId = ? WHERE Id = ?",
                variantId, matched, review.Id);
        }

        var queueRows = connection.Query<LegacyQueueRow>("SELECT Id, CardId FROM LearningSessionCards");
        foreach (var queueRow in queueRows)
        {
            if (!context.SenseIdByCardId.TryGetValue(queueRow.CardId, out var senseId)
                || !cardDirectionById.TryGetValue(queueRow.CardId, out var direction)
                || !context.DirectionVariantId.TryGetValue((senseId, direction), out var variantId))
            {
                continue;
            }

            connection.Execute("UPDATE LearningSessionCards SET TargetAnswerVariantId = ? WHERE Id = ?", variantId, queueRow.Id);
        }

        BackfillAutomaticProgress(connection, context, cardDirectionById, now);
    }

    private static void BackfillAutomaticProgress(
        SQLiteConnection connection,
        MigrationContext context,
        Dictionary<int, CardDirection> cardDirectionById,
        DateTime now)
    {
        var words = connection.Query<LegacyWordAutomaticStateRow>(
            """
            SELECT Id, AutomaticInteractionMode, ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount,
                   ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt
            FROM Words
            """);

        var cardsByWordId = connection
            .Query<LegacyCardRow>("SELECT Id, WordId, PreferredMeaningId, Direction FROM LearningCards")
            .GroupBy(c => c.WordId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var word in words)
        {
            var hasNonDefaultState =
                word.ConsecutiveRecallSuccessCount != 0
                || word.ConsecutiveTypingSuccessCount != 0
                || word.ConsecutiveTypingFailureCount != 0
                || word.MasteryReviewExtensionScheduled;

            if (!hasNonDefaultState || !cardsByWordId.TryGetValue(word.Id, out var wordCards))
            {
                continue;
            }

            foreach (var card in wordCards)
            {
                if (!context.SenseIdByCardId.TryGetValue(card.Id, out var senseId)
                    || !context.DirectionVariantId.TryGetValue((senseId, card.Direction), out var variantId))
                {
                    continue;
                }

                connection.Execute(
                    """
                    INSERT INTO AnswerVariantProgress
                        (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                         ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                         MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, 1, ?, ?)
                    """,
                    card.Id, variantId, (int)word.AutomaticInteractionMode, word.ConsecutiveRecallSuccessCount,
                    word.ConsecutiveTypingSuccessCount, word.ConsecutiveTypingFailureCount, word.UpdatedAt,
                    word.MasteryReviewExtensionScheduled, word.CreatedAt, now);
            }
        }
    }

    // ---- Step 4: forward reference/count validation (fail-closed on referential corruption) ----

    private static void Step4_ValidateForwardReferences(SQLiteConnection connection, int preMigrationCardCount, int preMigrationMeaningCount)
    {
        var cardCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards");
        if (cardCount != preMigrationCardCount)
        {
            throw Schema8MigrationException.InvariantViolation(
                $"LearningCards row count changed during migration ({preMigrationCardCount} -> {cardCount}).");
        }

        var meaningCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Meanings");
        if (meaningCount != preMigrationMeaningCount)
        {
            throw Schema8MigrationException.InvariantViolation(
                $"Meanings row count changed during migration ({preMigrationMeaningCount} -> {meaningCount}).");
        }

        var orphanMeaning = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM Meanings m
            LEFT JOIN Senses s ON s.Id = m.SenseId
            WHERE m.SenseId IS NULL OR s.Id IS NULL OR s.WordId <> m.WordId
            """);
        if (orphanMeaning > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                $"{orphanMeaning} Meaning row(s) have no resolvable Sense belonging to the same Word (missing or cross-Word Word/Meaning reference).");
        }

        var orphanCard = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM LearningCards c
            LEFT JOIN Senses s ON s.Id = c.SenseId
            LEFT JOIN Meanings m ON m.Id = c.PreferredMeaningId
            WHERE c.SenseId IS NULL OR s.Id IS NULL OR m.Id IS NULL OR m.SenseId <> c.SenseId
            """);
        if (orphanCard > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                $"{orphanCard} LearningCard row(s) reference a missing Meaning or an unresolved Sense.");
        }

        var orphanContext = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM ContextSnapshots x
            LEFT JOIN Senses s ON s.Id = x.SenseId
            WHERE x.SenseId IS NULL OR s.Id IS NULL
            """);
        if (orphanContext > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                $"{orphanContext} ContextSnapshot row(s) reference a Meaning that never resolved to a Sense.");
        }

        var orphanReviewCard = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningCards c ON c.Id = r.CardId WHERE c.Id IS NULL");
        if (orphanReviewCard > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                $"{orphanReviewCard} LearningReview row(s) reference a missing LearningCard.");
        }

        var orphanQueueCard = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN LearningCards c ON c.Id = q.CardId WHERE c.Id IS NULL");
        if (orphanQueueCard > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                $"{orphanQueueCard} LearningSessionCard row(s) reference a missing LearningCard.");
        }

        var orphanReviewTarget = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN AnswerVariants v ON v.Id = r.TargetAnswerVariantId WHERE r.TargetAnswerVariantId IS NOT NULL AND v.Id IS NULL");
        var orphanReviewMatched = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN AnswerVariants v ON v.Id = r.MatchedAnswerVariantId WHERE r.MatchedAnswerVariantId IS NOT NULL AND v.Id IS NULL");
        if (orphanReviewTarget > 0 || orphanReviewMatched > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                "A LearningReview target/matched AnswerVariant reference does not resolve.");
        }

        var orphanQueueTarget = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN AnswerVariants v ON v.Id = q.TargetAnswerVariantId WHERE q.TargetAnswerVariantId IS NOT NULL AND v.Id IS NULL");
        if (orphanQueueTarget > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                "A LearningSessionCard target AnswerVariant reference does not resolve.");
        }

        var badAssignment = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments a
            LEFT JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE v.Id IS NULL OR v.SenseId <> a.SenseId
            """);
        if (badAssignment > 0)
        {
            throw Schema8MigrationException.ReferentialCorruption(
                $"{badAssignment} SenseAnswerVariantAssignment row(s) reference an AnswerVariant outside their own Sense.");
        }
    }

    // ---- Step 5/6: card index cutover ----

    private static void Step5_DropOldCardIndex(SQLiteConnection connection) =>
        connection.Execute($"DROP INDEX IF EXISTS {Schema8Ddl.OldCardIndexName}");

    private static void Step6_CreateNewCardIndex(SQLiteConnection connection)
    {
        try
        {
            connection.Execute(Schema8Ddl.IndexLearningCardsSenseDirection);
        }
        catch (SQLiteException ex)
        {
            throw Schema8MigrationException.InvariantViolation(
                $"Could not create IX_LearningCards_Sense_Direction (duplicate (SenseId, Direction) pair): {ex.Message}");
        }
    }

    // ---- Step 7: final invariant validation gate ----

    private static void Step7_ValidateFinalInvariants(SQLiteConnection connection)
    {
        var duplicatePreferred = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM (
                SELECT SenseId, CardDirection FROM SenseAnswerVariantAssignments
                WHERE IsPreferred = 1
                GROUP BY SenseId, CardDirection
                HAVING COUNT(*) > 1
            )
            """);
        if (duplicatePreferred > 0)
        {
            throw Schema8MigrationException.InvariantViolation(
                "More than one preferred assignment exists for a (SenseId, CardDirection) pair.");
        }

        var duplicateAssignmentTriple = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM (
                SELECT SenseId, CardDirection, AnswerVariantId FROM SenseAnswerVariantAssignments
                GROUP BY SenseId, CardDirection, AnswerVariantId
                HAVING COUNT(*) > 1
            )
            """);
        if (duplicateAssignmentTriple > 0)
        {
            throw Schema8MigrationException.InvariantViolation(
                "Duplicate (SenseId, CardDirection, AnswerVariantId) assignment rows exist.");
        }

        var badWordStatus = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Words WHERE Status IN (?, ?, ?)",
            (int)WordStatus.Prepared, (int)WordStatus.Learning, (int)WordStatus.Mastered);
        if (badWordStatus > 0)
        {
            throw Schema8MigrationException.InvariantViolation(
                "A Word row still holds a Prepared/Learning/Mastered status after migration.");
        }

        ValidateStableIdsNonEmpty(connection, "Senses");
        ValidateStableIdsNonEmpty(connection, "AnswerVariants");
        ValidateStableIdsNonEmpty(connection, "SenseAnswerVariantAssignments");
        ValidateStableIdsNonEmpty(connection, "Meanings");

        var oldIndexExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = ?", Schema8Ddl.OldCardIndexName) > 0;
        if (oldIndexExists)
        {
            throw Schema8MigrationException.InvariantViolation($"{Schema8Ddl.OldCardIndexName} still exists after migration.");
        }

        var newIndexExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_LearningCards_Sense_Direction'") > 0;
        if (!newIndexExists)
        {
            throw Schema8MigrationException.InvariantViolation("IX_LearningCards_Sense_Direction was not created.");
        }

        if (HasColumn(connection, "LearningCards", "MeaningId"))
        {
            throw Schema8MigrationException.InvariantViolation("LearningCards.MeaningId column still exists after migration.");
        }
    }

    private static void ValidateStableIdsNonEmpty(SQLiteConnection connection, string table)
    {
        var emptyCount = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE StableId IS NULL OR TRIM(StableId) = ''");
        if (emptyCount > 0)
        {
            throw Schema8MigrationException.InvariantViolation($"{table} has {emptyCount} row(s) with an empty StableId.");
        }
    }

    private static bool HasColumn(SQLiteConnection connection, string table, string column) =>
        connection.Query<TableColumnInfo>($"PRAGMA table_info({table})")
            .Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

    // ---- Step 8: version cutover ----

    private static void Step8_SetVersion(SQLiteConnection connection) =>
        connection.Execute($"PRAGMA user_version = {TargetVersion}");

    // ---- Already-applied (target version) validation, no mutation ----

    private static void ValidateAlreadyMigratedShape(SQLiteConnection connection)
    {
        foreach (var table in new[] { "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "AnswerVariantProgress" })
        {
            var exists = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table) > 0;
            if (!exists)
            {
                throw Schema8MigrationException.AlreadyAppliedShapeInvalid($"Required table '{table}' is missing.");
            }
        }

        if (HasColumn(connection, "LearningCards", "MeaningId"))
        {
            throw Schema8MigrationException.AlreadyAppliedShapeInvalid("LearningCards still has a legacy MeaningId column.");
        }

        if (!HasColumn(connection, "LearningCards", "PreferredMeaningId"))
        {
            throw Schema8MigrationException.AlreadyAppliedShapeInvalid("LearningCards is missing the PreferredMeaningId column.");
        }

        var newIndexExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_LearningCards_Sense_Direction'") > 0;
        if (!newIndexExists)
        {
            throw Schema8MigrationException.AlreadyAppliedShapeInvalid("IX_LearningCards_Sense_Direction is missing.");
        }
    }
}
