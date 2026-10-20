using System.Text.Json.Serialization;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;

namespace KnownFirst.Tests.LexicalEvidence;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LexicalEvidenceReportDocument))]
[JsonSerializable(typeof(LexicalEvidenceSummary))]
[JsonSerializable(typeof(LexicalCandidateEvidence))]
[JsonSerializable(typeof(LexicalLookupStepEvidence))]
[JsonSerializable(typeof(LexicalHttpAttemptEvidence))]
[JsonSerializable(typeof(LexicalFormRelationEvidence))]
[JsonSerializable(typeof(LexicalMeaningSnapshot))]
[JsonSerializable(typeof(LexicalDiagnosticEventEvidence))]
[JsonSerializable(typeof(LexicalRateMetric))]
[JsonSerializable(typeof(LexicalStatusCountEntry))]
[JsonSerializable(typeof(LexicalClassificationCountEntry))]
[JsonSerializable(typeof(LexicalFailedTermEvidence))]
[JsonSerializable(typeof(LexicalProblematicTermEvidence))]
[JsonSerializable(typeof(LexicalEvidenceExecutionMode))]
[JsonSerializable(typeof(LexicalEvidenceTransportRealism))]
[JsonSerializable(typeof(LexicalCacheObservationOutcome))]
[JsonSerializable(typeof(LexicalCacheWriteOutcome))]
[JsonSerializable(typeof(LexicalOutcomeClassification))]
[JsonSerializable(typeof(LexicalProblemKind))]
[JsonSerializable(typeof(LexicalLookupMode))]
[JsonSerializable(typeof(LexicalLookupStatus))]
[JsonSerializable(typeof(TokenKind))]
[JsonSerializable(typeof(GrammaticalRelationKind))]
[JsonSerializable(typeof(MeaningRelevanceTier))]
[JsonSerializable(typeof(IReadOnlyList<LexicalCandidateEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalLookupStepEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalHttpAttemptEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalFormRelationEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalMeaningSnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalDiagnosticEventEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalRateMetric>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalStatusCountEntry>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalClassificationCountEntry>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalFailedTermEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<LexicalProblematicTermEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(IReadOnlyList<double>))]
public sealed partial class LexicalEvidenceJsonSerializerContext : JsonSerializerContext
{
}
