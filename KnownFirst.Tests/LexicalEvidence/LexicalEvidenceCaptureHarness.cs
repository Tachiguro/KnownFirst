using System.Net;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Lexical.Wikipedia;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests.LexicalEvidence;

public sealed record LexicalEvidenceEnrichmentCaptureOptions(
    string CaseId,
    string Description,
    LexicalLookupRequest Request,
    string? OriginalDocumentContent = null,
    string? RepresentativeContext = null,
    LexicalEvidenceTransportRealism TransportRealism = LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport,
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? OfflineHttpHandler = null,
    IWikipediaApiClient? SimulatedWikipediaApiClient = null,
    IReadOnlyList<ILexicalLookupProvider>? CustomProviders = null,
    ILexicalCacheRepository? CacheRepository = null,
    DateTime? UtcNow = null);

public sealed record LexicalEvidencePreparationCaptureOptions(
    string CaseId,
    string Description,
    IKnownFirstDatabase Database,
    LexicalEvidenceTransportRealism TransportRealism = LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport,
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? OfflineHttpHandler = null,
    IWikipediaApiClient? SimulatedWikipediaApiClient = null,
    IReadOnlyList<ILexicalLookupProvider>? CustomProviders = null,
    ILexicalCacheRepository? CacheRepository = null,
    DateTime? UtcNow = null,
    int RequestedSessionLimit = 1,
    Func<PreparationService, PreparationItem, CancellationToken, Task>? PostLookupAction = null);

public sealed record PreparationLookupCorrelationResult(
    LexicalCandidateEvidence Evidence,
    PreparationItem ItemAfterLookup,
    PreparationItem? ItemAfterPostLookupAction,
    PreparationCandidateEntity PersistedCandidate,
    PreparationCandidatePayloadV1? PersistedEnvelope,
    IReadOnlyList<string> IsolatedPrefetchTerms);

public static class LexicalEvidenceCaptureHarness
{
    public const string CurrentHarnessVersion = "kf-lex-005-slice1-v1";
    public static readonly DateTime DefaultUtcNow = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);

    public static async Task<LexicalCandidateEvidence> CaptureEnrichmentAsync(
        LexicalEvidenceEnrichmentCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CaseId);
        ArgumentNullException.ThrowIfNull(options.Request);

        var utcNow = options.UtcNow ?? DefaultUtcNow;
        var clock = new FakeClock(utcNow);
        var delayRecorder = new NoDelayRecordingAsyncDelay();
        var diagnosticLog = new RecordingLexicalDiagnosticLog();
        var httpInstrumentation = new InstrumentedOfflineHttpMessageHandler(options.OfflineHttpHandler);
        using var httpClient = new HttpClient(httpInstrumentation)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var cacheInstrumentor = new InstrumentedLexicalCacheRepository(
            options.CacheRepository ?? new NoOpLexicalCacheRepository());

        var stepTracker = new LookupStepExecutionTracker(httpInstrumentation, delayRecorder, cacheInstrumentor);
        var (providers, wikiTracker) = BuildInstrumentedProviders(
            options.CustomProviders,
            options.SimulatedWikipediaApiClient,
            httpClient,
            clock,
            delayRecorder,
            diagnosticLog,
            stepTracker);

        var resolver = new InstrumentedProviderResolver(providers, stepTracker);
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cacheInstrumentor,
            resolver,
            diagnosticLog);

        var finalResult = await service.EnrichAsync(
            options.Request,
            options.OriginalDocumentContent ?? string.Empty,
            options.RepresentativeContext,
            cancellationToken);

        return BuildCandidateEvidence(
            caseId: options.CaseId,
            description: options.Description,
            executionMode: LexicalEvidenceExecutionMode.DirectEnrichmentService,
            transportRealism: options.TransportRealism,
            initialRequest: options.Request,
            representativeContext: options.RepresentativeContext,
            preparationLookupAttemptCount: null,
            finalResult: finalResult,
            stepTracker: stepTracker,
            cacheInstrumentor: cacheInstrumentor,
            httpInstrumentation: httpInstrumentation,
            wikiTracker: wikiTracker,
            delayRecorder: delayRecorder,
            diagnosticLog: diagnosticLog);
    }

    public static async Task<LexicalCandidateEvidence> CapturePreparationLookupAsync(
        LexicalEvidencePreparationCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        var correlation = await CapturePreparationCorrelationAsync(options, cancellationToken);
        return correlation.Evidence;
    }

    public static async Task<PreparationLookupCorrelationResult> CapturePreparationCorrelationAsync(
        LexicalEvidencePreparationCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CaseId);
        ArgumentNullException.ThrowIfNull(options.Database);

        var utcNow = options.UtcNow ?? DefaultUtcNow;
        var clock = new FakeClock(utcNow);
        var delayRecorder = new NoDelayRecordingAsyncDelay();
        var diagnosticLog = new RecordingLexicalDiagnosticLog();
        var httpInstrumentation = new InstrumentedOfflineHttpMessageHandler(options.OfflineHttpHandler);
        using var httpClient = new HttpClient(httpInstrumentation)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var cacheInstrumentor = new InstrumentedLexicalCacheRepository(
            options.CacheRepository ?? new LexicalCacheRepository(options.Database));

        var stepTracker = new LookupStepExecutionTracker(httpInstrumentation, delayRecorder, cacheInstrumentor);
        var (providers, wikiTracker) = BuildInstrumentedProviders(
            options.CustomProviders,
            options.SimulatedWikipediaApiClient,
            httpClient,
            clock,
            delayRecorder,
            diagnosticLog,
            stepTracker);

        var resolver = new InstrumentedProviderResolver(providers, stepTracker);
        var innerEnrichmentService = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cacheInstrumentor,
            resolver,
            diagnosticLog);
        var capturingEnrichmentService = new CapturingLexicalEnrichmentService(innerEnrichmentService);

        var preparationService = new PreparationService(
            options.Database,
            capturingEnrichmentService,
            clock,
            diagnosticLog);

        try
        {
            var currentBeforeStart = await preparationService.GetCurrentAsync();
            if (currentBeforeStart is null)
            {
                var sessionLimit = Math.Max(1, options.RequestedSessionLimit);
                await preparationService.StartAsync(
                    PreparationMethod.AutomaticOnline,
                    requestedLimit: sessionLimit);
            }

            var itemAfterLookup = await preparationService.LookupCurrentAsync(cancellationToken)
                ?? throw new InvalidOperationException($"PreparationService.LookupCurrentAsync returned null for case '{options.CaseId}'.");

            PreparationItem? itemAfterPostAction = null;
            if (options.PostLookupAction is not null)
            {
                await options.PostLookupAction(preparationService, itemAfterLookup, cancellationToken);
                itemAfterPostAction = await preparationService.GetCurrentAsync();
            }

            var candidateEntity = await options.Database.ReadAsync(connection =>
                connection.FindAsync<PreparationCandidateEntity>(itemAfterLookup.CandidateId))
                ?? throw new InvalidOperationException($"PreparationCandidateEntity {itemAfterLookup.CandidateId} not found after LookupCurrentAsync.");

            var payloadRead = PreparationCandidatePayloadCodec.Read(candidateEntity.ResultJson);
            var persistedEnvelope = payloadRead.Envelope;

            var capturedRequest = capturingEnrichmentService.LastRequest
                ?? throw new InvalidOperationException($"No LexicalLookupRequest was captured during PreparationService lookup for case '{options.CaseId}'.");

            var finalResult = capturingEnrichmentService.LastResult
                ?? itemAfterLookup.Result
                ?? throw new InvalidOperationException($"No LexicalResult was produced during PreparationService lookup for case '{options.CaseId}'.");

            var evidence = BuildCandidateEvidence(
                caseId: options.CaseId,
                description: options.Description,
                executionMode: LexicalEvidenceExecutionMode.PreparationServiceLookup,
                transportRealism: options.TransportRealism,
                initialRequest: capturedRequest,
                representativeContext: capturingEnrichmentService.LastRepresentativeContext,
                preparationLookupAttemptCount: candidateEntity.LookupAttemptCount,
                finalResult: finalResult,
                stepTracker: stepTracker,
                cacheInstrumentor: cacheInstrumentor,
                httpInstrumentation: httpInstrumentation,
                wikiTracker: wikiTracker,
                delayRecorder: delayRecorder,
                diagnosticLog: diagnosticLog);

            return new PreparationLookupCorrelationResult(
                Evidence: evidence,
                ItemAfterLookup: itemAfterLookup,
                ItemAfterPostLookupAction: itemAfterPostAction,
                PersistedCandidate: candidateEntity,
                PersistedEnvelope: persistedEnvelope,
                IsolatedPrefetchTerms: capturingEnrichmentService.IsolatedPrefetchTerms);
        }
        finally
        {
            await preparationService.CancelPrefetchAsync();
        }
    }

    public static LexicalEvidenceReportDocument BuildReportDocument(
        string suiteId,
        string description,
        IReadOnlyList<LexicalCandidateEvidence> candidates,
        DateTime? capturedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suiteId);
        ArgumentNullException.ThrowIfNull(candidates);

        var orderedCandidates = candidates
            .OrderBy(c => c.CaseId, StringComparer.Ordinal)
            .ToArray();

        var summary = BuildSummary(orderedCandidates);
        return new LexicalEvidenceReportDocument(
            SuiteId: suiteId,
            Description: description ?? string.Empty,
            CapturedAtUtc: capturedAtUtc ?? DefaultUtcNow,
            Summary: summary,
            Candidates: orderedCandidates);
    }

    public static LexicalEvidenceSummary BuildSummary(IReadOnlyList<LexicalCandidateEvidence> orderedCandidates)
    {
        ArgumentNullException.ThrowIfNull(orderedCandidates);

        var total = orderedCandidates.Count;
        var germanCount = orderedCandidates.Count(c => string.Equals(c.SourceLanguage, "de", StringComparison.OrdinalIgnoreCase));
        var englishCount = orderedCandidates.Count(c => string.Equals(c.SourceLanguage, "en", StringComparison.OrdinalIgnoreCase));
        var definitionCount = orderedCandidates.Count(c => c.LookupMode == LexicalLookupMode.Definition);
        var translationCount = orderedCandidates.Count(c => c.LookupMode == LexicalLookupMode.Translation);
        var defAndTransCount = orderedCandidates.Count(c => c.LookupMode == LexicalLookupMode.DefinitionAndTranslation);

        var measuredHttpCandidateCount = orderedCandidates.Count(c => c.TransportRealism == LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport);
        var simulatedWikiCandidateCount = orderedCandidates.Count(c => c.TransportRealism == LexicalEvidenceTransportRealism.SimulatedWikipediaApiClient);
        var simulatedProviderCandidateCount = orderedCandidates.Count(c => c.TransportRealism == LexicalEvidenceTransportRealism.SimulatedProviderBoundary);
        var prepCandidateCount = orderedCandidates.Count(c => c.ExecutionMode == LexicalEvidenceExecutionMode.PreparationServiceLookup);
        var directCandidateCount = orderedCandidates.Count(c => c.ExecutionMode == LexicalEvidenceExecutionMode.DirectEnrichmentService);

        var cacheConsultedCandidateCount = orderedCandidates.Count(c => c.CacheReadAttemptCount > 0);
        var cacheConsultedStepCount = orderedCandidates.Sum(c => c.LookupSteps.Count(s => s.CacheOutcome != LexicalCacheObservationOutcome.NotConsulted));
        var germanCaseFallbackEligibleCount = orderedCandidates.Count(c =>
            string.Equals(c.SourceLanguage, "de", StringComparison.OrdinalIgnoreCase) && c.TokenKind == TokenKind.Word);
        var lemmaRedirectCandidateCount = orderedCandidates.Count(c => c.LemmaRedirectAttempted);
        var wiktionaryNotFoundCandidateCount = orderedCandidates.Count(c =>
            c.LookupSteps.Any(s => !s.IsFallbackStep
                && string.Equals(s.ProviderName, WiktionaryLookupProvider.Name, StringComparison.OrdinalIgnoreCase)
                && s.StepStatus == LexicalLookupStatus.NotFound));
        var wikiFallbackEligibleCount = orderedCandidates.Count(c => c.WikipediaFallbackEligible);
        var wikiFallbackTriggeredCount = orderedCandidates.Count(c => c.WikipediaFallbackTriggered);
        var multiMeaningSuccessCandidateCount = orderedCandidates.Count(c =>
            c.FinalStatus == LexicalLookupStatus.Success && c.PreRankingMeanings.Count > 1);

        var totalPrepAttempts = orderedCandidates.Sum(c => c.PreparationLookupAttemptCount ?? 0);
        var totalProviderInvocations = orderedCandidates.Sum(c => c.ProviderInvocationCount);
        var totalWikiClientInvocations = orderedCandidates.Sum(c => c.WikipediaApiClientInvocationCount);
        var totalHttpAttempts = orderedCandidates.Sum(c => c.HttpAttemptCount);
        var totalHttpRetries = orderedCandidates.Sum(c => c.HttpRetryCount);

        var totalCacheReads = orderedCandidates.Sum(c => c.CacheReadAttemptCount);
        var totalCacheHits = orderedCandidates.Sum(c => c.CacheHitCount);
        var totalCacheMisses = orderedCandidates.Sum(c => c.CacheMissCount);
        var totalCacheReadFailures = orderedCandidates.Sum(c => c.CacheReadFailureCount);
        var totalCacheWrites = orderedCandidates.Sum(c => c.CacheWriteAttemptCount);
        var totalCacheStored = orderedCandidates.Sum(c => c.CacheWriteStoredCount);
        var totalCacheSkipped = orderedCandidates.Sum(c => c.CacheWriteSkippedCount);
        var totalCacheWriteFailures = orderedCandidates.Sum(c => c.CacheWriteFailureCount);

        var successCount = orderedCandidates.Count(c => c.FinalStatus == LexicalLookupStatus.Success);
        var notFoundCount = orderedCandidates.Count(c => c.FinalStatus == LexicalLookupStatus.NotFound);
        var parseFailureCount = orderedCandidates.Count(c => c.FinalStatus == LexicalLookupStatus.ParseFailure);
        var transientFailureCount = orderedCandidates.Count(c => c.FinalStatus == LexicalLookupStatus.TransientFailure);
        var permanentFailureCount = orderedCandidates.Count(c => c.FinalStatus == LexicalLookupStatus.PermanentFailure);
        var usableDataCount = orderedCandidates.Count(c => c.HasUsableData);
        var referenceDataCount = orderedCandidates.Count(c => c.HasReferenceData);

        var caseFallbackAttemptedCount = orderedCandidates.Count(c => c.CaseFallbackAttempted);
        var caseFallbackSucceededCount = orderedCandidates.Count(c => c.CaseFallbackSucceeded);
        var lemmaRedirectAttemptedCount = orderedCandidates.Count(c => c.LemmaRedirectAttempted);
        var lemmaRedirectSucceededCount = orderedCandidates.Count(c => c.LemmaRedirectSucceeded);
        var wikiFallbackSucceededCount = orderedCandidates.Count(c => c.WikipediaFallbackSucceeded);
        var meaningReorderedCount = orderedCandidates.Count(c => c.MeaningOrderChangedByRanking);

        var canonicalStatuses = new[]
        {
            LexicalLookupStatus.Success,
            LexicalLookupStatus.NotFound,
            LexicalLookupStatus.ParseFailure,
            LexicalLookupStatus.TransientFailure,
            LexicalLookupStatus.PermanentFailure
        };

        var statusBreakdown = canonicalStatuses
            .Select(status =>
            {
                var count = orderedCandidates.Count(c => c.FinalStatus == status);
                return new LexicalStatusCountEntry(
                    Status: status,
                    Count: count,
                    Denominator: total,
                    Percentage: ComputePercentage(count, total));
            })
            .ToArray();

        var classificationBreakdown = orderedCandidates
            .GroupBy(c => c.OutcomeClassification)
            .OrderBy(g => g.Key)
            .Select(g => new LexicalClassificationCountEntry(
                Classification: g.Key,
                Count: g.Count(),
                Denominator: total,
                Percentage: ComputePercentage(g.Count(), total)))
            .ToArray();

        var rateMetrics = new LexicalRateMetric[]
        {
            CreateRateMetric("overall-success-rate", "Candidates ending in LexicalLookupStatus.Success over TotalCandidateCount.", successCount, total, "TotalCandidateCount"),
            CreateRateMetric("usable-data-rate", "Candidates with HasUsableData == true over TotalCandidateCount.", usableDataCount, total, "TotalCandidateCount"),
            CreateRateMetric("cache-hit-rate-over-reads", "Total cache hits over TotalCacheReadAttemptCount.", totalCacheHits, totalCacheReads, "TotalCacheReadAttemptCount"),
            CreateRateMetric("german-case-fallback-attempt-rate", "German case fallback attempts over GermanCaseFallbackEligibleCandidateCount.", caseFallbackAttemptedCount, germanCaseFallbackEligibleCount, "GermanCaseFallbackEligibleCandidateCount"),
            CreateRateMetric("german-case-fallback-recovery-rate", "German case fallback successes over GermanCaseFallbackAttemptedCount.", caseFallbackSucceededCount, caseFallbackAttemptedCount, "GermanCaseFallbackAttemptedCount"),
            CreateRateMetric("lemma-redirect-recovery-rate", "Lemma redirect successes over LemmaRedirectAttemptedCount.", lemmaRedirectSucceededCount, lemmaRedirectAttemptedCount, "LemmaRedirectAttemptedCount"),
            CreateRateMetric("wikipedia-fallback-trigger-rate", "Wikipedia fallback triggers over WikipediaFallbackEligibleCandidateCount.", wikiFallbackTriggeredCount, wikiFallbackEligibleCount, "WikipediaFallbackEligibleCandidateCount"),
            CreateRateMetric("wikipedia-fallback-recovery-rate", "Wikipedia fallback successes over WikipediaFallbackTriggeredCandidateCount.", wikiFallbackSucceededCount, wikiFallbackTriggeredCount, "WikipediaFallbackTriggeredCandidateCount"),
            CreateRateMetric("meaning-reorder-rate", "Candidates where ranking changed provider order over MultiMeaningSuccessCandidateCount.", meaningReorderedCount, multiMeaningSuccessCandidateCount, "MultiMeaningSuccessCandidateCount")
        };

        var failedTerms = orderedCandidates
            .Where(c => c.FinalStatus != LexicalLookupStatus.Success)
            .Select(c => new LexicalFailedTermEvidence(
                CaseId: c.CaseId,
                SourceLanguage: c.SourceLanguage,
                LookupMode: c.LookupMode,
                TargetLanguage: c.TargetLanguage,
                DisplayedSurfaceForm: c.DisplayedSurfaceForm,
                VocabularyCanonicalTerm: c.VocabularyCanonicalTerm,
                CanonicalLookupTerm: c.CanonicalLookupTerm,
                FinalQueriedLemma: c.FinalQueriedLemma,
                FinalProviderName: c.FinalProviderName,
                FinalStatus: c.FinalStatus,
                FinalErrorCode: c.FinalErrorCode,
                OutcomeClassification: c.OutcomeClassification,
                OrderedProviderChain: c.OrderedProviderChain,
                OrderedQueryCandidates: c.OrderedQueryCandidates,
                PreparationLookupAttemptCount: c.PreparationLookupAttemptCount,
                ProviderInvocationCount: c.ProviderInvocationCount,
                WikipediaApiClientInvocationCount: c.WikipediaApiClientInvocationCount,
                HttpAttemptCount: c.HttpAttemptCount,
                HttpRetryCount: c.HttpRetryCount,
                CanRetry: c.CanRetry,
                Explanation: $"Lookup failed with status {c.FinalStatus} and error code '{c.FinalErrorCode ?? "none"}' ({c.OutcomeClassification})."))
            .ToArray();

        var problematicTerms = BuildProblematicTerms(orderedCandidates);

        return new LexicalEvidenceSummary(
            TotalCandidateCount: total,
            GermanCandidateCount: germanCount,
            EnglishCandidateCount: englishCount,
            DefinitionModeCandidateCount: definitionCount,
            TranslationModeCandidateCount: translationCount,
            DefinitionAndTranslationModeCandidateCount: defAndTransCount,
            MeasuredHttpTransportCandidateCount: measuredHttpCandidateCount,
            SimulatedWikipediaApiClientCandidateCount: simulatedWikiCandidateCount,
            SimulatedProviderBoundaryCandidateCount: simulatedProviderCandidateCount,
            PreparationServiceCandidateCount: prepCandidateCount,
            DirectEnrichmentCandidateCount: directCandidateCount,
            CacheConsultedCandidateCount: cacheConsultedCandidateCount,
            CacheConsultedStepCount: cacheConsultedStepCount,
            GermanCaseFallbackEligibleCandidateCount: germanCaseFallbackEligibleCount,
            LemmaRedirectCandidateCount: lemmaRedirectCandidateCount,
            WiktionaryNotFoundCandidateCount: wiktionaryNotFoundCandidateCount,
            WikipediaFallbackEligibleCandidateCount: wikiFallbackEligibleCount,
            WikipediaFallbackTriggeredCandidateCount: wikiFallbackTriggeredCount,
            MultiMeaningSuccessCandidateCount: multiMeaningSuccessCandidateCount,
            TotalPreparationLookupAttemptCount: totalPrepAttempts,
            TotalProviderInvocationCount: totalProviderInvocations,
            TotalWikipediaApiClientInvocationCount: totalWikiClientInvocations,
            TotalHttpAttemptCount: totalHttpAttempts,
            TotalHttpRetryCount: totalHttpRetries,
            TotalCacheReadAttemptCount: totalCacheReads,
            TotalCacheHitCount: totalCacheHits,
            TotalCacheMissCount: totalCacheMisses,
            TotalCacheReadFailureCount: totalCacheReadFailures,
            TotalCacheWriteAttemptCount: totalCacheWrites,
            TotalCacheWriteStoredCount: totalCacheStored,
            TotalCacheWriteSkippedCount: totalCacheSkipped,
            TotalCacheWriteFailureCount: totalCacheWriteFailures,
            SuccessCount: successCount,
            NotFoundCount: notFoundCount,
            ParseFailureCount: parseFailureCount,
            TransientFailureCount: transientFailureCount,
            PermanentFailureCount: permanentFailureCount,
            UsableDataCount: usableDataCount,
            ReferenceDataCount: referenceDataCount,
            GermanCaseFallbackAttemptedCount: caseFallbackAttemptedCount,
            GermanCaseFallbackSucceededCount: caseFallbackSucceededCount,
            LemmaRedirectAttemptedCount: lemmaRedirectAttemptedCount,
            LemmaRedirectSucceededCount: lemmaRedirectSucceededCount,
            WikipediaFallbackSucceededCount: wikiFallbackSucceededCount,
            MeaningOrderChangedByRankingCount: meaningReorderedCount,
            StatusBreakdown: statusBreakdown,
            ClassificationBreakdown: classificationBreakdown,
            RateMetrics: rateMetrics,
            FailedTerms: failedTerms,
            ProblematicTerms: problematicTerms);
    }

    private static IReadOnlyList<LexicalProblematicTermEvidence> BuildProblematicTerms(
        IReadOnlyList<LexicalCandidateEvidence> orderedCandidates)
    {
        var list = new List<LexicalProblematicTermEvidence>();
        foreach (var c in orderedCandidates)
        {
            if (c.FinalStatus != LexicalLookupStatus.Success)
            {
                list.Add(CreateProblematicTerm(
                    c,
                    LexicalProblemKind.LookupFailed,
                    $"Lookup failed with status {c.FinalStatus} and code '{c.FinalErrorCode ?? "none"}'."));
                continue;
            }

            if (c.CaseFallbackSucceeded)
            {
                list.Add(CreateProblematicTerm(
                    c,
                    LexicalProblemKind.RecoveredViaGermanCaseFallback,
                    $"Primary German query missed; recovered via lowercase initial rune fallback ('{c.FinalPageTitle}')."));
            }

            if (c.LemmaRedirectSucceeded)
            {
                list.Add(CreateProblematicTerm(
                    c,
                    LexicalProblemKind.RecoveredViaLemmaRedirect,
                    $"Inflected form '{c.EncounteredSurfaceForm}' redirected (depth {c.FinalRedirectDepth}) to '{c.FinalDisplayTerm}'."));
            }

            if (c.WikipediaFallbackSucceeded)
            {
                list.Add(CreateProblematicTerm(
                    c,
                    LexicalProblemKind.RecoveredViaWikipediaFallback,
                    $"Wiktionary missed; recovered via Wikipedia fallback ('{c.FinalPageTitle}')."));
            }

            if (c.HttpRetryCount > 0)
            {
                list.Add(CreateProblematicTerm(
                    c,
                    LexicalProblemKind.RecoveredAfterHttpRetry,
                    $"Recovered after {c.HttpRetryCount} HTTP retry attempt(s)."));
            }

            if (c.CacheReadFailureCount > 0 || c.CacheWriteFailureCount > 0)
            {
                list.Add(CreateProblematicTerm(
                    c,
                    LexicalProblemKind.CacheOperationFailed,
                    $"Observed cache fault (readFailures={c.CacheReadFailureCount}, writeFailures={c.CacheWriteFailureCount})."));
            }
        }

        return list;
    }

    private static LexicalProblematicTermEvidence CreateProblematicTerm(
        LexicalCandidateEvidence c,
        LexicalProblemKind kind,
        string explanation) => new(
            CaseId: c.CaseId,
            ProblemKind: kind,
            SourceLanguage: c.SourceLanguage,
            LookupMode: c.LookupMode,
            TargetLanguage: c.TargetLanguage,
            DisplayedSurfaceForm: c.DisplayedSurfaceForm,
            CanonicalLookupTerm: c.CanonicalLookupTerm,
            FinalQueriedLemma: c.FinalQueriedLemma,
            FinalProviderName: c.FinalProviderName,
            FinalStatus: c.FinalStatus,
            FinalErrorCode: c.FinalErrorCode,
            OutcomeClassification: c.OutcomeClassification,
            OrderedProviderChain: c.OrderedProviderChain,
            OrderedQueryCandidates: c.OrderedQueryCandidates,
            ProviderInvocationCount: c.ProviderInvocationCount,
            HttpAttemptCount: c.HttpAttemptCount,
            HttpRetryCount: c.HttpRetryCount,
            Explanation: explanation);

    private static LexicalCandidateEvidence BuildCandidateEvidence(
        string caseId,
        string description,
        LexicalEvidenceExecutionMode executionMode,
        LexicalEvidenceTransportRealism transportRealism,
        LexicalLookupRequest initialRequest,
        string? representativeContext,
        int? preparationLookupAttemptCount,
        LexicalResult finalResult,
        LookupStepExecutionTracker stepTracker,
        InstrumentedLexicalCacheRepository cacheInstrumentor,
        InstrumentedOfflineHttpMessageHandler httpInstrumentation,
        InstrumentedWikipediaApiClient? wikiTracker,
        NoDelayRecordingAsyncDelay delayRecorder,
        RecordingLexicalDiagnosticLog diagnosticLog)
    {
        var steps = stepTracker.BuildSteps();
        var orderedProviderChain = steps
            .Select(s => s.ProviderName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var orderedQueryCandidates = steps
            .SelectMany(s => s.QueryCandidates)
            .ToArray();

        var primaryCacheOutcome = steps.Count > 0
            ? steps[0].CacheOutcome
            : cacheInstrumentor.FirstReadOutcome;

        var effectiveCacheOutcome = steps.Count > 0
            ? steps[^1].CacheOutcome
            : cacheInstrumentor.LastReadOutcome;

        var caseFallbackAttempted = steps.Any(s => s.CaseFallbackTriggered);
        var caseFallbackSucceeded = steps.Any(s => s.CaseFallbackTriggered && s.StepStatus == LexicalLookupStatus.Success)
            && finalResult.Status == LexicalLookupStatus.Success;

        var nonFallbackSteps = steps.Where(s => !s.IsFallbackStep).ToArray();
        var lemmaRedirectAttempted = nonFallbackSteps.Length > 1
            || nonFallbackSteps.Any(s => s.ExtractedFormRelations.Count > 0)
            || finalResult.RedirectDepth > 0
            || string.Equals(finalResult.ErrorCode, "lemma-redirect-loop", StringComparison.Ordinal)
            || string.Equals(finalResult.ErrorCode, "lemma-redirect-depth-exceeded", StringComparison.Ordinal);

        var lemmaRedirectSucceeded = lemmaRedirectAttempted
            && finalResult.Status == LexicalLookupStatus.Success
            && (finalResult.RedirectDepth > 0 || !string.IsNullOrWhiteSpace(finalResult.GrammaticalRelationship));

        var (wikiEligible, wikiTriggered, wikiSucceeded, wikiDisposition) = EvaluateWikipediaFallbackDisposition(
            initialRequest,
            steps,
            finalResult);

        var preRankingRawMeanings = stepTracker.LastWinningPreRankingDirectMeanings;
        var preRankingSnapshots = BuildPreRankingMeaningSnapshots(preRankingRawMeanings);
        var rankedSnapshots = BuildRankedMeaningSnapshots(finalResult.Meanings, preRankingRawMeanings);
        var meaningOrderChanged = DetermineMeaningOrderChanged(preRankingSnapshots, rankedSnapshots);

        var outcomeClassification = ClassifyOutcome(
            finalResult: finalResult,
            caseFallbackSucceeded: caseFallbackSucceeded,
            lemmaRedirectSucceeded: lemmaRedirectSucceeded,
            wikiTriggered: wikiTriggered,
            wikiSucceeded: wikiSucceeded,
            wikiDisposition: wikiDisposition);

        var cacheWriteSkippedCount = steps.Count(s => s.CacheWriteOutcome == LexicalCacheWriteOutcome.SkippedNotCacheable);

        return new LexicalCandidateEvidence(
            CaseId: caseId,
            Description: description ?? string.Empty,
            ExecutionMode: executionMode,
            TransportRealism: transportRealism,
            SourceLanguage: initialRequest.SourceLanguage,
            LookupMode: initialRequest.LookupMode,
            TargetLanguage: initialRequest.TargetLanguage,
            ExplanationLanguage: initialRequest.ExplanationLanguage,
            RequestedProvider: initialRequest.Provider,
            DisplayedSurfaceForm: initialRequest.DisplayedSurfaceForm,
            VocabularyCanonicalTerm: initialRequest.VocabularyCanonicalTerm,
            CanonicalLookupTerm: initialRequest.CanonicalLookupTerm,
            TokenKind: initialRequest.TokenKind,
            RepresentativeContext: representativeContext,
            PreparationLookupAttemptCount: preparationLookupAttemptCount,
            ProviderInvocationCount: stepTracker.TotalProviderInvocationCount,
            WikipediaApiClientInvocationCount: wikiTracker?.InvocationCount ?? 0,
            HttpAttemptCount: httpInstrumentation.TotalHttpAttemptCount,
            HttpRetryCount: steps.Sum(s => s.HttpRetryCount),
            RecordedRetryDelayMilliseconds: delayRecorder.RecordedDelaysMilliseconds.ToArray(),
            CacheReadAttemptCount: cacheInstrumentor.ReadAttemptCount,
            CacheHitCount: cacheInstrumentor.HitCount,
            CacheMissCount: cacheInstrumentor.MissCount,
            CacheReadFailureCount: cacheInstrumentor.ReadFailureCount,
            CacheWriteAttemptCount: cacheInstrumentor.WriteAttemptCount,
            CacheWriteStoredCount: cacheInstrumentor.WriteStoredCount,
            CacheWriteSkippedCount: cacheWriteSkippedCount,
            CacheWriteFailureCount: cacheInstrumentor.WriteFailureCount,
            PrimaryCacheOutcome: primaryCacheOutcome,
            EffectiveCacheOutcome: effectiveCacheOutcome,
            FinalResultFromCache: finalResult.IsFromCache,
            OrderedProviderChain: orderedProviderChain,
            OrderedQueryCandidates: orderedQueryCandidates,
            LookupSteps: steps,
            CaseFallbackAttempted: caseFallbackAttempted,
            CaseFallbackSucceeded: caseFallbackSucceeded,
            LemmaRedirectAttempted: lemmaRedirectAttempted,
            LemmaRedirectSucceeded: lemmaRedirectSucceeded,
            FinalRedirectDepth: finalResult.RedirectDepth,
            EncounteredSurfaceForm: finalResult.EncounteredSurfaceForm,
            GrammaticalRelationship: finalResult.GrammaticalRelationship,
            WikipediaFallbackEligible: wikiEligible,
            WikipediaFallbackTriggered: wikiTriggered,
            WikipediaFallbackSucceeded: wikiSucceeded,
            WikipediaFallbackDisposition: wikiDisposition,
            PreRankingMeanings: preRankingSnapshots,
            RankedMeanings: rankedSnapshots,
            MeaningOrderChangedByRanking: meaningOrderChanged,
            FinalStatus: finalResult.Status,
            FinalErrorCode: finalResult.ErrorCode,
            OutcomeClassification: outcomeClassification,
            FinalProviderName: finalResult.ProviderName,
            FinalSourceProject: finalResult.SourceProject,
            FinalPageTitle: finalResult.PageTitle,
            FinalRevisionId: finalResult.RevisionId,
            FinalQueriedLemma: finalResult.QueriedLemma,
            FinalDisplayTerm: finalResult.DisplayTerm,
            AcronymExpansion: finalResult.AcronymExpansion,
            HasUsableData: finalResult.HasUsableData,
            HasReferenceData: finalResult.HasReferenceData,
            CanRetry: finalResult.Status == LexicalLookupStatus.TransientFailure,
            DiagnosticEvents: diagnosticLog.Events.ToArray());
    }

    private static (bool Eligible, bool Triggered, bool Succeeded, string Disposition) EvaluateWikipediaFallbackDisposition(
        LexicalLookupRequest initialRequest,
        IReadOnlyList<LexicalLookupStepEvidence> steps,
        LexicalResult finalResult)
    {
        var fallbackStep = steps.FirstOrDefault(s => s.IsFallbackStep);
        var triggered = fallbackStep is not null;
        var succeeded = fallbackStep is { StepStatus: LexicalLookupStatus.Success }
            && finalResult.Status == LexicalLookupStatus.Success
            && string.Equals(finalResult.ProviderName, WikipediaLookupProvider.Name, StringComparison.OrdinalIgnoreCase);

        if (triggered)
        {
            return (true, true, succeeded, "eligible-and-triggered");
        }

        var lastPrimaryStep = steps.LastOrDefault(s => !s.IsFallbackStep);
        if (lastPrimaryStep is null)
        {
            return (false, false, false, "not-evaluated-no-primary-step");
        }

        if (lastPrimaryStep.StepStatus == LexicalLookupStatus.Success && lastPrimaryStep.ExtractedDirectMeaningCount > 0)
        {
            return (false, false, false, "not-needed-primary-succeeded");
        }

        if (initialRequest.LookupMode == LexicalLookupMode.Translation && lastPrimaryStep.StepStatus == LexicalLookupStatus.NotFound)
        {
            return (false, false, false, "excluded-translation-only-mode");
        }

        if (lastPrimaryStep.StepStatus != LexicalLookupStatus.NotFound)
        {
            return (false, false, false, $"excluded-primary-status-{lastPrimaryStep.StepStatus}");
        }

        return (false, false, false, "not-triggered");
    }

    private static IReadOnlyList<LexicalMeaningSnapshot> BuildPreRankingMeaningSnapshots(
        IReadOnlyList<LexicalMeaning> preRankingMeanings)
    {
        if (preRankingMeanings.Count == 0)
        {
            return [];
        }

        var snapshots = new LexicalMeaningSnapshot[preRankingMeanings.Count];
        for (var i = 0; i < preRankingMeanings.Count; i++)
        {
            var meaning = preRankingMeanings[i];
            var classification = MeaningRelevancePolicy.Classify(meaning);
            snapshots[i] = new LexicalMeaningSnapshot(
                OrderIndex: i,
                OriginalProviderIndex: i,
                MeaningId: meaning.MeaningId,
                PartOfSpeech: meaning.PartOfSpeech,
                Definition: meaning.Definition,
                Translation: meaning.Translation,
                Example: meaning.Example,
                UsageLabels: meaning.UsageLabels,
                RelevanceTier: classification.Tier,
                TriggeringDemotionLabel: classification.TriggeringLabel);
        }

        return snapshots;
    }

    private static IReadOnlyList<LexicalMeaningSnapshot> BuildRankedMeaningSnapshots(
        IReadOnlyList<LexicalMeaning> rankedMeanings,
        IReadOnlyList<LexicalMeaning> preRankingMeanings)
    {
        if (rankedMeanings.Count == 0)
        {
            return [];
        }

        var usedOriginalIndices = new HashSet<int>();
        var snapshots = new LexicalMeaningSnapshot[rankedMeanings.Count];

        for (var rankedIndex = 0; rankedIndex < rankedMeanings.Count; rankedIndex++)
        {
            var meaning = rankedMeanings[rankedIndex];
            var originalIndex = FindOriginalIndex(meaning, preRankingMeanings, usedOriginalIndices, rankedIndex);
            var classification = MeaningRelevancePolicy.Classify(meaning);
            snapshots[rankedIndex] = new LexicalMeaningSnapshot(
                OrderIndex: rankedIndex,
                OriginalProviderIndex: originalIndex,
                MeaningId: meaning.MeaningId,
                PartOfSpeech: meaning.PartOfSpeech,
                Definition: meaning.Definition,
                Translation: meaning.Translation,
                Example: meaning.Example,
                UsageLabels: meaning.UsageLabels,
                RelevanceTier: classification.Tier,
                TriggeringDemotionLabel: classification.TriggeringLabel);
        }

        return snapshots;
    }

    private static int FindOriginalIndex(
        LexicalMeaning meaning,
        IReadOnlyList<LexicalMeaning> preRankingMeanings,
        HashSet<int> usedOriginalIndices,
        int fallbackIndex)
    {
        for (var i = 0; i < preRankingMeanings.Count; i++)
        {
            if (usedOriginalIndices.Contains(i))
            {
                continue;
            }

            if (string.Equals(preRankingMeanings[i].MeaningId, meaning.MeaningId, StringComparison.Ordinal))
            {
                usedOriginalIndices.Add(i);
                return i;
            }
        }

        for (var i = 0; i < preRankingMeanings.Count; i++)
        {
            if (usedOriginalIndices.Contains(i))
            {
                continue;
            }

            if (string.Equals(preRankingMeanings[i].Definition, meaning.Definition, StringComparison.Ordinal)
                && string.Equals(preRankingMeanings[i].Translation, meaning.Translation, StringComparison.Ordinal))
            {
                usedOriginalIndices.Add(i);
                return i;
            }
        }

        return fallbackIndex;
    }

    private static bool DetermineMeaningOrderChanged(
        IReadOnlyList<LexicalMeaningSnapshot> preRanking,
        IReadOnlyList<LexicalMeaningSnapshot> ranked)
    {
        if (preRanking.Count <= 1 || preRanking.Count != ranked.Count)
        {
            return false;
        }

        for (var i = 0; i < ranked.Count; i++)
        {
            if (ranked[i].OriginalProviderIndex != i)
            {
                return true;
            }
        }

        return false;
    }

    private static LexicalOutcomeClassification ClassifyOutcome(
        LexicalResult finalResult,
        bool caseFallbackSucceeded,
        bool lemmaRedirectSucceeded,
        bool wikiTriggered,
        bool wikiSucceeded,
        string wikiDisposition)
    {
        var code = finalResult.ErrorCode ?? string.Empty;

        if (finalResult.Status == LexicalLookupStatus.Success)
        {
            if (finalResult.IsFromCache)
            {
                return LexicalOutcomeClassification.CacheHitSuccess;
            }

            if (wikiSucceeded)
            {
                return LexicalOutcomeClassification.WikipediaFallbackSuccess;
            }

            if (lemmaRedirectSucceeded)
            {
                return LexicalOutcomeClassification.LemmaRedirectSuccess;
            }

            if (caseFallbackSucceeded)
            {
                return LexicalOutcomeClassification.GermanCaseFallbackSuccess;
            }

            return LexicalOutcomeClassification.DirectSuccess;
        }

        if (finalResult.Status == LexicalLookupStatus.NotFound)
        {
            if (string.Equals(wikiDisposition, "excluded-translation-only-mode", StringComparison.Ordinal))
            {
                return LexicalOutcomeClassification.TranslationModeFallbackExcluded;
            }

            if (wikiTriggered)
            {
                return code switch
                {
                    "disambiguation" => LexicalOutcomeClassification.WikipediaDisambiguation,
                    "no-usable-extract" => LexicalOutcomeClassification.WikipediaNoUsableContent,
                    _ => LexicalOutcomeClassification.OtherNotFound
                };
            }

            return code switch
            {
                "missing-page" => LexicalOutcomeClassification.MissingPage,
                "language-section-not-found" or "missing-language-section" => LexicalOutcomeClassification.LanguageSectionNotFound,
                "definition-not-found" => LexicalOutcomeClassification.DefinitionNotFound,
                "translation-not-found" => LexicalOutcomeClassification.TranslationNotFound,
                "no-suitable-direct-sense" => LexicalOutcomeClassification.NoSuitableDirectSense,
                _ => LexicalOutcomeClassification.OtherNotFound
            };
        }

        if (finalResult.Status == LexicalLookupStatus.ParseFailure)
        {
            return code switch
            {
                "malformed-json" => LexicalOutcomeClassification.MalformedJson,
                "missing-parse-payload" => LexicalOutcomeClassification.MissingParsePayload,
                "missing-html" or "missing-html-content" => LexicalOutcomeClassification.MissingHtml,
                "redirect-anomaly" => LexicalOutcomeClassification.WikipediaRedirectAnomaly,
                _ => LexicalOutcomeClassification.OtherParseFailure
            };
        }

        if (finalResult.Status == LexicalLookupStatus.TransientFailure)
        {
            return code switch
            {
                "rate-limited" => LexicalOutcomeClassification.RateLimited,
                "transient-server-error" => LexicalOutcomeClassification.TransientServerError,
                "timeout" => LexicalOutcomeClassification.TimedOut,
                "network-unavailable" or "network-failure" => LexicalOutcomeClassification.NetworkUnavailable,
                _ => LexicalOutcomeClassification.OtherTransientFailure
            };
        }

        return code switch
        {
            "lemma-redirect-loop" => LexicalOutcomeClassification.LemmaRedirectLoop,
            "lemma-redirect-depth-exceeded" => LexicalOutcomeClassification.LemmaRedirectDepthExceeded,
            "provider-not-registered" => LexicalOutcomeClassification.ProviderNotRegistered,
            "provider-identity-mismatch" => LexicalOutcomeClassification.ProviderIdentityMismatch,
            "provider-crash" => LexicalOutcomeClassification.ProviderCrashOrError,
            _ when code.StartsWith("http-4", StringComparison.OrdinalIgnoreCase) => LexicalOutcomeClassification.HttpClientError,
            _ when code.StartsWith("api-", StringComparison.OrdinalIgnoreCase) => LexicalOutcomeClassification.ApiPermanentError,
            _ => LexicalOutcomeClassification.OtherPermanentFailure
        };
    }

    private static (IReadOnlyList<ILexicalLookupProvider> Providers, InstrumentedWikipediaApiClient? WikiTracker) BuildInstrumentedProviders(
        IReadOnlyList<ILexicalLookupProvider>? customProviders,
        IWikipediaApiClient? simulatedWikipediaApiClient,
        HttpClient httpClient,
        IClock clock,
        IAsyncDelay delay,
        ILexicalDiagnosticLog diagnosticLog,
        LookupStepExecutionTracker stepTracker)
    {
        if (customProviders is { Count: > 0 })
        {
            var wrappedCustom = customProviders
                .Select(p => (ILexicalLookupProvider)new InstrumentedLexicalLookupProvider(p, stepTracker))
                .ToArray();
            return (wrappedCustom, null);
        }

        var wiktionary = new WiktionaryLookupProvider(httpClient, new WiktionaryHtmlParser(), clock, delay, diagnosticLog: diagnosticLog);
        var rawWikiClient = simulatedWikipediaApiClient ?? new WikipediaApiClient(httpClient, clock);
        var wikiTracker = new InstrumentedWikipediaApiClient(rawWikiClient, stepTracker);
        var wikipedia = new WikipediaLookupProvider(wikiTracker, clock);

        return (
            [
                new InstrumentedLexicalLookupProvider(wiktionary, stepTracker),
                new InstrumentedLexicalLookupProvider(wikipedia, stepTracker)
            ],
            wikiTracker);
    }

    private static LexicalRateMetric CreateRateMetric(
        string metricCode,
        string description,
        int numerator,
        int denominator,
        string denominatorName) => new(
            MetricCode: metricCode,
            Description: description,
            Numerator: numerator,
            Denominator: denominator,
            DenominatorName: denominatorName,
            Percentage: ComputePercentage(numerator, denominator));

    private static double? ComputePercentage(int numerator, int denominator) =>
        denominator <= 0 ? null : Math.Round((double)numerator * 100.0 / denominator, 2, MidpointRounding.AwayFromZero);

    private sealed class CapturingLexicalEnrichmentService(ILexicalEnrichmentService inner) : ILexicalEnrichmentService
    {
        private readonly object _sync = new();
        private readonly List<string> _isolatedPrefetchTerms = [];
        private bool _primaryLookupClaimed;

        public LexicalLookupRequest? LastRequest { get; private set; }
        public string? LastRepresentativeContext { get; private set; }
        public LexicalResult? LastResult { get; private set; }

        public IReadOnlyList<string> IsolatedPrefetchTerms
        {
            get
            {
                lock (_sync)
                {
                    return [.. _isolatedPrefetchTerms];
                }
            }
        }

        public async Task<LexicalResult> EnrichAsync(
            LexicalLookupRequest request,
            string originalDocumentContent,
            string? representativeContext,
            CancellationToken cancellationToken = default)
        {
            var isPrimary = false;
            lock (_sync)
            {
                if (!_primaryLookupClaimed)
                {
                    _primaryLookupClaimed = true;
                    isPrimary = true;
                    LastRequest = request;
                    LastRepresentativeContext = representativeContext;
                }
                else
                {
                    _isolatedPrefetchTerms.Add(request.CanonicalLookupTerm);
                }
            }

            if (!isPrimary)
            {
                // Isolate background prefetch from contaminating the primary candidate's evidence or shared cache.
                throw new OperationCanceledException(cancellationToken);
            }

            var result = await inner.EnrichAsync(request, originalDocumentContent, representativeContext, cancellationToken);
            LastResult = result;
            return result;
        }
    }

    private sealed class InstrumentedOfflineHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler) : HttpMessageHandler
    {
        private readonly List<RecordedHttpExchange> _exchanges = [];

        public int TotalHttpAttemptCount => _exchanges.Count;
        public IReadOnlyList<RecordedHttpExchange> Exchanges => _exchanges;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uriString = request.RequestUri?.ToString() ?? string.Empty;
            var queryCandidate = TryExtractQueryCandidate(request.RequestUri);
            var globalSequence = _exchanges.Count + 1;

            if (handler is null)
            {
                var notFound = new HttpResponseMessage(HttpStatusCode.NotFound);
                _exchanges.Add(new RecordedHttpExchange(
                    globalSequence,
                    uriString,
                    queryCandidate,
                    (int)notFound.StatusCode,
                    notFound.StatusCode.ToString()));
                return notFound;
            }

            try
            {
                var response = await handler(request, cancellationToken);
                _exchanges.Add(new RecordedHttpExchange(
                    globalSequence,
                    uriString,
                    queryCandidate,
                    (int)response.StatusCode,
                    response.StatusCode.ToString()));
                return response;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _exchanges.Add(new RecordedHttpExchange(
                    globalSequence,
                    uriString,
                    queryCandidate,
                    null,
                    $"Exception:{ex.GetType().Name}"));
                throw;
            }
        }

        private static string TryExtractQueryCandidate(Uri? uri)
        {
            if (uri is null || string.IsNullOrWhiteSpace(uri.Query))
            {
                return string.Empty;
            }

            var query = uri.Query.TrimStart('?');
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && (string.Equals(kv[0], "page", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kv[0], "titles", StringComparison.OrdinalIgnoreCase)))
                {
                    return Uri.UnescapeDataString(kv[1]);
                }
            }

            return string.Empty;
        }
    }

    private sealed record RecordedHttpExchange(
        int GlobalSequence,
        string RequestUri,
        string QueryCandidate,
        int? StatusCode,
        string OutcomeSummary);

    private sealed class InstrumentedWikipediaApiClient(
        IWikipediaApiClient inner,
        LookupStepExecutionTracker stepTracker) : IWikipediaApiClient
    {
        public int InvocationCount { get; private set; }

        public async Task<WikipediaArticleResult> GetArticleAsync(
            WikipediaArticleRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            stepTracker.RecordWikipediaApiClientCall(request.RequestedTitle);
            return await inner.GetArticleAsync(request, cancellationToken);
        }
    }

    private sealed class InstrumentedLexicalLookupProvider(
        ILexicalLookupProvider inner,
        LookupStepExecutionTracker stepTracker) : ILexicalLookupProvider
    {
        public string ProviderName => inner.ProviderName;
        public int ProviderSchemaVersion => inner.ProviderSchemaVersion;

        public async Task<LexicalResult> LookupAsync(
            LexicalLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            stepTracker.RecordProviderInvocationStart(inner.ProviderName, inner.ProviderSchemaVersion, request);
            var result = await inner.LookupAsync(request, cancellationToken);
            stepTracker.RecordProviderInvocationResult(result);
            return result;
        }
    }

    private sealed class InstrumentedProviderResolver(
        IReadOnlyList<ILexicalLookupProvider> providers,
        LookupStepExecutionTracker stepTracker) : ILexicalLookupProviderResolver
    {
        private readonly Dictionary<string, ILexicalLookupProvider> _byName =
            providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);

        public ILexicalLookupProvider? TryResolve(string providerName)
        {
            var resolved = _byName.TryGetValue(providerName, out var provider) ? provider : null;
            stepTracker.BeginStepFromResolver(providerName, resolved?.ProviderSchemaVersion ?? 0, resolved is not null);
            return resolved;
        }

        public ILexicalLookupProvider Resolve(string providerName) =>
            TryResolve(providerName)
            ?? throw new InvalidOperationException($"The lexical provider '{providerName}' is not registered.");
    }

    private sealed class InstrumentedLexicalCacheRepository(ILexicalCacheRepository inner) : ILexicalCacheRepository
    {
        public int ReadAttemptCount { get; private set; }
        public int HitCount { get; private set; }
        public int MissCount { get; private set; }
        public int ReadFailureCount { get; private set; }
        public int WriteAttemptCount { get; private set; }
        public int WriteStoredCount { get; private set; }
        public int WriteFailureCount { get; private set; }

        public LexicalCacheObservationOutcome FirstReadOutcome { get; private set; } = LexicalCacheObservationOutcome.NotConsulted;
        public LexicalCacheObservationOutcome LastReadOutcome { get; private set; } = LexicalCacheObservationOutcome.NotConsulted;

        public event Action<LexicalLookupRequest, string, int, LexicalCacheObservationOutcome, LexicalResult?>? OnReadObserved;
        public event Action<LexicalCacheWriteOutcome>? OnWriteObserved;

        public async Task<LexicalResult?> GetAsync(
            LexicalLookupRequest request,
            string provider,
            int providerSchemaVersion)
        {
            ReadAttemptCount++;
            try
            {
                var cached = await inner.GetAsync(request, provider, providerSchemaVersion);
                var outcome = cached is not null
                    ? LexicalCacheObservationOutcome.Hit
                    : LexicalCacheObservationOutcome.Miss;

                if (outcome == LexicalCacheObservationOutcome.Hit)
                {
                    HitCount++;
                }
                else
                {
                    MissCount++;
                }

                RecordReadOutcome(outcome);
                OnReadObserved?.Invoke(request, provider, providerSchemaVersion, outcome, cached);
                return cached;
            }
            catch
            {
                ReadFailureCount++;
                RecordReadOutcome(LexicalCacheObservationOutcome.ReadFailed);
                OnReadObserved?.Invoke(request, provider, providerSchemaVersion, LexicalCacheObservationOutcome.ReadFailed, null);
                throw;
            }
        }

        public async Task SaveAsync(
            LexicalLookupRequest request,
            LexicalResult result,
            int providerSchemaVersion)
        {
            WriteAttemptCount++;
            try
            {
                await inner.SaveAsync(request, result, providerSchemaVersion);
                WriteStoredCount++;
                OnWriteObserved?.Invoke(LexicalCacheWriteOutcome.Stored);
            }
            catch
            {
                WriteFailureCount++;
                OnWriteObserved?.Invoke(LexicalCacheWriteOutcome.WriteFailed);
                throw;
            }
        }

        private void RecordReadOutcome(LexicalCacheObservationOutcome outcome)
        {
            if (FirstReadOutcome == LexicalCacheObservationOutcome.NotConsulted)
            {
                FirstReadOutcome = outcome;
            }

            LastReadOutcome = outcome;
        }
    }

    private sealed class NoOpLexicalCacheRepository : ILexicalCacheRepository
    {
        public Task<LexicalResult?> GetAsync(
            LexicalLookupRequest request,
            string provider,
            int providerSchemaVersion) => Task.FromResult<LexicalResult?>(null);

        public Task SaveAsync(
            LexicalLookupRequest request,
            LexicalResult result,
            int providerSchemaVersion) => Task.CompletedTask;
    }

    private sealed class NoDelayRecordingAsyncDelay : IAsyncDelay
    {
        private readonly List<double> _delays = [];
        public IReadOnlyList<double> RecordedDelaysMilliseconds => _delays;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _delays.Add(Math.Round(delay.TotalMilliseconds, 2, MidpointRounding.AwayFromZero));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLexicalDiagnosticLog : ILexicalDiagnosticLog
    {
        private readonly List<LexicalDiagnosticEventEvidence> _events = [];
        public IReadOnlyList<LexicalDiagnosticEventEvidence> Events => _events;

        public string ExportPath => string.Empty;

        public void Write(LexicalDiagnosticEvent diagnosticEvent, Exception? exception = null)
        {
            if (diagnosticEvent.Phase.StartsWith("prefetch.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _events.Add(new LexicalDiagnosticEventEvidence(
                Order: _events.Count + 1,
                Phase: diagnosticEvent.Phase,
                NormalizedTerm: diagnosticEvent.NormalizedTerm,
                SourceLanguage: diagnosticEvent.SourceLanguage,
                LookupMode: diagnosticEvent.LookupMode,
                TargetLanguage: diagnosticEvent.TargetLanguage,
                Provider: diagnosticEvent.Provider,
                CacheOutcome: diagnosticEvent.CacheOutcome,
                HttpOutcome: diagnosticEvent.HttpOutcome,
                ParserOutcome: diagnosticEvent.ParserOutcome,
                ExceptionType: exception?.GetType().Name));
        }

        public string ReadReport() => string.Empty;

        public void Clear() => _events.Clear();
    }

    private sealed class LookupStepExecutionTracker
    {
        private readonly InstrumentedOfflineHttpMessageHandler _httpHandler;
        private readonly NoDelayRecordingAsyncDelay _delayRecorder;
        private readonly List<MutableStepState> _steps = [];
        private MutableStepState? _currentStep;

        public LookupStepExecutionTracker(
            InstrumentedOfflineHttpMessageHandler httpHandler,
            NoDelayRecordingAsyncDelay delayRecorder,
            InstrumentedLexicalCacheRepository cacheRepository)
        {
            _httpHandler = httpHandler;
            _delayRecorder = delayRecorder;
            cacheRepository.OnReadObserved += HandleCacheReadObserved;
            cacheRepository.OnWriteObserved += HandleCacheWriteObserved;
        }

        public int TotalProviderInvocationCount { get; private set; }
        public IReadOnlyList<LexicalMeaning> LastWinningPreRankingDirectMeanings { get; private set; } = [];

        public void BeginStepFromResolver(string requestedProviderName, int providerSchemaVersion, bool isRegistered)
        {
            var isFallback = _steps.Count > 0
                && string.Equals(requestedProviderName, WikipediaLookupProvider.Name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_steps[0].ProviderName, WikipediaLookupProvider.Name, StringComparison.OrdinalIgnoreCase);

            var redirectDepth = isFallback
                ? _steps.LastOrDefault(s => !s.IsFallbackStep)?.RedirectDepth ?? 0
                : _steps.Count(s => !s.IsFallbackStep);

            _currentStep = new MutableStepState(
                StepOrder: _steps.Count + 1,
                ProviderName: requestedProviderName,
                ProviderSchemaVersion: providerSchemaVersion,
                IsFallbackStep: isFallback,
                IsProviderRegistered: isRegistered,
                RedirectDepth: redirectDepth,
                HttpStartCount: _httpHandler.Exchanges.Count,
                DelayStartCount: _delayRecorder.RecordedDelaysMilliseconds.Count);
            _steps.Add(_currentStep);
        }

        public void RecordProviderInvocationStart(string providerName, int providerSchemaVersion, LexicalLookupRequest request)
        {
            TotalProviderInvocationCount++;
            if (_currentStep is null)
            {
                BeginStepFromResolver(providerName, providerSchemaVersion, isRegistered: true);
            }

            _currentStep!.ProviderInvoked = true;
            _currentStep.ProviderSchemaVersion = providerSchemaVersion;
            _currentStep.QueriedTerm = request.CanonicalLookupTerm;
            _currentStep.LookupMode = request.LookupMode;
            _currentStep.TargetLanguage = request.TargetLanguage;
            _currentStep.TokenKind = request.TokenKind;
            if (string.IsNullOrWhiteSpace(_currentStep.CacheKey))
            {
                _currentStep.CacheKey = LexicalCacheRepository.CreateCacheKey(request, providerName, providerSchemaVersion);
            }

            _currentStep.HttpStartCount = _httpHandler.Exchanges.Count;
            _currentStep.DelayStartCount = _delayRecorder.RecordedDelaysMilliseconds.Count;
        }

        public void RecordWikipediaApiClientCall(string requestedTitle)
        {
            if (_currentStep is not null)
            {
                _currentStep.WikipediaApiClientInvoked = true;
                if (!string.IsNullOrWhiteSpace(requestedTitle))
                {
                    _currentStep.ExplicitQueryCandidates.Add(requestedTitle);
                }
            }
        }

        public void RecordProviderInvocationResult(LexicalResult providerResult)
        {
            if (_currentStep is null)
            {
                return;
            }

            _currentStep.StepResult = providerResult;
            _currentStep.HttpEndCount = _httpHandler.Exchanges.Count;
            _currentStep.DelayEndCount = _delayRecorder.RecordedDelaysMilliseconds.Count;
            CaptureDirectMeaningsAndRelations(_currentStep, providerResult);
        }

        private void HandleCacheReadObserved(
            LexicalLookupRequest request,
            string provider,
            int providerSchemaVersion,
            LexicalCacheObservationOutcome outcome,
            LexicalResult? cachedResult)
        {
            if (_currentStep is null)
            {
                return;
            }

            _currentStep.QueriedTerm = request.CanonicalLookupTerm;
            _currentStep.LookupMode = request.LookupMode;
            _currentStep.TargetLanguage = request.TargetLanguage;
            _currentStep.TokenKind = request.TokenKind;
            _currentStep.ProviderSchemaVersion = providerSchemaVersion;
            _currentStep.CacheKey = LexicalCacheRepository.CreateCacheKey(request, provider, providerSchemaVersion);
            _currentStep.CacheReadOutcome = outcome;

            if (outcome == LexicalCacheObservationOutcome.Hit && cachedResult is not null)
            {
                _currentStep.StepResult = cachedResult;
                _currentStep.HttpEndCount = _httpHandler.Exchanges.Count;
                _currentStep.DelayEndCount = _delayRecorder.RecordedDelaysMilliseconds.Count;
                CaptureDirectMeaningsAndRelations(_currentStep, cachedResult);
            }
        }

        private void HandleCacheWriteObserved(LexicalCacheWriteOutcome outcome)
        {
            if (_currentStep is not null)
            {
                _currentStep.CacheWriteOutcome = outcome;
            }
        }

        private void CaptureDirectMeaningsAndRelations(MutableStepState step, LexicalResult result)
        {
            var directMeanings = new List<LexicalMeaning>();
            var relations = new List<LexicalFormRelationEvidence>();

            if (result.FormRelations is { Count: > 0 })
            {
                foreach (var rel in result.FormRelations)
                {
                    relations.Add(new LexicalFormRelationEvidence(rel.Kind, rel.BaseLemma, rel.Relationship));
                }
            }

            foreach (var meaning in result.Meanings)
            {
                var relation = ProviderFormRelationPolicy.Resolve(meaning.Definition);
                if (relation is not null)
                {
                    relations.Add(new LexicalFormRelationEvidence(relation.Kind, relation.BaseLemma, relation.Relationship));
                }
                else
                {
                    directMeanings.Add(meaning);
                }
            }

            step.DirectMeanings = directMeanings;
            step.ExtractedFormRelations = relations;

            if (result.Status == LexicalLookupStatus.Success && directMeanings.Count > 0)
            {
                LastWinningPreRankingDirectMeanings = directMeanings;
            }
        }

        public IReadOnlyList<LexicalLookupStepEvidence> BuildSteps()
        {
            var output = new List<LexicalLookupStepEvidence>(_steps.Count);
            foreach (var step in _steps)
            {
                var endCount = step.HttpEndCount >= step.HttpStartCount
                    ? step.HttpEndCount
                    : _httpHandler.Exchanges.Count;

                var stepExchanges = _httpHandler.Exchanges
                    .Skip(step.HttpStartCount)
                    .Take(Math.Max(0, endCount - step.HttpStartCount))
                    .ToArray();

                var httpAttempts = new List<LexicalHttpAttemptEvidence>(stepExchanges.Length);
                var seenUrisInStep = new HashSet<string>(StringComparer.Ordinal);
                var queryCandidates = new List<string>();

                for (var i = 0; i < stepExchanges.Length; i++)
                {
                    var ex = stepExchanges[i];
                    var isRetry = !seenUrisInStep.Add(ex.RequestUri);
                    httpAttempts.Add(new LexicalHttpAttemptEvidence(
                        AttemptOrder: ex.GlobalSequence,
                        StepAttemptIndex: i + 1,
                        ProviderName: step.ProviderName,
                        QueryTerm: ex.QueryCandidate,
                        RequestUri: ex.RequestUri,
                        HttpStatusCode: ex.StatusCode,
                        Outcome: ex.OutcomeSummary,
                        IsRetry: isRetry));

                    if (!string.IsNullOrWhiteSpace(ex.QueryCandidate)
                        && !queryCandidates.Contains(ex.QueryCandidate, StringComparer.Ordinal))
                    {
                        queryCandidates.Add(ex.QueryCandidate);
                    }
                }

                foreach (var explicitCandidate in step.ExplicitQueryCandidates)
                {
                    if (!queryCandidates.Contains(explicitCandidate, StringComparer.Ordinal))
                    {
                        queryCandidates.Add(explicitCandidate);
                    }
                }

                if (queryCandidates.Count == 0 && !string.IsNullOrWhiteSpace(step.QueriedTerm))
                {
                    queryCandidates.Add(step.QueriedTerm);
                }

                var caseFallbackTriggered = string.Equals(step.ProviderName, WiktionaryLookupProvider.Name, StringComparison.OrdinalIgnoreCase)
                    && queryCandidates.Count > 1;

                var cacheWriteOutcome = step.CacheWriteOutcome;
                if (cacheWriteOutcome == LexicalCacheWriteOutcome.NotAttempted
                    && step.ProviderInvoked
                    && step.StepResult is { Status: not LexicalLookupStatus.Success })
                {
                    cacheWriteOutcome = LexicalCacheWriteOutcome.SkippedNotCacheable;
                }

                var rawStatus = step.StepResult?.Status;
                var rawErrorCode = step.StepResult?.ErrorCode;
                var stepStatus = rawStatus
                    ?? (step.IsProviderRegistered ? LexicalLookupStatus.NotFound : LexicalLookupStatus.PermanentFailure);
                var stepErrorCode = rawErrorCode
                    ?? (step.IsProviderRegistered ? null : "provider-not-registered");

                output.Add(new LexicalLookupStepEvidence(
                    StepOrder: step.StepOrder,
                    ProviderName: step.ProviderName,
                    ProviderSchemaVersion: step.ProviderSchemaVersion,
                    QueriedTerm: step.QueriedTerm,
                    LookupMode: step.LookupMode,
                    TargetLanguage: step.TargetLanguage,
                    TokenKind: step.TokenKind,
                    RedirectDepth: step.RedirectDepth,
                    IsFallbackStep: step.IsFallbackStep,
                    CacheKey: step.CacheKey,
                    CacheOutcome: step.CacheReadOutcome,
                    CacheWriteOutcome: cacheWriteOutcome,
                    ProviderInvoked: step.ProviderInvoked,
                    WikipediaApiClientInvoked: step.WikipediaApiClientInvoked,
                    QueryCandidates: queryCandidates,
                    CaseFallbackTriggered: caseFallbackTriggered,
                    HttpAttemptCount: httpAttempts.Count,
                    HttpRetryCount: httpAttempts.Count(a => a.IsRetry),
                    HttpAttempts: httpAttempts,
                    RawProviderStatus: rawStatus,
                    RawProviderErrorCode: rawErrorCode,
                    StepStatus: stepStatus,
                    StepErrorCode: stepErrorCode,
                    ExtractedDirectMeaningCount: step.DirectMeanings.Count,
                    ExtractedFormRelations: step.ExtractedFormRelations,
                    PreRankingMeanings: BuildPreRankingMeaningSnapshots(step.DirectMeanings)));
            }

            return output;
        }

        private sealed class MutableStepState(
            int StepOrder,
            string ProviderName,
            int ProviderSchemaVersion,
            bool IsFallbackStep,
            bool IsProviderRegistered,
            int RedirectDepth,
            int HttpStartCount,
            int DelayStartCount)
        {
            public int StepOrder { get; } = StepOrder;
            public string ProviderName { get; } = ProviderName;
            public int ProviderSchemaVersion { get; set; } = ProviderSchemaVersion;
            public bool IsFallbackStep { get; } = IsFallbackStep;
            public bool IsProviderRegistered { get; } = IsProviderRegistered;
            public int RedirectDepth { get; } = RedirectDepth;
            public int HttpStartCount { get; set; } = HttpStartCount;
            public int HttpEndCount { get; set; } = -1;
            public int DelayStartCount { get; set; } = DelayStartCount;
            public int DelayEndCount { get; set; } = -1;

            public string QueriedTerm { get; set; } = string.Empty;
            public LexicalLookupMode LookupMode { get; set; } = LexicalLookupMode.Definition;
            public string? TargetLanguage { get; set; }
            public TokenKind TokenKind { get; set; } = TokenKind.Word;
            public string CacheKey { get; set; } = string.Empty;
            public LexicalCacheObservationOutcome CacheReadOutcome { get; set; } = LexicalCacheObservationOutcome.NotConsulted;
            public LexicalCacheWriteOutcome CacheWriteOutcome { get; set; } = LexicalCacheWriteOutcome.NotAttempted;
            public bool ProviderInvoked { get; set; }
            public bool WikipediaApiClientInvoked { get; set; }
            public LexicalResult? StepResult { get; set; }
            public List<LexicalMeaning> DirectMeanings { get; set; } = [];
            public List<LexicalFormRelationEvidence> ExtractedFormRelations { get; set; } = [];
            public List<string> ExplicitQueryCandidates { get; set; } = [];
        }
    }
}
