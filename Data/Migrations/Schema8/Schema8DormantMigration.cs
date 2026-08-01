using KnownFirst.Core.Learning;
using KnownFirst.Models;
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

    /// <summary>
    /// KF-MEANING-001 Slice 4: the legacy card projection additionally carries <c>State</c> (mastered
    /// compatibility row for a legacy Retired card), <c>CreatedAtUtc</c> (the Required-epoch boundary of the
    /// migration-created primary assignment) and <c>LastReviewedAtUtc</c> (deterministic compatibility-row
    /// timestamps — never migration-execution time).
    /// </summary>
    private const string LegacyCardSelect =
        "SELECT Id, WordId, PreferredMeaningId, Direction, State, CreatedAtUtc, LastReviewedAtUtc FROM LearningCards";

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
            .Query<LegacyCardRow>(LegacyCardSelect)
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

            var vocabularyIdentity = Schema8SemanticUpgradePolicy.ComputeVocabularyIdentity(word);
            var groups = Schema8SemanticUpgradePolicy.GroupMeaningsIntoSenses(meanings, vocabularyIdentity);
            var initialSenseStatus = Schema8SemanticUpgradePolicy.TranslateInitialSenseStatus(word.Status);

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
                        connection, senseId, CardDirection.MeaningToTerm, group, cardsForGroup,
                        static m => m.DisplayTerm, static m => m.SourceLanguage, context, now);
                }

                if (directionsPresent.Contains(CardDirection.TermToMeaning))
                {
                    CreatePreferredAssignmentForDirection(
                        connection, senseId, CardDirection.TermToMeaning, group, cardsForGroup,
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
                        EnsureAcceptedOnlyAssignment(
                            connection, senseId, CardDirection.MeaningToTerm, termVariantId, now);
                    }

                    if (directionsPresent.Contains(CardDirection.TermToMeaning) && !string.IsNullOrEmpty(meaning.Translation))
                    {
                        var explanationVariantId = GetOrCreateAnswerVariant(connection, senseId, meaning.ExplanationLanguage, meaning.Translation, meaning.Id, now);
                        EnsureAcceptedOnlyAssignment(
                            connection, senseId, CardDirection.TermToMeaning, explanationVariantId, now);
                    }

                    if (directionsPresent.Contains(CardDirection.MeaningToTerm))
                    {
                        foreach (var alias in Schema8SemanticUpgradePolicy.DeserializeAliases(meaning.AcceptedAliasesJson).Distinct(StringComparer.Ordinal))
                        {
                            if (string.IsNullOrWhiteSpace(alias))
                            {
                                continue;
                            }

                            var aliasVariantId = GetOrCreateAnswerVariant(connection, senseId, meaning.SourceLanguage, alias, meaning.Id, now);
                            EnsureAcceptedOnlyAssignment(
                                connection, senseId, CardDirection.MeaningToTerm, aliasVariantId, now);
                        }
                    }
                }
            }
        }

        return context;
    }

    // GroupMeaningsIntoSenses, ComputeVocabularyIdentity, BuildPreparedItem,
    // TranslateInitialSenseStatus, and DeserializeAliases moved to the shared, connection-free
    // Schema8SemanticUpgradePolicy (KF-MEANING-001 Slice 2) so the archive-format v1->v2 in-memory
    // upgrader (BackupArchiveV1UpgradePolicy) uses the identical Sense-grouping algorithm rather than
    // a second, independently-written copy. Purely mechanical move — behavior unchanged.

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

    /// <summary>
    /// Inserts one assignment row. KF-MEANING-001 Slice 4 binding invariant, enforced by the caller
    /// contract and re-asserted in <see cref="Step7_ValidateFinalInvariants"/>:
    /// <paramref name="requiredSinceUtc"/> is non-null exactly when
    /// <paramref name="requirement"/> is <see cref="AnswerVariantRequirement.Required"/>.
    /// </summary>
    private static void CreateAssignment(
        SQLiteConnection connection, int senseId, CardDirection direction, int answerVariantId,
        AnswerVariantRequirement requirement, bool isPreferred, DateTime? requiredSinceUtc, DateTime now)
    {
        if ((requirement == AnswerVariantRequirement.Required) != requiredSinceUtc.HasValue)
        {
            throw Schema8MigrationException.InvariantViolation(
                $"Assignment for Sense {senseId}/{direction}/variant {answerVariantId} violates " +
                "'Requirement = Required if and only if RequiredSinceUtc is not null'.");
        }

        connection.Execute(
            """
            INSERT INTO SenseAnswerVariantAssignments
                (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            NewStableId(), senseId, (int)direction, answerVariantId, (int)requirement, isPreferred,
            requiredSinceUtc, now, now);
    }

    /// <summary>
    /// Adds an <see cref="AnswerVariantRequirement.AcceptedOnly"/>, non-preferred, null-boundary assignment
    /// unless the exact <c>(SenseId, CardDirection, AnswerVariantId)</c> triple already exists. Never
    /// downgrades or duplicates an existing row — in particular it can never displace or demote the primary
    /// Required assignment created by <see cref="CreatePreferredAssignmentForDirection"/> (Decision 12).
    /// </summary>
    private static void EnsureAcceptedOnlyAssignment(
        SQLiteConnection connection, int senseId, CardDirection direction, int answerVariantId, DateTime now)
    {
        var exists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ? AND AnswerVariantId = ?",
            senseId, (int)direction, answerVariantId) > 0;
        if (exists)
        {
            return;
        }

        CreateAssignment(
            connection, senseId, direction, answerVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null, now);
    }

    /// <summary>
    /// Focused invariant 2, corrected by KF-MEANING-001 Slice 4: creates the single primary assignment for
    /// one <paramref name="direction"/> of a Sense as <see cref="AnswerVariantRequirement.Required"/> and
    /// preferred, with <c>RequiredSinceUtc</c> set to the affected card's own <c>CreatedAtUtc</c> so the
    /// card's entire legacy review history stays inside the first Required epoch. Does nothing when no
    /// Meaning in the group carries an assignable expression for that direction (never invents one). The
    /// candidate is chosen deterministically — lowest legacy Meaning.Id first, then lowest normalized text
    /// (ordinal) — which always selects the representative Meaning when it contributes, since the
    /// representative is itself always the group's lowest-Id Meaning (Focused invariant 1). Must be called
    /// before any other assignment is created for this exact (SenseId, CardDirection) pair.
    /// </summary>
    private static void CreatePreferredAssignmentForDirection(
        SQLiteConnection connection,
        int senseId,
        CardDirection direction,
        List<LegacyMeaningRow> group,
        List<LegacyCardRow> cardsForGroup,
        Func<LegacyMeaningRow, string> textSelector,
        Func<LegacyMeaningRow, string> languageSelector,
        MigrationContext context,
        DateTime now)
    {
        var chosen = Schema8SemanticUpgradePolicy.SelectPreferredCandidate(group, textSelector);
        if (chosen is null)
        {
            return;
        }

        // IX_LearningCards_Word_Direction is unique on (WordId, Direction), so at most one legacy card of
        // this Word — and therefore of this Sense group — can exist per direction.
        var card = cardsForGroup.SingleOrDefault(c => c.Direction == direction);
        if (card is null)
        {
            return;
        }

        var variantId = GetOrCreateAnswerVariant(connection, senseId, languageSelector(chosen), textSelector(chosen), chosen.Id, now);
        CreateAssignment(
            connection, senseId, direction, variantId,
            AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: card.CreatedAtUtc, now);
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
            .Query<LegacyCardRow>(LegacyCardSelect)
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

    /// <summary>
    /// Migrates word-level automatic counters into per-<c>(CardId, AnswerVariantId)</c> progress rows, and —
    /// KF-MEANING-001 Slice 4 — additionally seeds a mastered compatibility row for every legacy
    /// <see cref="CardState.Retired"/> card whose word carries only default counters, so activation never
    /// reactivates an already-mastered legacy card.
    /// <para>
    /// Every timestamp is derived from the card, never from migration-execution time, so the migration stays
    /// deterministic and the seeded row is tick-equal to its assignment's <c>RequiredSinceUtc</c>
    /// (both are <c>card.CreatedAtUtc</c>) and therefore counts as current-Required-epoch progress.
    /// </para>
    /// </summary>
    private static void BackfillAutomaticProgress(
        SQLiteConnection connection,
        MigrationContext context,
        Dictionary<int, CardDirection> cardDirectionById,
        DateTime now)
    {
        var words = connection
            .Query<LegacyWordAutomaticStateRow>(
                """
                SELECT Id, AutomaticInteractionMode, ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount,
                       ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt
                FROM Words
                """)
            .ToDictionary(w => w.Id);

        var cards = connection
            .Query<LegacyCardRow>(LegacyCardSelect)
            .OrderBy(c => c.Id)
            .ToList();

        foreach (var card in cards)
        {
            if (!context.SenseIdByCardId.TryGetValue(card.Id, out var senseId)
                || !context.DirectionVariantId.TryGetValue((senseId, card.Direction), out var variantId)
                || !words.TryGetValue(card.WordId, out var word))
            {
                continue;
            }

            var hasNonDefaultState =
                word.ConsecutiveRecallSuccessCount != 0
                || word.ConsecutiveTypingSuccessCount != 0
                || word.ConsecutiveTypingFailureCount != 0
                || word.MasteryReviewExtensionScheduled;
            var isRetired = card.State == CardState.Retired;

            if (!hasNonDefaultState && !isRetired)
            {
                continue;
            }

            var interactionMode = hasNonDefaultState
                ? word.AutomaticInteractionMode
                : LearningInteractionMode.Typing;
            var readingSuccesses = hasNonDefaultState ? word.ConsecutiveRecallSuccessCount : 0;
            var typingSuccesses = hasNonDefaultState
                ? word.ConsecutiveTypingSuccessCount
                : AutomaticLearningPolicy.RequiredConsecutiveAssessments;
            var typingFailures = hasNonDefaultState ? word.ConsecutiveTypingFailureCount : 0;
            var extensionScheduled = hasNonDefaultState && word.MasteryReviewExtensionScheduled;

            connection.Execute(
                """
                INSERT INTO AnswerVariantProgress
                    (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                     ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                     MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)
                """,
                card.Id, variantId, (int)interactionMode, readingSuccesses,
                typingSuccesses, typingFailures, card.LastReviewedAtUtc,
                extensionScheduled, isRetired, card.CreatedAtUtc,
                card.LastReviewedAtUtc ?? card.CreatedAtUtc);
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

        // KF-MEANING-001 Slice 4 invariant I1: Requirement = Required if and only if RequiredSinceUtc is
        // non-null. Re-asserted explicitly here, before PRAGMA user_version becomes 8.
        var badBoundary = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments
            WHERE (Requirement = ? AND RequiredSinceUtc IS NULL)
               OR (Requirement <> ? AND RequiredSinceUtc IS NOT NULL)
            """,
            (int)AnswerVariantRequirement.Required, (int)AnswerVariantRequirement.Required);
        if (badBoundary > 0)
        {
            throw Schema8MigrationException.InvariantViolation(
                $"{badBoundary} assignment row(s) violate 'Requirement = Required if and only if RequiredSinceUtc is not null'.");
        }

        // Every progress row created by this migration must be current-Required-epoch progress, i.e. its
        // CreatedAtUtc must equal its assignment's RequiredSinceUtc, otherwise replay would reset it and an
        // already-mastered legacy card would reactivate at activation time.
        var seedOutsideEpoch = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM AnswerVariantProgress p
            JOIN LearningCards c ON c.Id = p.CardId
            JOIN SenseAnswerVariantAssignments a
              ON a.SenseId = c.SenseId AND a.CardDirection = c.Direction AND a.AnswerVariantId = p.AnswerVariantId
            WHERE a.Requirement = ? AND (a.RequiredSinceUtc IS NULL OR p.CreatedAtUtc <> a.RequiredSinceUtc)
            """,
            (int)AnswerVariantRequirement.Required);
        if (seedOutsideEpoch > 0)
        {
            throw Schema8MigrationException.InvariantViolation(
                $"{seedOutsideEpoch} migrated progress row(s) are not current-Required-epoch progress " +
                "(CreatedAtUtc does not match the assignment's RequiredSinceUtc).");
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
        Schema8ShapeValidator.HasColumn(connection, table, column);

    // ---- Step 8: version cutover ----

    private static void Step8_SetVersion(SQLiteConnection connection) =>
        connection.Execute($"PRAGMA user_version = {TargetVersion}");

    // ---- Already-applied (target version) validation, no mutation ----

    // KF-MEANING-001 Slice 2: delegates to the shared Schema8ShapeValidator (also used by
    // Services/DataSafety/BackupSchemaCapability) so there is exactly one source of truth for what a
    // physically valid Schema-8 shape looks like.
    private static void ValidateAlreadyMigratedShape(SQLiteConnection connection)
    {
        if (!Schema8ShapeValidator.IsValidShape(connection, out var failureDetail))
        {
            throw Schema8MigrationException.AlreadyAppliedShapeInvalid(failureDetail!);
        }
    }
}
