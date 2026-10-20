using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;

namespace KnownFirst.Tests.LexicalEvidence;

public enum LexicalEvidenceExecutionMode
{
    DirectEnrichmentService = 0,
    PreparationServiceLookup = 1
}

public enum LexicalEvidenceTransportRealism
{
    MeasuredOfflineHttpTransport = 0,
    SimulatedWikipediaApiClient = 1,
    SimulatedProviderBoundary = 2
}

public enum LexicalCacheObservationOutcome
{
    NotConsulted = 0,
    Hit = 1,
    Miss = 2,
    ReadFailed = 3
}

public enum LexicalCacheWriteOutcome
{
    NotAttempted = 0,
    Stored = 1,
    SkippedNotCacheable = 2,
    WriteFailed = 3
}

public enum LexicalOutcomeClassification
{
    DirectSuccess = 0,
    CacheHitSuccess = 1,
    GermanCaseFallbackSuccess = 2,
    LemmaRedirectSuccess = 3,
    WikipediaFallbackSuccess = 4,

    MissingPage = 10,
    LanguageSectionNotFound = 11,
    DefinitionNotFound = 12,
    TranslationNotFound = 13,
    TranslationModeFallbackExcluded = 14,
    NoSuitableDirectSense = 15,
    WikipediaDisambiguation = 16,
    WikipediaNoUsableContent = 17,
    OtherNotFound = 18,

    MalformedJson = 20,
    MissingParsePayload = 21,
    MissingHtml = 22,
    WikipediaRedirectAnomaly = 23,
    OtherParseFailure = 24,

    RateLimited = 30,
    TransientServerError = 31,
    TimedOut = 32,
    NetworkUnavailable = 33,
    OtherTransientFailure = 34,

    LemmaRedirectLoop = 40,
    LemmaRedirectDepthExceeded = 41,
    ProviderNotRegistered = 42,
    ProviderIdentityMismatch = 43,
    ProviderCrashOrError = 44,
    HttpClientError = 45,
    ApiPermanentError = 46,
    OtherPermanentFailure = 47
}

public enum LexicalProblemKind
{
    LookupFailed = 0,
    RecoveredAfterHttpRetry = 1,
    RecoveredViaGermanCaseFallback = 2,
    RecoveredViaLemmaRedirect = 3,
    RecoveredViaWikipediaFallback = 4,
    CacheOperationFailed = 5
}

public sealed record LexicalHttpAttemptEvidence(
    int AttemptOrder,
    int StepAttemptIndex,
    string ProviderName,
    string QueryTerm,
    string RequestUri,
    int? HttpStatusCode,
    string Outcome,
    bool IsRetry);

public sealed record LexicalFormRelationEvidence(
    GrammaticalRelationKind Kind,
    string BaseLemma,
    string Relationship);

public sealed record LexicalMeaningSnapshot(
    int OrderIndex,
    int OriginalProviderIndex,
    string MeaningId,
    string? PartOfSpeech,
    string Definition,
    string? Translation,
    string? Example,
    IReadOnlyList<string> UsageLabels,
    MeaningRelevanceTier RelevanceTier,
    string? TriggeringDemotionLabel);

public sealed record LexicalLookupStepEvidence(
    int StepOrder,
    string ProviderName,
    int ProviderSchemaVersion,
    string QueriedTerm,
    LexicalLookupMode LookupMode,
    string? TargetLanguage,
    TokenKind TokenKind,
    int RedirectDepth,
    bool IsFallbackStep,
    string CacheKey,
    LexicalCacheObservationOutcome CacheOutcome,
    LexicalCacheWriteOutcome CacheWriteOutcome,
    bool ProviderInvoked,
    bool WikipediaApiClientInvoked,
    IReadOnlyList<string> QueryCandidates,
    bool CaseFallbackTriggered,
    int HttpAttemptCount,
    int HttpRetryCount,
    IReadOnlyList<LexicalHttpAttemptEvidence> HttpAttempts,
    LexicalLookupStatus? RawProviderStatus,
    string? RawProviderErrorCode,
    LexicalLookupStatus StepStatus,
    string? StepErrorCode,
    int ExtractedDirectMeaningCount,
    IReadOnlyList<LexicalFormRelationEvidence> ExtractedFormRelations,
    IReadOnlyList<LexicalMeaningSnapshot> PreRankingMeanings);

public sealed record LexicalDiagnosticEventEvidence(
    int Order,
    string Phase,
    string NormalizedTerm,
    string SourceLanguage,
    LexicalLookupMode LookupMode,
    string? TargetLanguage,
    string Provider,
    string CacheOutcome,
    string HttpOutcome,
    string ParserOutcome,
    string? ExceptionType);

public sealed record LexicalCandidateEvidence(
    string CaseId,
    string Description,
    LexicalEvidenceExecutionMode ExecutionMode,
    LexicalEvidenceTransportRealism TransportRealism,
    string SourceLanguage,
    LexicalLookupMode LookupMode,
    string? TargetLanguage,
    string ExplanationLanguage,
    string RequestedProvider,
    string DisplayedSurfaceForm,
    string VocabularyCanonicalTerm,
    string CanonicalLookupTerm,
    TokenKind TokenKind,
    string? RepresentativeContext,
    int? PreparationLookupAttemptCount,
    int ProviderInvocationCount,
    int WikipediaApiClientInvocationCount,
    int HttpAttemptCount,
    int HttpRetryCount,
    IReadOnlyList<double> RecordedRetryDelayMilliseconds,
    int CacheReadAttemptCount,
    int CacheHitCount,
    int CacheMissCount,
    int CacheReadFailureCount,
    int CacheWriteAttemptCount,
    int CacheWriteStoredCount,
    int CacheWriteSkippedCount,
    int CacheWriteFailureCount,
    LexicalCacheObservationOutcome PrimaryCacheOutcome,
    LexicalCacheObservationOutcome EffectiveCacheOutcome,
    bool FinalResultFromCache,
    IReadOnlyList<string> OrderedProviderChain,
    IReadOnlyList<string> OrderedQueryCandidates,
    IReadOnlyList<LexicalLookupStepEvidence> LookupSteps,
    bool CaseFallbackAttempted,
    bool CaseFallbackSucceeded,
    bool LemmaRedirectAttempted,
    bool LemmaRedirectSucceeded,
    int FinalRedirectDepth,
    string? EncounteredSurfaceForm,
    string? GrammaticalRelationship,
    bool WikipediaFallbackEligible,
    bool WikipediaFallbackTriggered,
    bool WikipediaFallbackSucceeded,
    string WikipediaFallbackDisposition,
    IReadOnlyList<LexicalMeaningSnapshot> PreRankingMeanings,
    IReadOnlyList<LexicalMeaningSnapshot> RankedMeanings,
    bool MeaningOrderChangedByRanking,
    LexicalLookupStatus FinalStatus,
    string? FinalErrorCode,
    LexicalOutcomeClassification OutcomeClassification,
    string FinalProviderName,
    string FinalSourceProject,
    string FinalPageTitle,
    long? FinalRevisionId,
    string FinalQueriedLemma,
    string FinalDisplayTerm,
    string? AcronymExpansion,
    bool HasUsableData,
    bool HasReferenceData,
    bool CanRetry,
    IReadOnlyList<LexicalDiagnosticEventEvidence> DiagnosticEvents);

public sealed record LexicalRateMetric(
    string MetricCode,
    string Description,
    int Numerator,
    int Denominator,
    string DenominatorName,
    double? Percentage);

public sealed record LexicalStatusCountEntry(
    LexicalLookupStatus Status,
    int Count,
    int Denominator,
    double? Percentage);

public sealed record LexicalClassificationCountEntry(
    LexicalOutcomeClassification Classification,
    int Count,
    int Denominator,
    double? Percentage);

public sealed record LexicalFailedTermEvidence(
    string CaseId,
    string SourceLanguage,
    LexicalLookupMode LookupMode,
    string? TargetLanguage,
    string DisplayedSurfaceForm,
    string VocabularyCanonicalTerm,
    string CanonicalLookupTerm,
    string FinalQueriedLemma,
    string FinalProviderName,
    LexicalLookupStatus FinalStatus,
    string? FinalErrorCode,
    LexicalOutcomeClassification OutcomeClassification,
    IReadOnlyList<string> OrderedProviderChain,
    IReadOnlyList<string> OrderedQueryCandidates,
    int? PreparationLookupAttemptCount,
    int ProviderInvocationCount,
    int WikipediaApiClientInvocationCount,
    int HttpAttemptCount,
    int HttpRetryCount,
    bool CanRetry,
    string Explanation);

public sealed record LexicalProblematicTermEvidence(
    string CaseId,
    LexicalProblemKind ProblemKind,
    string SourceLanguage,
    LexicalLookupMode LookupMode,
    string? TargetLanguage,
    string DisplayedSurfaceForm,
    string CanonicalLookupTerm,
    string FinalQueriedLemma,
    string FinalProviderName,
    LexicalLookupStatus FinalStatus,
    string? FinalErrorCode,
    LexicalOutcomeClassification OutcomeClassification,
    IReadOnlyList<string> OrderedProviderChain,
    IReadOnlyList<string> OrderedQueryCandidates,
    int ProviderInvocationCount,
    int HttpAttemptCount,
    int HttpRetryCount,
    string Explanation);

public sealed record LexicalEvidenceSummary(
    int TotalCandidateCount,
    int GermanCandidateCount,
    int EnglishCandidateCount,
    int DefinitionModeCandidateCount,
    int TranslationModeCandidateCount,
    int DefinitionAndTranslationModeCandidateCount,
    int MeasuredHttpTransportCandidateCount,
    int SimulatedWikipediaApiClientCandidateCount,
    int SimulatedProviderBoundaryCandidateCount,
    int PreparationServiceCandidateCount,
    int DirectEnrichmentCandidateCount,
    int CacheConsultedCandidateCount,
    int CacheConsultedStepCount,
    int GermanCaseFallbackEligibleCandidateCount,
    int LemmaRedirectCandidateCount,
    int WiktionaryNotFoundCandidateCount,
    int WikipediaFallbackEligibleCandidateCount,
    int WikipediaFallbackTriggeredCandidateCount,
    int MultiMeaningSuccessCandidateCount,
    int TotalPreparationLookupAttemptCount,
    int TotalProviderInvocationCount,
    int TotalWikipediaApiClientInvocationCount,
    int TotalHttpAttemptCount,
    int TotalHttpRetryCount,
    int TotalCacheReadAttemptCount,
    int TotalCacheHitCount,
    int TotalCacheMissCount,
    int TotalCacheReadFailureCount,
    int TotalCacheWriteAttemptCount,
    int TotalCacheWriteStoredCount,
    int TotalCacheWriteSkippedCount,
    int TotalCacheWriteFailureCount,
    int SuccessCount,
    int NotFoundCount,
    int ParseFailureCount,
    int TransientFailureCount,
    int PermanentFailureCount,
    int UsableDataCount,
    int ReferenceDataCount,
    int GermanCaseFallbackAttemptedCount,
    int GermanCaseFallbackSucceededCount,
    int LemmaRedirectAttemptedCount,
    int LemmaRedirectSucceededCount,
    int WikipediaFallbackSucceededCount,
    int MeaningOrderChangedByRankingCount,
    IReadOnlyList<LexicalStatusCountEntry> StatusBreakdown,
    IReadOnlyList<LexicalClassificationCountEntry> ClassificationBreakdown,
    IReadOnlyList<LexicalRateMetric> RateMetrics,
    IReadOnlyList<LexicalFailedTermEvidence> FailedTerms,
    IReadOnlyList<LexicalProblematicTermEvidence> ProblematicTerms);

public sealed record LexicalEvidenceReportDocument(
    string SuiteId,
    string Description,
    DateTime CapturedAtUtc,
    LexicalEvidenceSummary Summary,
    IReadOnlyList<LexicalCandidateEvidence> Candidates);
