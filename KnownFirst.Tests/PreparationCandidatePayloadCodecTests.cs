using System.Text;
using System.Text.Json;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3: the discriminated <c>PreparationCandidates.ResultJson</c> codec. Covers every
/// case of <see cref="PreparationCandidatePayloadKind"/>, the exact <c>payloadVersion</c> discriminator
/// contract, and the Write-side validation/sort/limit rules.
/// </summary>
[TestClass]
public sealed class PreparationCandidatePayloadCodecTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Read_NullOrWhitespace_ReturnsEmpty(string? resultJson)
    {
        var result = PreparationCandidatePayloadCodec.Read(resultJson);

        Assert.AreEqual(PreparationCandidatePayloadKind.Empty, result.Kind);
        Assert.IsNull(result.AnyResult);
    }

    [TestMethod]
    public void Read_RawLexicalResultWithNoDiscriminator_ReturnsLegacyLexicalResult()
    {
        var lexicalResult = CreateLexicalResult();
        var json = JsonSerializer.Serialize(lexicalResult, LexicalJsonSerializerContext.Default.LexicalResult);

        var result = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.LegacyLexicalResult, result.Kind);
        Assert.AreEqual(lexicalResult.DisplayTerm, result.LegacyResult!.DisplayTerm);
        Assert.AreEqual(lexicalResult.Meanings.Count, result.LegacyResult.Meanings.Count);
        Assert.AreSame(result.LegacyResult, result.AnyResult);
    }

    [TestMethod]
    public void Read_InvalidJsonWithNoDiscriminator_ReturnsMalformed()
    {
        var result = PreparationCandidatePayloadCodec.Read("{ this is not valid json");

        Assert.AreEqual(PreparationCandidatePayloadKind.Malformed, result.Kind);
        Assert.IsNotNull(result.FailureDetail);
    }

    [TestMethod]
    public void Read_ValidEnvelopeV1_ReturnsEnvelope()
    {
        var lexicalResult = CreateLexicalResult(meaningCount: 3);
        var payload = PreparationCandidatePayloadV1.Create(
            lexicalResult,
            resolvedProviderMeaningIndexes: [0, 2],
            frozenEvidence: [new PreparationCandidateEvidence(1, "fingerprint-a", 0, 4)]);
        var json = PreparationCandidatePayloadCodec.Write(payload);

        var result = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, result.Kind);
        Assert.AreEqual(1, result.Envelope!.PayloadVersion);
        CollectionAssert.AreEqual(new[] { 0, 2 }, result.Envelope.ResolvedProviderMeaningIndexes.ToArray());
        Assert.AreEqual(1, result.Envelope.FrozenEvidence.Count);
        Assert.AreEqual("fingerprint-a", result.Envelope.FrozenEvidence[0].NormalizedFingerprint);
        Assert.AreSame(result.Envelope.Result, result.AnyResult);
    }

    [TestMethod]
    public void Read_DuplicatePayloadVersionProperty_ReturnsMalformed()
    {
        var json = """{"payloadVersion":1,"payloadVersion":1,"Result":null,"ResolvedProviderMeaningIndexes":[],"FrozenEvidence":[]}""";

        var result = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.Malformed, result.Kind);
        StringAssert.Contains(result.FailureDetail, "Duplicate");
    }

    [TestMethod]
    [DataRow("""{"payloadVersion":"1","Result":null,"ResolvedProviderMeaningIndexes":[],"FrozenEvidence":[]}""")]
    [DataRow("""{"payloadVersion":1.5,"Result":null,"ResolvedProviderMeaningIndexes":[],"FrozenEvidence":[]}""")]
    [DataRow("""{"payloadVersion":true,"Result":null,"ResolvedProviderMeaningIndexes":[],"FrozenEvidence":[]}""")]
    [DataRow("""{"payloadVersion":null,"Result":null,"ResolvedProviderMeaningIndexes":[],"FrozenEvidence":[]}""")]
    public void Read_NonIntegerPayloadVersion_ReturnsMalformed(string json)
    {
        var result = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.Malformed, result.Kind);
    }

    [TestMethod]
    public void Read_UnsupportedVersion_ReturnsUnsupportedAndNeverFallsBackToLegacy()
    {
        var lexicalResult = CreateLexicalResult();
        var legacyBody = JsonSerializer.Serialize(lexicalResult, LexicalJsonSerializerContext.Default.LexicalResult);
        // A future/unknown envelope version, wrapping an otherwise-legacy-shaped body under "Result" —
        // must never be reinterpreted as a raw LexicalResult.
        var json = $$"""{"payloadVersion":2,"Result":{{legacyBody}},"ResolvedProviderMeaningIndexes":[],"FrozenEvidence":[]}""";

        var result = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.UnsupportedEnvelopeVersion, result.Kind);
        Assert.AreEqual(2, result.UnsupportedVersion);
        Assert.IsNull(result.LegacyResult);
        Assert.IsNull(result.Envelope);
    }

    [TestMethod]
    public void Read_NonCanonicalCasedDiscriminator_IsTreatedAsLegacyNotEnvelope()
    {
        var lexicalResult = CreateLexicalResult();
        var rawJson = JsonSerializer.Serialize(lexicalResult, LexicalJsonSerializerContext.Default.LexicalResult);
        // Insert a differently-cased "PayloadVersion" property — never the canonical discriminator.
        var json = rawJson.Insert(1, "\"PayloadVersion\":1,");

        var result = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.LegacyLexicalResult, result.Kind);
        Assert.AreEqual(lexicalResult.DisplayTerm, result.LegacyResult!.DisplayTerm);
    }

    [TestMethod]
    public void Read_OversizedLegacyLexicalResult_IsNotRejectedForSize()
    {
        var lexicalResult = CreateLexicalResult(definitionPadding: 300_000);
        var json = JsonSerializer.Serialize(lexicalResult, LexicalJsonSerializerContext.Default.LexicalResult);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(json) > PreparationCandidatePayloadCodec.MaxEnvelopeBytes);

        var result = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.LegacyLexicalResult, result.Kind);
    }

    [TestMethod]
    public void Read_OversizedEnvelope_ReturnsMalformed()
    {
        var lexicalResult = CreateLexicalResult(definitionPadding: 300_000);
        var payload = PreparationCandidatePayloadV1.Create(lexicalResult);
        // Bypass Write()'s own limit enforcement to exercise Read()'s independent size check.
        var oversized = JsonSerializer.Serialize(
            payload, PreparationCandidatePayloadJsonSerializerContext.Default.PreparationCandidatePayloadV1);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(oversized) > PreparationCandidatePayloadCodec.MaxEnvelopeBytes);

        var result = PreparationCandidatePayloadCodec.Read(oversized);

        Assert.AreEqual(PreparationCandidatePayloadKind.Malformed, result.Kind);
    }

    [TestMethod]
    public void Write_OversizedEnvelope_Throws()
    {
        var lexicalResult = CreateLexicalResult(definitionPadding: 300_000);
        var payload = PreparationCandidatePayloadV1.Create(lexicalResult);

        var exception = Assert.ThrowsExactly<PreparationPayloadException>(() => PreparationCandidatePayloadCodec.Write(payload));
        Assert.AreEqual("envelope-too-large", exception.ErrorCode);
    }

    [TestMethod]
    public void Write_DuplicateResolvedIndexes_Throws()
    {
        var lexicalResult = CreateLexicalResult(meaningCount: 3);
        var payload = PreparationCandidatePayloadV1.Create(lexicalResult, resolvedProviderMeaningIndexes: [1, 1]);

        var exception = Assert.ThrowsExactly<PreparationPayloadException>(() => PreparationCandidatePayloadCodec.Write(payload));
        Assert.AreEqual("duplicate-resolved-index", exception.ErrorCode);
    }

    [TestMethod]
    public void Write_UnsortedResolvedIndexes_AreSortedAscendingInOutput()
    {
        var lexicalResult = CreateLexicalResult(meaningCount: 4);
        var payload = PreparationCandidatePayloadV1.Create(lexicalResult, resolvedProviderMeaningIndexes: [3, 0, 2]);

        var json = PreparationCandidatePayloadCodec.Write(payload);
        var readBack = PreparationCandidatePayloadCodec.Read(json);

        CollectionAssert.AreEqual(new[] { 0, 2, 3 }, readBack.Envelope!.ResolvedProviderMeaningIndexes.ToArray());
    }

    [TestMethod]
    public void Write_OutOfRangeResolvedIndex_Throws()
    {
        var lexicalResult = CreateLexicalResult(meaningCount: 1);
        var payload = PreparationCandidatePayloadV1.Create(lexicalResult, resolvedProviderMeaningIndexes: [5]);

        var exception = Assert.ThrowsExactly<PreparationPayloadException>(() => PreparationCandidatePayloadCodec.Write(payload));
        Assert.AreEqual("resolved-index-out-of-range", exception.ErrorCode);
    }

    [TestMethod]
    public void Write_InvalidEvidence_Throws()
    {
        var lexicalResult = CreateLexicalResult();
        var payload = PreparationCandidatePayloadV1.Create(
            lexicalResult,
            frozenEvidence: [new PreparationCandidateEvidence(1, "fp", 0, 0)]);

        var exception = Assert.ThrowsExactly<PreparationPayloadException>(() => PreparationCandidatePayloadCodec.Write(payload));
        Assert.AreEqual("invalid-evidence", exception.ErrorCode);
    }

    [TestMethod]
    public void Write_NullResultWithNoResolvedIndexes_WritesAndReadsBackAsPendingEnvelope()
    {
        var payload = PreparationCandidatePayloadV1.CreatePending(
            [new PreparationCandidateEvidence(1, "fp", 0, 4)]);

        var json = PreparationCandidatePayloadCodec.Write(payload);
        var readBack = PreparationCandidatePayloadCodec.Read(json);

        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, readBack.Kind);
        Assert.IsNull(readBack.Envelope!.Result);
        Assert.IsNull(readBack.AnyResult);
        Assert.AreEqual(1, readBack.Envelope.FrozenEvidence.Count);
    }

    [TestMethod]
    public void Write_NullResultWithResolvedIndexes_Throws()
    {
        var payload = new PreparationCandidatePayloadV1(1, null, [0], []);

        var exception = Assert.ThrowsExactly<PreparationPayloadException>(() => PreparationCandidatePayloadCodec.Write(payload));
        Assert.AreEqual("resolved-index-without-result", exception.ErrorCode);
    }

    [TestMethod]
    public void Write_WrongPayloadVersion_Throws()
    {
        var lexicalResult = CreateLexicalResult();
        var payload = new PreparationCandidatePayloadV1(2, lexicalResult, [], []);

        var exception = Assert.ThrowsExactly<PreparationPayloadException>(() => PreparationCandidatePayloadCodec.Write(payload));
        Assert.AreEqual("invalid-payload-version", exception.ErrorCode);
    }

    private static LexicalResult CreateLexicalResult(int meaningCount = 1, int definitionPadding = 0)
    {
        var meanings = Enumerable.Range(0, meaningCount)
            .Select(index => new LexicalMeaning(
                $"sense-{index}",
                "noun",
                index == 0 && definitionPadding > 0
                    ? $"Definition {index}".PadRight(definitionPadding, 'a')
                    : $"Definition {index}",
                $"Translation {index}",
                null,
                []))
            .ToList();

        return new LexicalResult(
            LexicalLookupStatus.Success,
            "queried-lemma",
            "DisplayTerm",
            TokenKind.Word,
            "en",
            "de",
            AcronymExpansion: null,
            meanings,
            "Wiktionary",
            "en.wiktionary.org",
            "Term",
            RevisionId: 42,
            Attribution: "Wiktionary contributors",
            LookupAtUtc: new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc));
    }
}
