using System.Text.Json.Serialization;
using KnownFirst.Core.Text;

namespace KnownFirst.Tests.AnalysisEvidence;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnalysisEvidenceDocument))]
[JsonSerializable(typeof(AnalysisInputEvidence))]
[JsonSerializable(typeof(AnalysisSentenceEvidence))]
[JsonSerializable(typeof(AnalysisTokenDecisionEvidence))]
[JsonSerializable(typeof(AnalysisCandidateEvidence))]
[JsonSerializable(typeof(AnalysisSurfaceFormEvidence))]
[JsonSerializable(typeof(AnalysisOccurrenceEvidence))]
[JsonSerializable(typeof(AnalysisDerivedTermEvidence))]
[JsonSerializable(typeof(AnalysisCandidateGroupEvidence))]
[JsonSerializable(typeof(AnalysisContextDecisionEvidence))]
[JsonSerializable(typeof(AnalysisInvariantFailureEvidence))]
[JsonSerializable(typeof(TokenKind))]
[JsonSerializable(typeof(CandidateProvenanceKind))]
[JsonSerializable(typeof(TechnicalTokenFamily))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisSentenceEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisTokenDecisionEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisCandidateEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisSurfaceFormEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisOccurrenceEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisDerivedTermEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisCandidateGroupEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisContextDecisionEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<AnalysisInvariantFailureEvidence>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public sealed partial class AnalysisEvidenceJsonSerializerContext : JsonSerializerContext
{
}
