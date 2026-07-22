using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;

namespace KnownFirst.Tests;

[TestClass]
public sealed class BackupJsonContractTests
{
    [TestMethod]
    public void Manifest_RoundTripsWithExactContractNamesAndUtcTimestamp()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var manifest = BackupTestData.CreateManifest(payload);

        var bytes = BackupJsonCodec.SerializeManifest(manifest);
        var json = Encoding.UTF8.GetString(bytes);
        var actual = BackupJsonCodec.DeserializeManifest(bytes);

        Assert.AreEqual(manifest.FormatVersion, actual.FormatVersion);
        Assert.AreEqual(manifest.SourceAppVersion, actual.SourceAppVersion);
        Assert.AreEqual(manifest.SourceDatabaseSchemaVersion, actual.SourceDatabaseSchemaVersion);
        Assert.AreEqual(manifest.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.AreEqual(manifest.SourcePlatform, actual.SourcePlatform);
        Assert.AreEqual(manifest.RecordCounts, actual.RecordCounts);
        Assert.AreEqual(manifest.DataChecksum, actual.DataChecksum);
        CollectionAssert.AreEqual(manifest.OptionalFeatures.ToArray(), actual.OptionalFeatures.ToArray());
        CollectionAssert.AreEqual(manifest.RequiredFeatures.ToArray(), actual.RequiredFeatures.ToArray());
        StringAssert.Contains(json, "\"formatVersion\":1");
        StringAssert.Contains(json, "\"sourcePlatform\":\"windows\"");
        StringAssert.Contains(json, "\"createdAtUtc\":\"2030-01-02T03:04:05.6780000Z\"");
        CollectionAssert.AreEqual(bytes, BackupJsonCodec.SerializeManifest(actual));
    }

    [TestMethod]
    public void MaximumDataGraph_RoundTripsSemanticallyAndDeterministically()
    {
        var payload = BackupTestData.CreateMaximumPayload();

        var bytes = BackupJsonCodec.SerializeData(payload);
        var actual = BackupJsonCodec.DeserializeData(bytes);
        var secondBytes = BackupJsonCodec.SerializeData(actual);

        CollectionAssert.AreEqual(bytes, secondBytes);
        Assert.AreEqual(payload.SourceMaterials[0].OriginalText, actual.SourceMaterials[0].OriginalText);
        Assert.AreEqual(payload.PreparedLearning[0].AdditionalNote, actual.PreparedLearning[0].AdditionalNote);
        Assert.AreEqual(
            payload.Workflows.PreparationBatches[0].Items[0].LookupDraft!.Meanings[0].Definition,
            actual.Workflows.PreparationBatches[0].Items[0].LookupDraft!.Meanings[0].Definition);
        Assert.AreEqual(
            payload.Extensions.Features["synthetic-feature"].Json,
            actual.Extensions.Features["synthetic-feature"].Json);
    }

    [TestMethod]
    public void EmptyCollections_RoundTripAsArraysAndEmptyExtensionsObject()
    {
        var payload = BackupTestData.CreateEmptyPayload();

        var bytes = BackupJsonCodec.SerializeData(payload);
        var json = Encoding.UTF8.GetString(bytes);
        var actual = BackupJsonCodec.DeserializeData(bytes);

        Assert.IsEmpty(actual.SourceMaterials);
        Assert.IsEmpty(actual.Vocabulary);
        Assert.IsEmpty(actual.PreparedLearning);
        Assert.IsEmpty(actual.Extensions.Features);
        StringAssert.Contains(json, "\"sourceMaterials\":[]");
        StringAssert.Contains(json, "\"extensions\":{}");
    }

    [TestMethod]
    public void Utf8Output_HasNoBomAndPreservesUnicodeAndLineEndings()
    {
        var payload = BackupTestData.CreateMaximumPayload();

        var bytes = BackupJsonCodec.SerializeData(payload);
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));

        var actual = BackupJsonCodec.DeserializeData(bytes);
        Assert.AreEqual("Über Security schützt café.\r\nΣυνέχεια\n", actual.SourceMaterials[0].OriginalText);
        Assert.AreEqual("Notiz mit Umlaut äöü.", actual.PreparedLearning[0].AdditionalNote);
    }

    [TestMethod]
    public void OptionalNulls_RoundTripWhileRequiredNullIsRejected()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var prepared = payload.PreparedLearning[0] with
        {
            EncounteredSurfaceForm = null,
            GrammaticalRelationship = null,
            ProviderMeaningId = null,
            AcronymExpansion = null,
            Translation = null,
            DictionaryExample = null,
            AdditionalNote = null,
            LegacyAnswerText = null,
            Definition = "usable answer"
        };
        var valid = payload with { PreparedLearning = [prepared] };

        var actual = BackupJsonCodec.DeserializeData(BackupJsonCodec.SerializeData(valid));
        Assert.IsNull(actual.PreparedLearning[0].AdditionalNote);

        var manifestJson = Encoding.UTF8.GetString(
            BackupJsonCodec.SerializeManifest(BackupTestData.CreateManifest(payload)));
        var invalid = manifestJson.Replace(
            "\"sourceAppVersion\":\"1.0.0-beta.8\"",
            "\"sourceAppVersion\":null",
            StringComparison.Ordinal);
        AssertBackupError(
            BackupErrorCodes.ManifestInvalid,
            () => BackupJsonCodec.DeserializeManifest(Encoding.UTF8.GetBytes(invalid)));
    }

    [TestMethod]
    public void Enums_AreLowercaseKebabCaseAndNumericOrUnknownValuesAreRejected()
    {
        var payload = BackupTestData.CreateMaximumPayload();
        var json = Encoding.UTF8.GetString(BackupJsonCodec.SerializeData(payload));
        StringAssert.Contains(json, "\"direction\":\"meaning-to-term\"");
        StringAssert.Contains(json, "\"status\":\"result-ready\"");
        StringAssert.Contains(json, "\"kind\":\"plural\"");

        var numeric = json.Replace(
            "\"direction\":\"meaning-to-term\"",
            "\"direction\":1",
            StringComparison.Ordinal);
        AssertBackupError(
            BackupErrorCodes.UnknownEnum,
            () => BackupJsonCodec.DeserializeData(Encoding.UTF8.GetBytes(numeric)));

        var unknown = json.Replace(
            "\"direction\":\"meaning-to-term\"",
            "\"direction\":\"MeaningToTerm\"",
            StringComparison.Ordinal);
        AssertBackupError(
            BackupErrorCodes.UnknownEnum,
            () => BackupJsonCodec.DeserializeData(Encoding.UTF8.GetBytes(unknown)));
    }

    [TestMethod]
    public void MissingRequiredAndUnknownCoreProperties_AreRejected()
    {
        var manifest = BackupTestData.CreateManifest(BackupTestData.CreateEmptyPayload());
        var json = Encoding.UTF8.GetString(BackupJsonCodec.SerializeManifest(manifest));
        var missing = json.Replace(
            "\"sourceAppVersion\":\"1.0.0-beta.8\",",
            string.Empty,
            StringComparison.Ordinal);
        Assert.AreNotEqual(json, missing);
        AssertBackupError(
            BackupErrorCodes.ManifestInvalid,
            () => BackupJsonCodec.DeserializeManifest(Encoding.UTF8.GetBytes(missing)));

        var unknown = json.Insert(1, "\"unknownCoreField\":true,");
        AssertBackupError(
            BackupErrorCodes.ManifestInvalid,
            () => BackupJsonCodec.DeserializeManifest(Encoding.UTF8.GetBytes(unknown)));
    }

    [TestMethod]
    public void MalformedJsonTrailingDataBomAndInvalidTimestamp_AreRejected()
    {
        AssertBackupError(
            BackupErrorCodes.DataJsonInvalid,
            () => BackupJsonCodec.DeserializeData("{"u8));

        var data = BackupJsonCodec.SerializeData(BackupTestData.CreateEmptyPayload());
        var trailing = data.Concat("{}"u8.ToArray()).ToArray();
        AssertBackupError(
            BackupErrorCodes.DataJsonInvalid,
            () => BackupJsonCodec.DeserializeData(trailing));

        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(data).ToArray();
        AssertBackupError(
            BackupErrorCodes.DataJsonInvalid,
            () => BackupJsonCodec.DeserializeData(withBom));

        var manifest = Encoding.UTF8.GetString(BackupJsonCodec.SerializeManifest(
            BackupTestData.CreateManifest(BackupTestData.CreateEmptyPayload())));
        var invalidTimestamp = manifest.Replace(
            "2030-01-02T03:04:05.6780000Z",
            "2030-01-02T03:04:05+01:00",
            StringComparison.Ordinal);
        AssertBackupError(
            BackupErrorCodes.InvalidTimestamp,
            () => BackupJsonCodec.DeserializeManifest(Encoding.UTF8.GetBytes(invalidTimestamp)));
    }

    [TestMethod]
    public void GeneratedContext_CoversEveryReachableDtoAndCollection()
    {
        foreach (var type in TraverseSerializableGraph(typeof(BackupManifest), typeof(BackupPayload)))
        {
            Assert.IsNotNull(
                BackupJsonCodec.GetGeneratedTypeInfo(type),
                $"Missing generated metadata for {type}.");
        }
    }

    [TestMethod]
    public void ProductionSerialization_HasNoReflectionFallbackOrUntypedOverloads()
    {
        Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault);

        var repositoryRoot = Directory.GetParent(Path.GetDirectoryName(GetSourceFilePath())!)!.FullName;
        var dataSafetyDirectory = Path.Combine(repositoryRoot, "Services", "DataSafety");
        var sources = Directory.GetFiles(dataSafetyDirectory, "*.cs")
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join('\n', sources);

        Assert.IsFalse(combined.Contains("DefaultJsonTypeInfoResolver", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("JsonSerializer.Serialize(object", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("JsonSerializer.Serialize(", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("JsonSerializer.Deserialize<object", StringComparison.Ordinal));
        StringAssert.Contains(combined, "SerializerContext.BackupManifest");
        StringAssert.Contains(combined, "SerializerContext.BackupPayload");
    }

    private static HashSet<Type> TraverseSerializableGraph(params Type[] roots)
    {
        var result = new HashSet<Type>();
        var pending = new Queue<Type>(roots);
        while (pending.TryDequeue(out var current))
        {
            current = Nullable.GetUnderlyingType(current) ?? current;
            if (!result.Add(current))
            {
                continue;
            }

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
            }

            if (current.Namespace == "KnownFirst.Models.Backup")
            {
                foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    pending.Enqueue(property.PropertyType);
                }
            }
        }

        result.RemoveWhere(type =>
            type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(DateTime));
        return result;
    }

    private static string GetSourceFilePath([CallerFilePath] string sourceFile = "") =>
        sourceFile;

    private static void AssertBackupError(string expectedCode, Action action)
    {
        var exception = Assert.ThrowsExactly<BackupFormatException>(action);
        Assert.AreEqual(expectedCode, exception.Code);
    }
}
