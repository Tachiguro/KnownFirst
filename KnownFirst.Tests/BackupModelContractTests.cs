using System.Reflection;
using System.Text.Json;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

[TestClass]
public sealed class BackupModelContractTests
{
    [TestMethod]
    public void MaximumShape_RepresentsCompleteV1GraphAndFixedCounts()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var manifest = BackupTestData.CreateManifest(payload);
        var preview = BackupTestData.CreatePreview(payload);

        BackupModelContract.ValidatePayload(payload);
        BackupModelContract.ValidateManifest(manifest);
        BackupModelContract.ValidateRecordCounts(manifest, payload);
        BackupModelContract.ValidatePreview(preview);

        Assert.AreEqual(1, manifest.RecordCounts.SourceMaterials);
        Assert.AreEqual(1, manifest.RecordCounts.SentenceRanges);
        Assert.AreEqual(1, manifest.RecordCounts.VocabularyItems);
        Assert.AreEqual(1, manifest.RecordCounts.EncounteredForms);
        Assert.AreEqual(1, manifest.RecordCounts.Occurrences);
        Assert.AreEqual(1, manifest.RecordCounts.PreparedItems);
        Assert.AreEqual(1, manifest.RecordCounts.ContextSnapshots);
        Assert.AreEqual(1, manifest.RecordCounts.LegacyReviewSummaries);
        Assert.AreEqual(1, manifest.RecordCounts.VocabularyReviewWorkflows);
        Assert.AreEqual(1, manifest.RecordCounts.VocabularyReviewItems);
        Assert.AreEqual(1, manifest.RecordCounts.PreparationWorkflows);
        Assert.AreEqual(1, manifest.RecordCounts.PreparationItems);
        Assert.AreEqual(1, manifest.RecordCounts.LearningCards);
        Assert.AreEqual(1, manifest.RecordCounts.LearningReviews);
        Assert.AreEqual(1, manifest.RecordCounts.LearningWorkflows);
        Assert.AreEqual(1, manifest.RecordCounts.LearningQueueItems);
    }

    [TestMethod]
    public void EmptyCollectionsAndAllowedEmptyOriginalText_AreValid()
    {
        var empty = BackupTestData.CreateEmptyPayload();
        BackupModelContract.ValidatePayload(empty);
        BackupModelContract.ValidateManifest(BackupTestData.CreateManifest(empty));

        var source = BackupTestData.CreateMaximumPayload().SourceMaterials[0] with
        {
            OriginalText = string.Empty,
            ContentSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            StoredWordCount = 0,
            Sentences = [],
            Occurrences = []
        };
        var payload = empty with { SourceMaterials = [source] };

        BackupModelContract.ValidatePayload(payload);
    }

    [TestMethod]
    public void NullableFields_AcceptNullOnlyWhereDeclared()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var prepared = payload.PreparedLearning[0] with
        {
            EncounteredSurfaceForm = null,
            GrammaticalRelationship = null,
            ProviderMeaningId = null,
            AcronymExpansion = null,
            DictionaryExample = null,
            AdditionalNote = null,
            LegacyAnswerText = null,
            Translation = null,
            Definition = "required usable answer"
        };
        var valid = payload with { PreparedLearning = [prepared] };
        BackupModelContract.ValidatePayload(valid);

        var invalidManifest = BackupTestData.CreateManifest(payload) with
        {
            SourceAppVersion = null!
        };
        AssertBackupError(
            BackupErrorCodes.InvariantViolation,
            () => BackupModelContract.ValidateManifest(invalidManifest));
    }

    [TestMethod]
    public void ExactStrings_PreserveUnicodeUmlautsAndLineEndings()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var source = payload.SourceMaterials[0];
        var prepared = payload.PreparedLearning[0];

        Assert.AreEqual("Über Security schützt café.\r\nΣυνέχεια\n", source.OriginalText);
        Assert.AreEqual("Notiz mit Umlaut äöü.", prepared.AdditionalNote);
        Assert.AreEqual(
            "Security protects data.\nSecond retained line.",
            prepared.DictionaryExample);
    }

    [TestMethod]
    public void RecordCounts_HaveExactlyTheFixedV1Properties()
    {
        var actual = typeof(BackupRecordCounts)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = new[]
        {
            "ContextSnapshots",
            "EncounteredForms",
            "LearningCards",
            "LearningQueueItems",
            "LearningReviews",
            "LearningWorkflows",
            "LegacyReviewSummaries",
            "Occurrences",
            "PreparationItems",
            "PreparationWorkflows",
            "PreparedItems",
            "SentenceRanges",
            "SourceMaterials",
            "VocabularyItems",
            "VocabularyReviewItems",
            "VocabularyReviewWorkflows"
        };

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void PersistenceEnums_RoundTripThroughExplicitExternalMappings()
    {
        AssertPersistenceRoundTrips<WordStatus, BackupKnowledgeState>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<TokenKind, BackupTokenKind>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<PreparationState, BackupPreparationState>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<LearningInteractionMode, BackupLearningInteractionMode>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<TechnicalTokenFamily, BackupTechnicalTokenFamily>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<LexicalLookupMode, BackupLexicalLookupMode>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<ReviewSessionStatus, BackupReviewSessionStatus>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<PreparationMethod, BackupPreparationMethod>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<PreparationSessionStatus, BackupPreparationSessionStatus>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<PreparationCandidateStatus, BackupPreparationCandidateStatus>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<CardDirection, BackupCardDirection>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<CardState, BackupCardState>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<ReviewRating, BackupReviewRating>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<LearningSessionStatus, BackupLearningSessionStatus>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<LexicalLookupStatus, BackupLexicalLookupStatus>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
        AssertPersistenceRoundTrips<GrammaticalRelationKind, BackupGrammaticalRelationKind>(
            BackupEnumMappings.ToBackup,
            BackupEnumMappings.ToPersistence);
    }

    [TestMethod]
    public void ExternalEnums_UseStrictLowercaseKebabCaseStrings()
    {
        AssertExternalRoundTrips(
            Enum.GetValues<BackupSourcePlatform>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseSourcePlatform);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupLexicalLookupMode>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLexicalLookupMode);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupKnowledgeState>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseKnowledgeState);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupTokenKind>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseTokenKind);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupPreparationState>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationState);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupLearningInteractionMode>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLearningInteractionMode);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupTechnicalTokenFamily>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseTechnicalTokenFamily);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupCardDirection>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseCardDirection);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupCardState>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseCardState);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupReviewRating>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseReviewRating);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupReviewSessionStatus>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseReviewSessionStatus);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupPreparationMethod>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationMethod);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupPreparationSessionStatus>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationSessionStatus);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupPreparationCandidateStatus>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationCandidateStatus);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupLearningSessionStatus>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLearningSessionStatus);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupLexicalLookupStatus>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLexicalLookupStatus);
        AssertExternalRoundTrips(
            Enum.GetValues<BackupGrammaticalRelationKind>(),
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseGrammaticalRelationKind);
    }

    [TestMethod]
    public void EnumMappings_RejectUnknownInternalExternalAndStringValues()
    {
        Action[] invalidInternalMappings =
        [
            () => BackupEnumMappings.ToBackup((WordStatus)int.MaxValue),
            () => BackupEnumMappings.ToBackup((TokenKind)int.MaxValue),
            () => BackupEnumMappings.ToBackup((PreparationState)int.MaxValue),
            () => BackupEnumMappings.ToBackup((LearningInteractionMode)int.MaxValue),
            () => BackupEnumMappings.ToBackup((TechnicalTokenFamily)int.MaxValue),
            () => BackupEnumMappings.ToBackup((LexicalLookupMode)int.MaxValue),
            () => BackupEnumMappings.ToBackup((ReviewSessionStatus)int.MaxValue),
            () => BackupEnumMappings.ToBackup((PreparationMethod)int.MaxValue),
            () => BackupEnumMappings.ToBackup((PreparationSessionStatus)int.MaxValue),
            () => BackupEnumMappings.ToBackup((PreparationCandidateStatus)int.MaxValue),
            () => BackupEnumMappings.ToBackup((CardDirection)int.MaxValue),
            () => BackupEnumMappings.ToBackup((CardState)int.MaxValue),
            () => BackupEnumMappings.ToBackup((ReviewRating)int.MaxValue),
            () => BackupEnumMappings.ToBackup((LearningSessionStatus)int.MaxValue),
            () => BackupEnumMappings.ToBackup((LexicalLookupStatus)int.MaxValue),
            () => BackupEnumMappings.ToBackup((GrammaticalRelationKind)int.MaxValue)
        ];
        foreach (var action in invalidInternalMappings)
        {
            AssertBackupError(BackupErrorCodes.UnknownEnum, action);
        }

        Action[] invalidExternalMappings =
        [
            () => BackupEnumMappings.ToPersistence((BackupKnowledgeState)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupTokenKind)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupPreparationState)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupLearningInteractionMode)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupTechnicalTokenFamily)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupLexicalLookupMode)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupReviewSessionStatus)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupPreparationMethod)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupPreparationSessionStatus)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupPreparationCandidateStatus)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupCardDirection)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupCardState)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupReviewRating)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupLearningSessionStatus)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupLexicalLookupStatus)int.MaxValue),
            () => BackupEnumMappings.ToPersistence((BackupGrammaticalRelationKind)int.MaxValue)
        ];
        foreach (var action in invalidExternalMappings)
        {
            AssertBackupError(BackupErrorCodes.UnknownEnum, action);
        }

        Action[] invalidStrings =
        [
            () => BackupEnumMappings.ParseSourcePlatform("Windows"),
            () => BackupEnumMappings.ParseLexicalLookupMode("Definition"),
            () => BackupEnumMappings.ParseKnowledgeState("0"),
            () => BackupEnumMappings.ParseTokenKind("technical_term"),
            () => BackupEnumMappings.ParsePreparationState("unknown"),
            () => BackupEnumMappings.ParseLearningInteractionMode("TYPING"),
            () => BackupEnumMappings.ParseTechnicalTokenFamily("SHA"),
            () => BackupEnumMappings.ParseCardDirection("TermToMeaning"),
            () => BackupEnumMappings.ParseCardState("unknown"),
            () => BackupEnumMappings.ParseReviewRating("1"),
            () => BackupEnumMappings.ParseReviewSessionStatus("ACTIVE"),
            () => BackupEnumMappings.ParsePreparationMethod("automatic_online"),
            () => BackupEnumMappings.ParsePreparationSessionStatus("unknown"),
            () => BackupEnumMappings.ParsePreparationCandidateStatus("unknown"),
            () => BackupEnumMappings.ParseLearningSessionStatus("unknown"),
            () => BackupEnumMappings.ParseLexicalLookupStatus("not_found"),
            () => BackupEnumMappings.ParseGrammaticalRelationKind("past_tense")
        ];
        foreach (var action in invalidStrings)
        {
            AssertBackupError(BackupErrorCodes.UnknownEnum, action);
        }
    }

    [TestMethod]
    public void ArchiveIds_RejectEmptyPathsDatabaseIntegersAndOversizeValues()
    {
        var source = BackupTestData.CreateMaximumPayload().SourceMaterials[0];
        foreach (var invalidId in new[]
                 {
                     string.Empty,
                     "42",
                     "../source",
                     "C:\\private\\source",
                     new string('a', BackupFormatLimits.MaxArchiveIdUtf8Bytes + 1)
                 })
        {
            var payload = BackupTestData.CreateEmptyPayload() with
            {
                SourceMaterials = [source with { Id = invalidId }]
            };
            AssertBackupError(
                BackupErrorCodes.InvalidArchiveId,
                () => BackupModelContract.ValidatePayload(payload));
        }
    }

    [TestMethod]
    public void InvalidNumericTimestampAndLimitValues_AreRejected()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var card = payload.Learning.Cards[0];

        var negative = payload with
        {
            Learning = payload.Learning with
            {
                Cards = [card with { IntervalDays = -1 }]
            }
        };
        AssertBackupError(
            BackupErrorCodes.InvariantViolation,
            () => BackupModelContract.ValidatePayload(negative));

        var notFinite = payload with
        {
            Learning = payload.Learning with
            {
                Cards = [card with { EaseFactor = double.NaN }]
            }
        };
        AssertBackupError(
            BackupErrorCodes.InvariantViolation,
            () => BackupModelContract.ValidatePayload(notFinite));

        var localTime = payload with
        {
            Learning = payload.Learning with
            {
                Cards = [card with { DueAtUtc = DateTime.SpecifyKind(card.DueAtUtc, DateTimeKind.Local) }]
            }
        };
        AssertBackupError(
            BackupErrorCodes.InvalidTimestamp,
            () => BackupModelContract.ValidatePayload(localTime));

        var tooManyFeatures = BackupTestData.CreateManifest(payload) with
        {
            OptionalFeatures = Enumerable.Range(0, BackupFormatLimits.MaxFeatureCount + 1)
                .Select(index => $"feature-{index:D3}")
                .ToArray()
        };
        AssertBackupError(
            BackupErrorCodes.LimitExceeded,
            () => BackupModelContract.ValidateManifest(tooManyFeatures));

        var negativeCount = BackupTestData.CreateManifest(payload) with
        {
            RecordCounts = BackupTestData.CreateManifest(payload).RecordCounts with
            {
                LearningReviews = -1
            }
        };
        AssertBackupError(
            BackupErrorCodes.LimitExceeded,
            () => BackupModelContract.ValidateManifest(negativeCount));

        var oversizedTitle = payload with
        {
            SourceMaterials =
            [
                payload.SourceMaterials[0] with
                {
                    Title = new string('x', BackupFormatLimits.MaxStringUtf8Bytes + 1)
                }
            ]
        };
        AssertBackupError(
            BackupErrorCodes.LimitExceeded,
            () => BackupModelContract.ValidatePayload(oversizedTitle));

        var tooManySources = BackupTestData.CreateEmptyPayload() with
        {
            SourceMaterials = Enumerable.Repeat(
                    payload.SourceMaterials[0],
                    BackupFormatLimits.MaxSourceMaterials + 1)
                .ToArray()
        };
        AssertBackupError(
            BackupErrorCodes.LimitExceeded,
            () => BackupModelContract.ValidatePayload(tooManySources));
    }

    [TestMethod]
    public void LookupModes_RequireStrictCompatibleTargetLanguages()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var source = payload.SourceMaterials[0];

        var definitionWithTarget = payload with
        {
            SourceMaterials =
            [
                source with
                {
                    LookupMode = BackupLexicalLookupMode.Definition,
                    TargetLanguage = "de"
                }
            ]
        };
        AssertBackupError(
            BackupErrorCodes.InvariantViolation,
            () => BackupModelContract.ValidatePayload(definitionWithTarget));

        var translationWithoutTarget = payload with
        {
            SourceMaterials =
            [
                source with
                {
                    LookupMode = BackupLexicalLookupMode.Translation,
                    TargetLanguage = null
                }
            ]
        };
        AssertBackupError(
            BackupErrorCodes.InvariantViolation,
            () => BackupModelContract.ValidatePayload(translationWithoutTarget));
    }

    [TestMethod]
    public void FormatLimits_MatchTheAcceptedV1SecurityContract()
    {
        var constants = typeof(BackupFormatLimits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => field.GetRawConstantValue());

        Assert.AreEqual(2, constants[nameof(BackupFormatLimits.RequiredZipEntryCount)]);
        Assert.AreEqual(128L * 1024 * 1024, constants[nameof(BackupFormatLimits.MaxArchiveBytes)]);
        Assert.AreEqual(256 * 1024, constants[nameof(BackupFormatLimits.MaxManifestUncompressedBytes)]);
        Assert.AreEqual(256 * 1024 * 1024, constants[nameof(BackupFormatLimits.MaxDataUncompressedBytes)]);
        Assert.AreEqual(100d, constants[nameof(BackupFormatLimits.MaxCompressionRatio)]);
        Assert.AreEqual(64, constants[nameof(BackupFormatLimits.MaxJsonDepth)]);
        Assert.AreEqual(16 * 1024 * 1024, constants[nameof(BackupFormatLimits.MaxDocumentOrContextUtf8Bytes)]);
        Assert.AreEqual(1024 * 1024, constants[nameof(BackupFormatLimits.MaxStringUtf8Bytes)]);
        Assert.AreEqual(10_000, constants[nameof(BackupFormatLimits.MaxSourceMaterials)]);
        Assert.AreEqual(250_000, constants[nameof(BackupFormatLimits.MaxVocabularyItems)]);
        Assert.AreEqual(1_000_000, constants[nameof(BackupFormatLimits.MaxOccurrences)]);
        Assert.AreEqual(1_000_000, constants[nameof(BackupFormatLimits.MaxOtherCountedRecords)]);
    }

    [TestMethod]
    public void PublicDtoGraph_HasNoEntitiesSqlitePathsJsonElementsOrMutableSetters()
    {
        var types = TraverseDtoGraph(
            typeof(BackupManifest),
            typeof(BackupPayload),
            typeof(BackupRestorePreview));
        var archiveIdNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Id",
            "VocabularyId",
            "PreparedItemId",
            "SourceMaterialId",
            "SentenceId",
            "CardId",
            "LearningSessionId"
        };

        foreach (var type in types)
        {
            Assert.IsTrue(type.IsPublic, type.FullName);
            Assert.AreNotEqual(
                true,
                type.Namespace?.StartsWith("KnownFirst.Data", StringComparison.Ordinal) == true);
            Assert.AreNotEqual(typeof(JsonElement), type);
            Assert.IsFalse(typeof(FileSystemInfo).IsAssignableFrom(type));

            foreach (var attribute in type.GetCustomAttributes(inherit: true))
            {
                Assert.AreNotEqual(
                    true,
                    attribute.GetType().Namespace?.StartsWith("SQLite", StringComparison.Ordinal) == true);
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                Assert.AreNotEqual("ResultJson", property.Name);
                Assert.IsFalse(property.Name.Contains("Path", StringComparison.Ordinal));
                Assert.IsFalse(property.Name.Contains("Device", StringComparison.Ordinal));
                Assert.IsFalse(property.Name.Contains("Email", StringComparison.Ordinal));
                Assert.IsFalse(property.Name.Contains("Account", StringComparison.Ordinal));
                Assert.AreNotEqual(typeof(JsonElement), property.PropertyType);
                if (archiveIdNames.Contains(property.Name))
                {
                    Assert.AreEqual(typeof(string), property.PropertyType, property.Name);
                }

                var setter = property.SetMethod;
                if (setter is not null)
                {
                    CollectionAssert.Contains(
                        setter.ReturnParameter.GetRequiredCustomModifiers(),
                        typeof(System.Runtime.CompilerServices.IsExternalInit),
                        $"{type.Name}.{property.Name} must be init-only.");
                }
            }
        }
    }

    [TestMethod]
    public void ErrorMessages_ContainOnlyStableCodes()
    {
        const string privatePath = "C:\\Users\\private\\document.txt";
        var source = BackupTestData.CreateMaximumPayload().SourceMaterials[0] with
        {
            Id = privatePath
        };
        var payload = BackupTestData.CreateEmptyPayload() with
        {
            SourceMaterials = [source]
        };

        var exception = Assert.ThrowsExactly<BackupFormatException>(
            () => BackupModelContract.ValidatePayload(payload));
        Assert.AreEqual(BackupErrorCodes.InvalidArchiveId, exception.Code);
        Assert.AreEqual(BackupErrorCodes.InvalidArchiveId, exception.Message);
        Assert.IsFalse(exception.Message.Contains(privatePath, StringComparison.Ordinal));
    }

    private static void AssertPersistenceRoundTrips<TPersistence, TBackup>(
        Func<TPersistence, TBackup> toBackup,
        Func<TBackup, TPersistence> toPersistence)
        where TPersistence : struct, Enum
        where TBackup : struct, Enum
    {
        foreach (var value in Enum.GetValues<TPersistence>())
        {
            Assert.AreEqual(value, toPersistence(toBackup(value)));
        }
    }

    private static void AssertExternalRoundTrips<T>(
        IEnumerable<T> values,
        Func<T, string> format,
        Func<string, T> parse)
        where T : struct, Enum
    {
        foreach (var value in values)
        {
            var external = format(value);
            Assert.AreEqual(value, parse(external));
            Assert.IsTrue(external.All(character =>
                character == '-'
                || character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9'));
        }
    }

    private static HashSet<Type> TraverseDtoGraph(params Type[] roots)
    {
        var result = new HashSet<Type>();
        var pending = new Queue<Type>(roots);
        while (pending.TryDequeue(out var current))
        {
            current = Nullable.GetUnderlyingType(current) ?? current;
            if (current.IsArray)
            {
                pending.Enqueue(current.GetElementType()!);
                continue;
            }

            if (current.IsGenericType)
            {
                foreach (var argument in current.GetGenericArguments())
                {
                    pending.Enqueue(argument);
                }

                continue;
            }

            if (current.IsPrimitive
                || current.IsEnum
                || current == typeof(string)
                || current == typeof(decimal)
                || current == typeof(DateTime)
                || !result.Add(current))
            {
                continue;
            }

            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Enqueue(property.PropertyType);
            }
        }

        return result;
    }

    private static void AssertBackupError(string expectedCode, Action action)
    {
        var exception = Assert.ThrowsExactly<BackupFormatException>(action);
        Assert.AreEqual(expectedCode, exception.Code);
    }
}
