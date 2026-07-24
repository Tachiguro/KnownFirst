using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PortableRecoveryTests
{
    private static readonly DateTime FixedUtc =
        new(2032, 4, 5, 6, 7, 8, DateTimeKind.Utc);

    [TestMethod]
    public async Task PortableArchive_HasExactStructureAndVerifiedChecksum()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-structure");
        await SeedDurableGraphAsync(source);

        using var archive = await CreateArchiveAsync(source);
        var validated = await BackupArchiveReader.ValidateAsync(
            archive,
            CancellationToken.None);

        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        CollectionAssert.AreEquivalent(
            new[] { "manifest.json", "data.json" },
            zip.Entries.Select(entry => entry.FullName).ToArray());
        Assert.HasCount(2, zip.Entries);
        Assert.IsTrue(validated.Manifest.DataChecksum.StartsWith("sha256:", StringComparison.Ordinal));
        Assert.AreEqual(71, validated.Manifest.DataChecksum.Length);

        var dataEntry = zip.GetEntry("data.json")!;
        using var dataStream = dataEntry.Open();
        using var data = new MemoryStream();
        await dataStream.CopyToAsync(data);
        var expected = "sha256:"
            + Convert.ToHexString(SHA256.HashData(data.ToArray())).ToLowerInvariant();
        Assert.AreEqual(expected, validated.Manifest.DataChecksum);
    }

    [TestMethod]
    public async Task PortableArchive_ExportThenImportIntoEmptyDatabase_Succeeds()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-destination");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        using var archive = await CreateArchiveAsync(source);

        var result = await CreateService(destination).ImportPortableArchiveAsync(
            archive,
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.IsTrue(await destination.ReadAsync(async connection =>
            await connection.Table<WordEntity>().CountAsync() == 1
            && await connection.Table<LearningReviewEntity>().CountAsync() == 1));
    }

    [TestMethod]
    public async Task PortableArchive_RecoveryPreservesLogicalDurableData()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-equivalence-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-equivalence-target");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        using var archive = await CreateArchiveAsync(source);

        var result = await CreateService(destination).ImportPortableArchiveAsync(
            archive,
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        var sourcePayload = await CapturePortablePayloadAsync(source);
        var targetPayload = await CapturePortablePayloadAsync(destination);
        CollectionAssert.AreEqual(
            BackupJsonCodec.SerializeData(sourcePayload),
            BackupJsonCodec.SerializeData(targetPayload));
    }

    [TestMethod]
    public async Task PortableArchive_NonEmptyDestinationIsRefusedWithoutMutation()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-refusal-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-refusal-target");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        await destination.RunInTransactionAsync(connection =>
        {
            connection.Insert(new WordEntity
            {
                Language = "de",
                CanonicalTerm = "bestehend",
                NormalizedTerm = "bestehend",
                Status = WordStatus.Known,
                TokenKind = TokenKind.Word,
                PreparationState = PreparationState.Unprepared,
                CreatedAt = FixedUtc,
                UpdatedAt = FixedUtc
            });
            return true;
        });
        var before = await CapturePortablePayloadAsync(destination);
        using var archive = await CreateArchiveAsync(source);

        var result = await CreateService(destination).ImportPortableArchiveAsync(
            archive,
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.TargetNotEmpty, result.Status);
        Assert.AreEqual(BackupErrorCodes.TargetNotEmpty, result.ErrorCode);
        var after = await CapturePortablePayloadAsync(destination);
        CollectionAssert.AreEqual(
            BackupJsonCodec.SerializeData(before),
            BackupJsonCodec.SerializeData(after));
    }

    [TestMethod]
    public async Task PortableArchive_CorruptDataIsRefusedBeforeMutation()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-corrupt-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-corrupt-target");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        using var validArchive = await CreateArchiveAsync(source);
        using var corruptArchive = await RewriteArchiveAsync(
            validArchive,
            data =>
            {
                var mutated = data.ToArray();
                mutated[^2] ^= 0x01;
                return mutated;
            });

        var result = await CreateService(destination).ImportPortableArchiveAsync(
            corruptArchive,
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.ChecksumMismatch, result.ErrorCode);
        Assert.AreEqual(0, await CountDurableRowsAsync(destination));
    }

    [TestMethod]
    public async Task PortableArchive_UnsupportedFormatIsRefusedBeforeMutation()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-version-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-version-target");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        using var validArchive = await CreateArchiveAsync(source);
        using var unsupportedArchive = await RewriteArchiveAsync(
            validArchive,
            data => data,
            manifest =>
            {
                var json = Encoding.UTF8.GetString(manifest);
                return Encoding.UTF8.GetBytes(
                    json.Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal));
            });

        var result = await CreateService(destination).ImportPortableArchiveAsync(
            unsupportedArchive,
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.UnsupportedFormat, result.ErrorCode);
        Assert.AreEqual(0, await CountDurableRowsAsync(destination));
    }

    [TestMethod]
    public async Task PortableArchive_InjectedDatabaseFailureRollsBackEveryMutation()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-rollback-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-rollback-target");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        using var archive = await CreateArchiveAsync(source);
        var service = new BackupService(
            destination,
            new FakePlatformInfo(),
            new ThrowAfterMutation(3));

        var result = await service.ImportPortableArchiveAsync(
            archive,
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.AreEqual(BackupErrorCodes.RestoreFailed, result.ErrorCode);
        Assert.AreEqual(0, await CountDurableRowsAsync(destination));
    }

    [TestMethod]
    public async Task PortableArchive_CancellationDuringImportRollsBackEveryMutation()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-cancel-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-cancel-target");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        using var archive = await CreateArchiveAsync(source);
        using var cancellation = new CancellationTokenSource();
        var service = new BackupService(
            destination,
            new FakePlatformInfo(),
            new CancelAfterMutation(3, cancellation));

        var result = await service.ImportPortableArchiveAsync(
            archive,
            cancellation.Token);

        Assert.AreEqual(PortableImportStatus.Cancelled, result.Status);
        Assert.AreEqual(BackupErrorCodes.OperationCancelled, result.ErrorCode);
        Assert.AreEqual(0, await CountDurableRowsAsync(destination));
    }

    [TestMethod]
    public async Task PortableArchive_MappingFailureDuringImportRollsBackEveryMutation()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-map-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-map-target");
        await SeedDurableGraphAsync(source);
        await destination.InitializeAsync();
        using var archive = await CreateArchiveAsync(source);
        var service = new BackupService(
            destination,
            new FakePlatformInfo(),
            new ThrowMappingFailure(3));

        var result = await service.ImportPortableArchiveAsync(
            archive,
            CancellationToken.None);

        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.AreEqual(BackupErrorCodes.InvariantViolation, result.ErrorCode);
        Assert.AreEqual(0, await CountDurableRowsAsync(destination));
    }

    [TestMethod]
    public async Task PortableArchive_ExcludesCachePreferencesAndActiveSessions()
    {
        await using var source = new TemporaryKnownFirstDatabase("portable-exclusions-source");
        await using var destination = new TemporaryKnownFirstDatabase("portable-exclusions-target");
        await SeedDurableGraphAsync(source);
        await AddExcludedStateAsync(source, "source-secret-setting", "source-cache-payload");
        await destination.InitializeAsync();
        await AddTargetExcludedStateAsync(
            destination,
            "target-setting-kept",
            "target-cache-kept");
        using var archive = await CreateArchiveAsync(source);

        var validated = await BackupArchiveReader.ValidateAsync(
            archive,
            CancellationToken.None);
        Assert.IsEmpty(validated.Payload.Workflows.VocabularyReviews);
        Assert.IsEmpty(validated.Payload.Workflows.PreparationBatches);
        Assert.HasCount(1, validated.Payload.Workflows.LearningSessions);
        Assert.IsTrue(validated.Payload.Workflows.LearningSessions.All(
            session => session.Status == BackupLearningSessionStatus.Completed));

        archive.Position = 0;
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true))
        {
            var rawData = Encoding.UTF8.GetString(
                await ReadEntryAsync(zip.GetEntry("data.json")!));
            Assert.DoesNotContain("source-secret-setting", rawData, StringComparison.Ordinal);
            Assert.DoesNotContain("source-cache-payload", rawData, StringComparison.Ordinal);
        }

        archive.Position = 0;
        var result = await CreateService(destination).ImportPortableArchiveAsync(
            archive,
            CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        var excluded = await destination.ReadAsync(async connection =>
        {
            var setting = await connection.ExecuteScalarAsync<string>(
                "SELECT SettingValue FROM AppSettings WHERE SettingKey = ?",
                "portable-test");
            var cache = await connection.Table<LexicalCacheEntity>().FirstAsync();
            return (setting, cache.ResultJson);
        });
        Assert.AreEqual("target-setting-kept", excluded.setting);
        Assert.AreEqual("target-cache-kept", excluded.ResultJson);
    }

    [TestMethod]
    public void PortableArchive_UsesSourceGeneratedJsonAndLocalizedSettingsContract()
    {
        Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.IsNotNull(BackupJsonCodec.GetGeneratedTypeInfo(typeof(BackupManifest)));
        Assert.IsNotNull(BackupJsonCodec.GetGeneratedTypeInfo(typeof(BackupPayload)));

        var root = FindRepositoryRoot();
        var importSource = File.ReadAllText(
            Path.Combine(root, "Data", "BackupImportRepository.cs"));
        StringAssert.Contains(
            importSource,
            "LexicalJsonSerializerContext.Default.LexicalResult");
        StringAssert.Contains(
            importSource,
            "LexicalJsonSerializerContext.Default.StringArray");

        var settings = File.ReadAllText(
            Path.Combine(root, "Components", "Pages", "Settings.razor"));
        StringAssert.Contains(settings, "Settings_DataExport");
        StringAssert.Contains(settings, "Settings_DataImport");
        StringAssert.Contains(settings, "Settings_PortableDataPrivacyNotice");
        StringAssert.Contains(settings, "Settings_DataImportConfirmAction");

        var english = LoadResources(
            Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var german = LoadResources(
            Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        Assert.AreEqual("Data Export", english["Settings_DataExport"]);
        Assert.AreEqual("Data Import", english["Settings_DataImport"]);
        Assert.AreEqual("Datenexport", german["Settings_DataExport"]);
        Assert.AreEqual("Datenimport", german["Settings_DataImport"]);
        StringAssert.Contains(
            english["Settings_PortableDataPrivacyNotice"],
            "not encrypted");
        StringAssert.Contains(
            german["Settings_PortableDataPrivacyNotice"],
            "nicht verschlüsselt");
    }

    private static BackupService CreateService(TemporaryKnownFirstDatabase database) =>
        new(database, new FakePlatformInfo());

    private static async Task<MemoryStream> CreateArchiveAsync(
        TemporaryKnownFirstDatabase database)
    {
        var archive = new MemoryStream();
        await CreateService(database).CreatePortableArchiveAsync(
            archive,
            CancellationToken.None);
        archive.Position = 0;
        return archive;
    }

    private static async Task<BackupPayload> CapturePortablePayloadAsync(
        TemporaryKnownFirstDatabase database) =>
        await database.ExecuteSnapshotAsync(connection =>
            BackupModelMapper.MapToExternal(
                BackupSnapshotRepository.CapturePortableSnapshot(connection)));

    private static async Task<int> CountDurableRowsAsync(
        TemporaryKnownFirstDatabase database) =>
        await database.ExecuteSnapshotAsync(connection =>
            connection.Table<DocumentEntity>().Count()
            + connection.Table<WordEntity>().Count()
            + connection.Table<WordFormEntity>().Count()
            + connection.Table<SentenceSpanEntity>().Count()
            + connection.Table<WordOccurrenceEntity>().Count()
            + connection.Table<MeaningEntity>().Count()
            + connection.Table<ReviewStateEntity>().Count()
            + connection.Table<ReviewSessionEntity>().Count()
            + connection.Table<ReviewCandidateEntity>().Count()
            + connection.Table<PreparationSessionEntity>().Count()
            + connection.Table<PreparationCandidateEntity>().Count()
            + connection.Table<ContextSnapshotEntity>().Count()
            + connection.Table<LearningCardEntity>().Count()
            + connection.Table<LearningReviewEntity>().Count()
            + connection.Table<LearningSessionEntity>().Count()
            + connection.Table<LearningSessionCardEntity>().Count());

    private static async Task SeedDurableGraphAsync(TemporaryKnownFirstDatabase database)
    {
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            const string text = "The house.";
            var contentHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            var document = new DocumentEntity
            {
                Title = "Personal text",
                TextLanguage = "en",
                ExplanationLanguage = "de",
                LookupMode = LexicalLookupMode.DefinitionAndTranslation,
                TargetLanguage = "de",
                Content = text,
                ContentFingerprint = contentHash,
                ImportedAt = FixedUtc,
                WordCount = 1
            };
            connection.Insert(document);
            var sentence = new SentenceSpanEntity
            {
                DocumentId = document.Id,
                StartPosition = 0,
                Length = text.Length,
                Order = 0
            };
            connection.Insert(sentence);
            var word = new WordEntity
            {
                Language = "en",
                CanonicalTerm = "house",
                NormalizedTerm = "house",
                Status = WordStatus.Learning,
                TokenKind = TokenKind.Word,
                PreparationState = PreparationState.Prepared,
                TotalOccurrenceCount = 1,
                DocumentCount = 1,
                AutomaticInteractionMode = LearningInteractionMode.Typing,
                ConsecutiveRecallSuccessCount = 2,
                ConsecutiveTypingSuccessCount = 1,
                ConsecutiveTypingFailureCount = 0,
                MasteryReviewExtensionScheduled = true,
                CreatedAt = FixedUtc,
                UpdatedAt = FixedUtc.AddMinutes(1)
            };
            connection.Insert(word);
            connection.Insert(new WordFormEntity
            {
                WordId = word.Id,
                SurfaceForm = "house",
                OccurrenceCount = 1
            });
            connection.Insert(new WordOccurrenceEntity
            {
                WordId = word.Id,
                DocumentId = document.Id,
                SentenceSpanId = sentence.Id,
                StartPosition = 4,
                Length = 5,
                SurfaceForm = "house",
                Order = 0,
                TechnicalFamily = TechnicalTokenFamily.None
            });
            connection.Insert(new ReviewStateEntity
            {
                WordId = word.Id,
                ReviewCount = 2,
                ForgotCount = 0,
                PartialCount = 1,
                KnownCount = 1,
                LastReviewedAt = FixedUtc.AddDays(-1)
            });
            var meaning = new MeaningEntity
            {
                WordId = word.Id,
                ExplanationLanguage = "de",
                SourceLanguage = "en",
                DisplayTerm = "house",
                TokenKind = TokenKind.Word,
                SelectedMeaningId = "meaning-1",
                Translation = "Haus",
                Definition = "A building used as a home.",
                DictionaryExample = "This is my house.",
                AdditionalNote = "Personal note",
                AcceptedAliasesJson = "[\"home\"]",
                TranslationOrDefinition = "Haus",
                Source = "Wiktionary",
                SourceProject = "en.wiktionary.org",
                SourcePageTitle = "house",
                SourceRevisionId = 123,
                Attribution = "CC BY-SA",
                ConfirmedByUser = true,
                CreatedAt = FixedUtc,
                UpdatedAt = FixedUtc,
                PreparedAt = FixedUtc
            };
            connection.Insert(meaning);
            connection.Insert(new ContextSnapshotEntity
            {
                MeaningId = meaning.Id,
                WordId = word.Id,
                SourceDocumentId = document.Id,
                SourceDocumentTitle = document.Title,
                Text = text,
                TargetStart = 4,
                TargetLength = 5,
                NormalizedFingerprint = contentHash,
                CreatedAtUtc = FixedUtc
            });
            var card = new LearningCardEntity
            {
                WordId = word.Id,
                MeaningId = meaning.Id,
                Direction = CardDirection.TermToMeaning,
                State = CardState.Review,
                DueAtUtc = FixedUtc.AddDays(2),
                IntervalDays = 2,
                EaseFactor = 2.5,
                SuccessfulReviewCount = 1,
                LapseCount = 0,
                LastReviewedAtUtc = FixedUtc,
                LastRating = ReviewRating.Good,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            };
            connection.Insert(card);
            var learningSession = new LearningSessionEntity
            {
                Status = LearningSessionStatus.Completed,
                TotalCards = 1,
                CompletedCards = 1,
                GoodCount = 1,
                StartedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
                CompletedAtUtc = FixedUtc
            };
            connection.Insert(learningSession);
            connection.Insert(new LearningSessionCardEntity
            {
                SessionId = learningSession.Id,
                CardId = card.Id,
                QueueOrder = 0,
                IsDueCard = true,
                AnswerRevealed = true,
                IsCompleted = true,
                Rating = ReviewRating.Good,
                CompletedAtUtc = FixedUtc
            });
            connection.Insert(new LearningReviewEntity
            {
                CardId = card.Id,
                SessionId = learningSession.Id,
                Rating = ReviewRating.Good,
                WasTypedAnswer = false,
                WasCorrect = true,
                ReviewedAtUtc = FixedUtc,
                DueAtUtc = FixedUtc.AddDays(2),
                IntervalDays = 2,
                EaseFactor = 2.5
            });
            return true;
        });
    }

    private static async Task AddExcludedStateAsync(
        TemporaryKnownFirstDatabase database,
        string settingValue,
        string cacheValue)
    {
        await database.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "CREATE TABLE IF NOT EXISTS AppSettings (SettingKey TEXT PRIMARY KEY, SettingValue TEXT)");
            connection.Execute(
                "INSERT INTO AppSettings (SettingKey, SettingValue) VALUES (?, ?)",
                "portable-test",
                settingValue);
            connection.Insert(new LexicalCacheEntity
            {
                CacheKey = "v2|portable-test",
                SourceLanguage = "en",
                ExplanationLanguage = "de",
                NormalizedLemma = "house",
                LookupMode = LexicalLookupMode.DefinitionAndTranslation,
                TargetLanguage = "de",
                CanonicalLookupTerm = "house",
                TokenKind = TokenKind.Word,
                Provider = "test",
                ProviderSchemaVersion = 1,
                ResultJson = cacheValue,
                FetchedAtUtc = FixedUtc
            });
            var documentId = connection.Table<DocumentEntity>().First().Id;
            var wordId = connection.Table<WordEntity>().First().Id;
            var cardId = connection.Table<LearningCardEntity>().First().Id;

            var review = new ReviewSessionEntity
            {
                DocumentId = documentId,
                Status = ReviewSessionStatus.Active,
                TotalCandidates = 1,
                StartedAt = FixedUtc
            };
            connection.Insert(review);
            connection.Insert(new ReviewCandidateEntity
            {
                SessionId = review.Id,
                WordId = wordId,
                Order = 0,
                Status = WordStatus.Unreviewed,
                PreviousWordStatus = WordStatus.Unreviewed,
                PreviousUpdatedAt = FixedUtc
            });
            var preparation = new PreparationSessionEntity
            {
                Status = PreparationSessionStatus.Active,
                Method = PreparationMethod.Manual,
                TotalItems = 1,
                StartedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            };
            connection.Insert(preparation);
            connection.Insert(new PreparationCandidateEntity
            {
                SessionId = preparation.Id,
                WordId = wordId,
                Order = 0,
                Status = PreparationCandidateStatus.Pending,
                UpdatedAtUtc = FixedUtc
            });
            var learning = new LearningSessionEntity
            {
                Status = LearningSessionStatus.Active,
                TotalCards = 1,
                StartedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            };
            connection.Insert(learning);
            connection.Insert(new LearningSessionCardEntity
            {
                SessionId = learning.Id,
                CardId = cardId,
                QueueOrder = 0,
                IsDueCard = true
            });
            return true;
        });
    }

    private static async Task AddTargetExcludedStateAsync(
        TemporaryKnownFirstDatabase database,
        string settingValue,
        string cacheValue)
    {
        await database.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "CREATE TABLE IF NOT EXISTS AppSettings (SettingKey TEXT PRIMARY KEY, SettingValue TEXT)");
            connection.Execute(
                "INSERT INTO AppSettings (SettingKey, SettingValue) VALUES (?, ?)",
                "portable-test",
                settingValue);
            connection.Insert(new LexicalCacheEntity
            {
                CacheKey = "v2|portable-target",
                SourceLanguage = "en",
                ExplanationLanguage = "de",
                NormalizedLemma = "house",
                LookupMode = LexicalLookupMode.DefinitionAndTranslation,
                TargetLanguage = "de",
                CanonicalLookupTerm = "house",
                TokenKind = TokenKind.Word,
                Provider = "test",
                ProviderSchemaVersion = 1,
                ResultJson = cacheValue,
                FetchedAtUtc = FixedUtc
            });
            return true;
        });
    }

    private static async Task<MemoryStream> RewriteArchiveAsync(
        MemoryStream source,
        Func<byte[], byte[]> rewriteData,
        Func<byte[], byte[]>? rewriteManifest = null)
    {
        source.Position = 0;
        byte[] manifest;
        byte[] data;
        using (var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
        {
            manifest = await ReadEntryAsync(zip.GetEntry("manifest.json")!);
            data = await ReadEntryAsync(zip.GetEntry("data.json")!);
        }

        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(
                zip.CreateEntry("manifest.json", CompressionLevel.Optimal),
                rewriteManifest?.Invoke(manifest) ?? manifest);
            await WriteEntryAsync(
                zip.CreateEntry("data.json", CompressionLevel.Optimal),
                rewriteData(data));
        }

        output.Position = 0;
        return output;
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream();
        await input.CopyToAsync(output);
        return output.ToArray();
    }

    private static async Task WriteEntryAsync(ZipArchiveEntry entry, byte[] bytes)
    {
        using var output = entry.Open();
        await output.WriteAsync(bytes);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KnownFirst.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("KnownFirst repository root was not found.");
    }

    private static Dictionary<string, string> LoadResources(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")!.Value,
                StringComparer.Ordinal);

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;

        public string SourceAppVersion => "1.0.0-test";
    }

    private sealed class ThrowAfterMutation(int mutationNumber) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
            if (mutationCount == mutationNumber)
            {
                throw new InvalidOperationException("Injected import failure.");
            }
        }
    }

    private sealed class CancelAfterMutation(
        int mutationNumber,
        CancellationTokenSource cancellation) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
            if (mutationCount == mutationNumber)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class ThrowMappingFailure(int mutationNumber) : IBackupImportFailureInjector
    {
        public void AfterMutation(int mutationCount)
        {
            if (mutationCount == mutationNumber)
            {
                throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
            }
        }
    }
}
