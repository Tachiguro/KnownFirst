namespace KnownFirst.Tests;

using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Targets;
using SQLite;

[TestClass]
[DoNotParallelize]
public sealed class LearningTargetPersistenceTests
{
    private static async Task<(SQLiteAsyncConnection Connection, string Path)> CreateFreshDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"knownfirst-target-tests-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(path);
        await DatabaseSchema.InitializeAsync(connection);
        return (connection, path);
    }

    private static async Task CleanupAsync(SQLiteAsyncConnection? connection, string path)
    {
        if (connection is not null)
        {
            await connection.CloseAsync();
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static int SeedSense(SQLiteConnection connection, string word = "Haus", string lang = "de")
    {
        connection.Execute(
            "INSERT INTO Words (Language, CanonicalTerm, NormalizedTerm, Status, CreatedAt, UpdatedAt) VALUES (?, ?, ?, 0, 0, 0)",
            lang, word, word.ToLowerInvariant());
        var wordId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");

        connection.Execute(
            """
            INSERT INTO Senses (StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, 0, '2026-09-04T00:00:00.0000000Z', '2026-09-04T00:00:00.0000000Z')
            """,
            Guid.NewGuid().ToString("N"), wordId, lang, lang);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    private static int SeedMeaning(SQLiteConnection connection, int senseId, string definition = "a building")
    {
        var wordId = connection.ExecuteScalar<int>("SELECT WordId FROM Senses WHERE Id = ?", senseId);
        connection.Execute(
            """
            INSERT INTO Meanings (WordId, SenseId, SourceLanguage, ExplanationLanguage, Definition, CreatedAt, UpdatedAt)
            VALUES (?, ?, 'de', 'en', ?, 0, 0)
            """,
            wordId, senseId, definition);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    // ========================================================================
    // A. Clean bootstrap / shape tests
    // ========================================================================

    [TestMethod]
    public async Task InitializeAsync_FreshDatabase_CreatesLearningTargetsTable()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var tableCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LearningTargets'");
            Assert.IsTrue(tableCount > 0, "Table 'LearningTargets' must exist after database initialization.");
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task CleanBootstrap_CreatesValidTargetPersistenceShapeAndIndexes()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var foreignKeys = await connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys");
            Assert.AreEqual(1, foreignKeys, "Foreign-key enforcement must be ON.");

            await connection.RunInTransactionAsync(conn =>
            {
                Assert.IsTrue(
                    TargetPersistenceShapeValidator.Validate(conn, out var failureDetail),
                    $"Shape validator failed: {failureDetail}");

                var fkCheck = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check");
                Assert.AreEqual(0, fkCheck, "No foreign key violations allowed in clean bootstrap.");
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task TargetPersistenceShapeValidator_MissingTargetTable_FailsClosed()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            await connection.RunInTransactionAsync(conn =>
            {
                conn.Execute("DROP TABLE TargetAnswerVariants");
                var valid = TargetPersistenceShapeValidator.Validate(conn, out var failureDetail);
                Assert.IsFalse(valid);
                Assert.IsNotNull(failureDetail);
                StringAssert.Contains(failureDetail, "TargetAnswerVariants");
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task TargetPersistenceShapeValidator_MissingIndex_FailsClosed()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            await connection.RunInTransactionAsync(conn =>
            {
                conn.Execute($"DROP INDEX {TargetPersistenceDdl.TargetAnswerVariantsPreferredIndexName}");
                var valid = TargetPersistenceShapeValidator.Validate(conn, out var failureDetail);
                Assert.IsFalse(valid);
                Assert.IsNotNull(failureDetail);
                StringAssert.Contains(failureDetail, TargetPersistenceDdl.TargetAnswerVariantsPreferredIndexName);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    // ========================================================================
    // B. LearningTarget identity
    // ========================================================================

    [TestMethod]
    public async Task LearningTargetIdentity_GermanAndEnglishDefinitions_CoexistUnderSameSense()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);

                var germanDef = LearningTarget.CreateDefinition("de", "de");
                var englishDef = LearningTarget.CreateDefinition("de", "en");

                var target1 = LearningTargetRepository.CreateTarget(conn, senseId, germanDef, now);
                var target2 = LearningTargetRepository.CreateTarget(conn, senseId, englishDef, now);

                Assert.AreNotEqual(target1.Id, target2.Id);
                Assert.AreEqual(LearningTargetKind.Definition, target1.TargetKind);
                Assert.AreEqual("de", target1.TargetLanguage);
                Assert.AreEqual(LearningTargetKind.Definition, target2.TargetKind);
                Assert.AreEqual("en", target2.TargetLanguage);

                var targets = LearningTargetRepository.GetTargetsForSense(conn, senseId);
                Assert.AreEqual(2, targets.Count);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task LearningTargetIdentity_TranslationsToDifferentLanguages_CoexistUnderSameSense()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);

                var deEn = LearningTarget.CreateTranslation("de", "en");
                var deFr = LearningTarget.CreateTranslation("de", "fr");

                var target1 = LearningTargetRepository.CreateTarget(conn, senseId, deEn, now);
                var target2 = LearningTargetRepository.CreateTarget(conn, senseId, deFr, now);

                Assert.AreNotEqual(target1.Id, target2.Id);
                Assert.AreEqual("en", target1.TargetLanguage);
                Assert.AreEqual("fr", target2.TargetLanguage);

                var loaded1 = LearningTargetRepository.FindTargetByIdentity(conn, senseId, deEn.Identity);
                var loaded2 = LearningTargetRepository.FindTargetByIdentity(conn, senseId, deFr.Identity);
                Assert.IsNotNull(loaded1);
                Assert.IsNotNull(loaded2);
                Assert.AreEqual(target1.Id, loaded1.Id);
                Assert.AreEqual(target2.Id, loaded2.Id);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task LearningTargetIdentity_GermanToEnglishDiffersFromEnglishToGerman()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);

                var deEn = LearningTarget.CreateTranslation("de", "en");
                var enDe = LearningTarget.CreateTranslation("en", "de");

                var targetDeEn = LearningTargetRepository.CreateTarget(conn, senseId, deEn, now);
                var targetEnDe = LearningTargetRepository.CreateTarget(conn, senseId, enDe, now);

                Assert.AreNotEqual(targetDeEn.Id, targetEnDe.Id);
                Assert.AreEqual("de", targetDeEn.SourceLanguage);
                Assert.AreEqual("en", targetDeEn.TargetLanguage);
                Assert.AreEqual("en", targetEnDe.SourceLanguage);
                Assert.AreEqual("de", targetEnDe.TargetLanguage);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task LearningTargetIdentity_DuplicateIdenticalTarget_RejectedOrIdempotentlyResolved()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);
                var target = LearningTarget.CreateTranslation("de", "en");

                var created = LearningTargetRepository.CreateTarget(conn, senseId, target, now);

                // Direct insert of duplicate identity fails SQLite unique constraint
                Assert.ThrowsExactly<SQLiteException>(() =>
                    LearningTargetRepository.CreateTarget(conn, senseId, target, now));

                // GetOrCreateTarget returns existing target idempotently
                var resolved = LearningTargetRepository.GetOrCreateTarget(conn, senseId, target, now);
                Assert.AreEqual(created.Id, resolved.Id);
                Assert.AreEqual(created.StableId, resolved.StableId);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task LearningTargetIdentity_AnotherTargetUnderSameSense_IsNotMutated()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);

                var target1 = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateTranslation("de", "en", typingOptOut: false), now);
                var target2 = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateTranslation("de", "fr", typingOptOut: true), now);

                var loaded1 = LearningTargetRepository.GetTargetById(conn, target1.Id);
                Assert.IsNotNull(loaded1);
                Assert.IsFalse(loaded1.TypingOptOut);
                Assert.AreEqual("en", loaded1.TargetLanguage);

                var loaded2 = LearningTargetRepository.GetTargetById(conn, target2.Id);
                Assert.IsNotNull(loaded2);
                Assert.IsTrue(loaded2.TypingOptOut);
                Assert.AreEqual("fr", loaded2.TargetLanguage);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    // ========================================================================
    // C. Target typing configuration
    // ========================================================================

    [TestMethod]
    public async Task TypingConfiguration_DefaultsToEnabled_OptOutPersistsIndependently()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);

                var defaultTarget = LearningTarget.CreateTranslation("de", "en");
                Assert.IsFalse(defaultTarget.TypingOptOut, "Domain default must have typing enabled.");

                var persistedDefault = LearningTargetRepository.CreateTarget(conn, senseId, defaultTarget, now);
                Assert.IsFalse(persistedDefault.TypingOptOut);

                var optOutTarget = LearningTarget.CreateTranslation("de", "es", typingOptOut: true);
                var persistedOptOut = LearningTargetRepository.CreateTarget(conn, senseId, optOutTarget, now);
                Assert.IsTrue(persistedOptOut.TypingOptOut);

                // Update opt-out on the default target
                var updateTime = now.AddHours(1);
                LearningTargetRepository.SetTypingOptOut(conn, persistedDefault.Id, true, updateTime);

                var reloadedDefault = LearningTargetRepository.GetTargetById(conn, persistedDefault.Id);
                Assert.IsNotNull(reloadedDefault);
                Assert.IsTrue(reloadedDefault.TypingOptOut);
                Assert.AreEqual(updateTime, reloadedDefault.UpdatedAtUtc);

                // Ensure other target was untouched
                var reloadedOptOut = LearningTargetRepository.GetTargetById(conn, persistedOptOut.Id);
                Assert.IsNotNull(reloadedOptOut);
                Assert.IsTrue(reloadedOptOut.TypingOptOut);
                Assert.AreEqual(now, reloadedOptOut.UpdatedAtUtc);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    // ========================================================================
    // D. Target answer variants
    // ========================================================================

    [TestMethod]
    public async Task TargetAnswerVariants_RequiredPreferredAndAcceptedOnly_PersistAndLoadCorrectly()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);
                var meaningId = SeedMeaning(conn, senseId, "house");
                var target = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateTranslation("de", "en"), now);

                var primaryDraft = new TargetAnswerVariantDraft(
                    AnswerLanguage: "en",
                    DisplayText: "house",
                    Requirement: AnswerVariantRequirement.Required,
                    IsPreferred: true,
                    SourceMeaningId: meaningId);

                var secondaryDraft = new TargetAnswerVariantDraft(
                    AnswerLanguage: "en",
                    DisplayText: "home",
                    Requirement: AnswerVariantRequirement.AcceptedOnly,
                    IsPreferred: false,
                    SourceMeaningId: meaningId);

                var primary = LearningTargetRepository.AddAnswerVariant(conn, target.Id, primaryDraft, now);
                var secondary = LearningTargetRepository.AddAnswerVariant(conn, target.Id, secondaryDraft, now);

                Assert.IsTrue(primary.IsPreferred);
                Assert.IsTrue(primary.IsRequired);
                Assert.AreEqual(now, primary.RequiredSinceUtc);
                Assert.AreEqual(meaningId, primary.SourceMeaningId);

                Assert.IsFalse(secondary.IsPreferred);
                Assert.IsFalse(secondary.IsRequired);
                Assert.IsNull(secondary.RequiredSinceUtc);

                var preferred = LearningTargetRepository.GetPreferredVariant(conn, target.Id);
                Assert.IsNotNull(preferred);
                Assert.AreEqual(primary.Id, preferred.Id);

                var required = LearningTargetRepository.GetRequiredVariants(conn, target.Id);
                Assert.AreEqual(1, required.Count);
                Assert.AreEqual(primary.Id, required[0].Id);

                var all = LearningTargetRepository.GetAnswerVariants(conn, target.Id);
                Assert.AreEqual(2, all.Count);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task TargetAnswerVariants_DuplicateNormalizedTextForSameTarget_IsRejected()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);
                var target = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateTranslation("de", "en"), now);

                LearningTargetRepository.AddAnswerVariant(conn, target.Id, new TargetAnswerVariantDraft(
                    AnswerLanguage: "en",
                    DisplayText: "house",
                    Requirement: AnswerVariantRequirement.Required,
                    IsPreferred: true), now);

                // Inserting duplicate normalized text for same target fails SQLite constraint
                Assert.ThrowsExactly<SQLiteException>(() =>
                    LearningTargetRepository.AddAnswerVariant(conn, target.Id, new TargetAnswerVariantDraft(
                        AnswerLanguage: "en",
                        DisplayText: " house ",
                        Requirement: AnswerVariantRequirement.AcceptedOnly,
                        IsPreferred: false), now));
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task TargetAnswerVariants_SameNormalizedTextAcrossDifferentTargets_IsAllowed()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);
                var target1 = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateTranslation("de", "en"), now);
                var target2 = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateDefinition("de", "en"), now);

                var v1 = LearningTargetRepository.AddAnswerVariant(conn, target1.Id, new TargetAnswerVariantDraft(
                    AnswerLanguage: "en",
                    DisplayText: "house",
                    Requirement: AnswerVariantRequirement.Required,
                    IsPreferred: true), now);

                var v2 = LearningTargetRepository.AddAnswerVariant(conn, target2.Id, new TargetAnswerVariantDraft(
                    AnswerLanguage: "en",
                    DisplayText: "house",
                    Requirement: AnswerVariantRequirement.Required,
                    IsPreferred: true), now);

                Assert.AreNotEqual(v1.Id, v2.Id);
                Assert.AreEqual("house", v1.NormalizedText);
                Assert.AreEqual("house", v2.NormalizedText);
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task TargetAnswerVariants_SinglePreferredAnswerPerTarget_IsDatabaseEnforced()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);
                var target = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateTranslation("de", "en"), now);

                LearningTargetRepository.AddAnswerVariant(conn, target.Id, new TargetAnswerVariantDraft(
                    AnswerLanguage: "en",
                    DisplayText: "house",
                    Requirement: AnswerVariantRequirement.Required,
                    IsPreferred: true), now);

                // Attempting a second preferred variant violates the partial unique index
                Assert.ThrowsExactly<SQLiteException>(() =>
                    LearningTargetRepository.AddAnswerVariant(conn, target.Id, new TargetAnswerVariantDraft(
                        AnswerLanguage: "en",
                        DisplayText: "home",
                        Requirement: AnswerVariantRequirement.Required,
                        IsPreferred: true), now));
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    // ========================================================================
    // E. Transactions / rollback
    // ========================================================================

    [TestMethod]
    public async Task Transactions_FailedAtomicTargetCreation_RollsBackCompletely()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);

                // Two preferred variants violates collection rule before insert
                var invalidVariants = new List<TargetAnswerVariantDraft>
                {
                    new("en", "house", AnswerVariantRequirement.Required, IsPreferred: true),
                    new("en", "home", AnswerVariantRequirement.Required, IsPreferred: true)
                };

                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    LearningTargetRepository.CreateTargetWithVariants(
                        conn, senseId, LearningTarget.CreateTranslation("de", "en"), invalidVariants, now));

                var targetCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningTargets");
                var variantCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetAnswerVariants");
                Assert.AreEqual(0, targetCount, "No target rows should survive rollback.");
                Assert.AreEqual(0, variantCount, "No variant rows should survive rollback.");
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task Transactions_ForeignKeyViolation_FailsClosed()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                // Non-existent SenseId throws
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    LearningTargetRepository.CreateTarget(
                        conn, 99999, LearningTarget.CreateTranslation("de", "en"), now));

                // Non-existent TargetId throws
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    LearningTargetRepository.AddAnswerVariant(
                        conn, 99999, new TargetAnswerVariantDraft("en", "house", AnswerVariantRequirement.Required, true), now));
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task Transactions_DeletingSense_CascadesToTargetsAndVariants()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);
                var (target, variants) = LearningTargetRepository.CreateTargetWithVariants(
                    conn,
                    senseId,
                    LearningTarget.CreateTranslation("de", "en"),
                    [new TargetAnswerVariantDraft("en", "house", AnswerVariantRequirement.Required, true)],
                    now);

                Assert.AreEqual(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningTargets WHERE Id = ?", target.Id));
                Assert.AreEqual(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetAnswerVariants WHERE TargetId = ?", target.Id));

                conn.Execute("DELETE FROM Senses WHERE Id = ?", senseId);

                Assert.AreEqual(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningTargets WHERE Id = ?", target.Id));
                Assert.AreEqual(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetAnswerVariants WHERE TargetId = ?", target.Id));
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task Transactions_DeletingMeaning_SetsSourceMeaningIdToNull()
    {
        var (connection, path) = await CreateFreshDatabaseAsync();
        try
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            await connection.RunInTransactionAsync(conn =>
            {
                var senseId = SeedSense(conn);
                var meaningId = SeedMeaning(conn, senseId, "a dwelling");
                var target = LearningTargetRepository.CreateTarget(
                    conn, senseId, LearningTarget.CreateTranslation("de", "en"), now);

                var variant = LearningTargetRepository.AddAnswerVariant(conn, target.Id, new TargetAnswerVariantDraft(
                    AnswerLanguage: "en",
                    DisplayText: "house",
                    Requirement: AnswerVariantRequirement.Required,
                    IsPreferred: true,
                    SourceMeaningId: meaningId), now);

                Assert.AreEqual(meaningId, variant.SourceMeaningId);

                conn.Execute("DELETE FROM Meanings WHERE Id = ?", meaningId);

                var reloaded = LearningTargetRepository.GetPreferredVariant(conn, target.Id);
                Assert.IsNotNull(reloaded);
                Assert.IsNull(reloaded.SourceMeaningId, "SourceMeaningId should be SET NULL when Meaning is deleted.");
            });
        }
        finally
        {
            await CleanupAsync(connection, path);
        }
    }
}
