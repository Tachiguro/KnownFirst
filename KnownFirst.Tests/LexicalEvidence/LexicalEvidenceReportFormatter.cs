using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KnownFirst.Tests.LexicalEvidence;

public static class LexicalEvidenceReportFormatter
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new(LexicalEvidenceJsonSerializerContext.Default.Options)
    {
        WriteIndented = true,
        NewLine = "\n"
    };

    private static readonly LexicalEvidenceJsonSerializerContext SerializerContext =
        new(IndentedJsonOptions);

    public static string FormatJson(LexicalEvidenceReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = JsonSerializer.Serialize(document, SerializerContext.LexicalEvidenceReportDocument);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    public static LexicalEvidenceReportDocument DeserializeJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize(json, SerializerContext.LexicalEvidenceReportDocument)
            ?? throw new InvalidOperationException("Failed to deserialize LexicalEvidenceReportDocument.");
    }

    public static string FormatCandidateJson(LexicalCandidateEvidence candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var json = JsonSerializer.Serialize(candidate, SerializerContext.LexicalCandidateEvidence);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    public static LexicalCandidateEvidence DeserializeCandidateJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize(json, SerializerContext.LexicalCandidateEvidence)
            ?? throw new InvalidOperationException("Failed to deserialize LexicalCandidateEvidence.");
    }

    public static string FormatText(LexicalEvidenceReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sb = new StringBuilder();
        AppendLine(sb, "=== LEXICAL LOOKUP EVIDENCE REPORT ===");
        AppendLine(sb, $"SuiteId: {EscapeString(document.SuiteId)}");
        AppendLine(sb, $"Description: {EscapeString(document.Description)}");
        AppendLine(sb, string.Create(CultureInfo.InvariantCulture, $"CapturedAtUtc: {document.CapturedAtUtc:O}"));
        AppendLine(sb, string.Empty);

        var s = document.Summary;
        AppendLine(sb, "--- QUANTITATIVE SUMMARY & EXPLICIT DENOMINATORS ---");
        AppendLine(sb, $"TotalCandidateCount: {s.TotalCandidateCount}");
        AppendLine(sb, $"LanguageBreakdown: de={s.GermanCandidateCount}/{s.TotalCandidateCount} | en={s.EnglishCandidateCount}/{s.TotalCandidateCount}");
        AppendLine(sb, $"ModeBreakdown: Definition={s.DefinitionModeCandidateCount}/{s.TotalCandidateCount} | Translation={s.TranslationModeCandidateCount}/{s.TotalCandidateCount} | DefinitionAndTranslation={s.DefinitionAndTranslationModeCandidateCount}/{s.TotalCandidateCount}");
        AppendLine(sb, $"ExecutionRealism: PreparationServiceLookup={s.PreparationServiceCandidateCount}/{s.TotalCandidateCount} | DirectEnrichmentService={s.DirectEnrichmentCandidateCount}/{s.TotalCandidateCount}");
        AppendLine(sb, $"TransportRealism: MeasuredOfflineHttpTransport={s.MeasuredHttpTransportCandidateCount}/{s.TotalCandidateCount} | SimulatedWikipediaApiClient={s.SimulatedWikipediaApiClientCandidateCount}/{s.TotalCandidateCount} | SimulatedProviderBoundary={s.SimulatedProviderBoundaryCandidateCount}/{s.TotalCandidateCount}");
        AppendLine(sb, $"DistinctAttemptCounters: TotalPreparationLookupAttemptCount={s.TotalPreparationLookupAttemptCount} | TotalProviderInvocationCount={s.TotalProviderInvocationCount} | TotalWikipediaApiClientInvocationCount={s.TotalWikipediaApiClientInvocationCount} | TotalHttpAttemptCount={s.TotalHttpAttemptCount} | TotalHttpRetryCount={s.TotalHttpRetryCount}");
        AppendLine(sb, $"CacheCounters: ReadAttempts={s.TotalCacheReadAttemptCount} | Hits={s.TotalCacheHitCount} | Misses={s.TotalCacheMissCount} | ReadFailures={s.TotalCacheReadFailureCount} | WriteAttempts={s.TotalCacheWriteAttemptCount} | WriteStored={s.TotalCacheWriteStoredCount} | WriteSkipped={s.TotalCacheWriteSkippedCount} | WriteFailures={s.TotalCacheWriteFailureCount}");
        AppendLine(sb, $"StatusTotals: Success={s.SuccessCount}/{s.TotalCandidateCount} | NotFound={s.NotFoundCount}/{s.TotalCandidateCount} | ParseFailure={s.ParseFailureCount}/{s.TotalCandidateCount} | TransientFailure={s.TransientFailureCount}/{s.TotalCandidateCount} | PermanentFailure={s.PermanentFailureCount}/{s.TotalCandidateCount}");
        AppendLine(sb, $"RecoveryTotals: CaseFallback={s.GermanCaseFallbackSucceededCount}/{s.GermanCaseFallbackAttemptedCount} (Eligible={s.GermanCaseFallbackEligibleCandidateCount}) | LemmaRedirect={s.LemmaRedirectSucceededCount}/{s.LemmaRedirectAttemptedCount} | WikipediaFallback={s.WikipediaFallbackSucceededCount}/{s.WikipediaFallbackTriggeredCandidateCount} (Eligible={s.WikipediaFallbackEligibleCandidateCount}) | MeaningReordered={s.MeaningOrderChangedByRankingCount}/{s.MultiMeaningSuccessCandidateCount}");
        AppendLine(sb, string.Empty);

        AppendLine(sb, $"--- EXPLICIT RATES ({s.RateMetrics.Count}) ---");
        foreach (var r in s.RateMetrics)
        {
            var pctText = r.Percentage?.ToString("F2", CultureInfo.InvariantCulture) ?? "N/A";
            AppendLine(sb, $"  [{EscapeString(r.MetricCode)}] {r.Numerator}/{r.Denominator} ({EscapeString(r.DenominatorName)}) = {pctText}% | {EscapeString(r.Description)}");
        }

        AppendLine(sb, string.Empty);
        AppendLine(sb, $"--- STATUS BREAKDOWN ({s.StatusBreakdown.Count}) ---");
        foreach (var sbItem in s.StatusBreakdown)
        {
            var pctText = sbItem.Percentage?.ToString("F2", CultureInfo.InvariantCulture) ?? "N/A";
            AppendLine(sb, $"  [{sbItem.Status}] {sbItem.Count}/{sbItem.Denominator} = {pctText}%");
        }

        AppendLine(sb, string.Empty);
        AppendLine(sb, $"--- OUTCOME CLASSIFICATION BREAKDOWN ({s.ClassificationBreakdown.Count}) ---");
        foreach (var cbItem in s.ClassificationBreakdown)
        {
            var pctText = cbItem.Percentage?.ToString("F2", CultureInfo.InvariantCulture) ?? "N/A";
            AppendLine(sb, $"  [{cbItem.Classification}] {cbItem.Count}/{cbItem.Denominator} = {pctText}%");
        }

        AppendLine(sb, string.Empty);
        AppendLine(sb, $"--- FAILED TERMS ({s.FailedTerms.Count}) ---");
        foreach (var f in s.FailedTerms)
        {
            AppendLine(sb, $"  [{EscapeString(f.CaseId)}] lang={EscapeString(f.SourceLanguage)} mode={f.LookupMode} target={EscapeString(f.TargetLanguage ?? "none")} surface=\"{EscapeString(f.DisplayedSurfaceForm)}\" canonical=\"{EscapeString(f.CanonicalLookupTerm)}\" finalLemma=\"{EscapeString(f.FinalQueriedLemma)}\" status={f.FinalStatus} code={EscapeString(f.FinalErrorCode ?? "none")} class={f.OutcomeClassification} providers=[{string.Join(" -> ", f.OrderedProviderChain.Select(EscapeString))}] queries=[{string.Join(", ", f.OrderedQueryCandidates.Select(EscapeString))}] providerCalls={f.ProviderInvocationCount} httpAttempts={f.HttpAttemptCount}");
        }

        AppendLine(sb, string.Empty);
        AppendLine(sb, $"--- PROBLEMATIC & RECOVERY TERMS ({s.ProblematicTerms.Count}) ---");
        foreach (var p in s.ProblematicTerms)
        {
            AppendLine(sb, $"  [{EscapeString(p.CaseId)}] kind={p.ProblemKind} lang={EscapeString(p.SourceLanguage)} mode={p.LookupMode} surface=\"{EscapeString(p.DisplayedSurfaceForm)}\" canonical=\"{EscapeString(p.CanonicalLookupTerm)}\" finalLemma=\"{EscapeString(p.FinalQueriedLemma)}\" status={p.FinalStatus} code={EscapeString(p.FinalErrorCode ?? "none")} class={p.OutcomeClassification} note=\"{EscapeString(p.Explanation)}\"");
        }

        AppendLine(sb, string.Empty);
        AppendLine(sb, $"--- PER-CANDIDATE EVIDENCE TRACES ({document.Candidates.Count}) ---");
        foreach (var candidate in document.Candidates)
        {
            AppendCandidateSection(sb, candidate);
            AppendLine(sb, string.Empty);
        }

        return sb.ToString();
    }

    public static string FormatCandidateText(LexicalCandidateEvidence candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var sb = new StringBuilder();
        AppendCandidateSection(sb, candidate);
        return sb.ToString();
    }

    public static (string TextPath, string JsonPath) WriteReportFiles(
        LexicalEvidenceReportDocument document,
        string outputDirectory,
        string baseFileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);

        Directory.CreateDirectory(outputDirectory);

        var textPath = Path.Combine(outputDirectory, $"{baseFileName}.txt");
        var jsonPath = Path.Combine(outputDirectory, $"{baseFileName}.json");

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(textPath, FormatText(document), utf8NoBom);
        File.WriteAllText(jsonPath, FormatJson(document), utf8NoBom);

        return (textPath, jsonPath);
    }

    private static void AppendCandidateSection(StringBuilder sb, LexicalCandidateEvidence c)
    {
        AppendLine(sb, $"=== CANDIDATE [{EscapeString(c.CaseId)}] ===");
        AppendLine(sb, $"Description: {EscapeString(c.Description)}");
        AppendLine(sb, $"ExecutionMode: {c.ExecutionMode} | TransportRealism: {c.TransportRealism}");
        AppendLine(sb, $"Request: lang={EscapeString(c.SourceLanguage)} explanation={EscapeString(c.ExplanationLanguage)} mode={c.LookupMode} target={EscapeString(c.TargetLanguage ?? "none")} kind={c.TokenKind} provider={EscapeString(c.RequestedProvider)}");
        AppendLine(sb, $"Terms: surface=\"{EscapeString(c.DisplayedSurfaceForm)}\" vocabCanonical=\"{EscapeString(c.VocabularyCanonicalTerm)}\" lookupCanonical=\"{EscapeString(c.CanonicalLookupTerm)}\" finalQueried=\"{EscapeString(c.FinalQueriedLemma)}\" finalDisplay=\"{EscapeString(c.FinalDisplayTerm)}\" encounteredForm=\"{EscapeString(c.EncounteredSurfaceForm ?? "none")}\" relation=\"{EscapeString(c.GrammaticalRelationship ?? "none")}\" redirectDepth={c.FinalRedirectDepth}");
        AppendLine(sb, $"Counters: prepAttempts={c.PreparationLookupAttemptCount?.ToString(CultureInfo.InvariantCulture) ?? "null"} providerCalls={c.ProviderInvocationCount} wikiClientCalls={c.WikipediaApiClientInvocationCount} httpAttempts={c.HttpAttemptCount} httpRetries={c.HttpRetryCount} retryDelaysMs=[{string.Join(", ", c.RecordedRetryDelayMilliseconds.Select(d => d.ToString("F0", CultureInfo.InvariantCulture)))}]");
        AppendLine(sb, $"Cache: primary={c.PrimaryCacheOutcome} effective={c.EffectiveCacheOutcome} fromCache={c.FinalResultFromCache} reads={c.CacheReadAttemptCount} hits={c.CacheHitCount} misses={c.CacheMissCount} readFails={c.CacheReadFailureCount} writes={c.CacheWriteAttemptCount} stored={c.CacheWriteStoredCount} skipped={c.CacheWriteSkippedCount} writeFails={c.CacheWriteFailureCount}");
        AppendLine(sb, $"Fallbacks: caseFallback={c.CaseFallbackAttempted}/{c.CaseFallbackSucceeded} lemmaRedirect={c.LemmaRedirectAttempted}/{c.LemmaRedirectSucceeded} wikiEligible={c.WikipediaFallbackEligible} wikiTriggered={c.WikipediaFallbackTriggered} wikiSucceeded={c.WikipediaFallbackSucceeded} wikiDisposition={EscapeString(c.WikipediaFallbackDisposition)}");
        AppendLine(sb, $"OrderedProviderChain: [{string.Join(" -> ", c.OrderedProviderChain.Select(EscapeString))}]");
        AppendLine(sb, $"OrderedQueryCandidates: [{string.Join(", ", c.OrderedQueryCandidates.Select(EscapeString))}]");
        AppendLine(sb, $"FinalOutcome: status={c.FinalStatus} code={EscapeString(c.FinalErrorCode ?? "none")} class={c.OutcomeClassification} provider={EscapeString(c.FinalProviderName)} project={EscapeString(c.FinalSourceProject)} pageTitle=\"{EscapeString(c.FinalPageTitle ?? "none")}\" revId={c.FinalRevisionId?.ToString(CultureInfo.InvariantCulture) ?? "null"} usable={c.HasUsableData} reference={c.HasReferenceData} canRetry={c.CanRetry}");

        AppendLine(sb, $"LookupSteps ({c.LookupSteps.Count}):");
        foreach (var step in c.LookupSteps)
        {
            AppendLine(sb, $"  Step[{step.StepOrder}] provider={EscapeString(step.ProviderName)} fallback={step.IsFallbackStep} depth={step.RedirectDepth} term=\"{EscapeString(step.QueriedTerm)}\" candidates=[{string.Join(", ", step.QueryCandidates.Select(EscapeString))}] cacheRead={step.CacheOutcome} cacheWrite={step.CacheWriteOutcome} providerInvoked={step.ProviderInvoked} wikiClientInvoked={step.WikipediaApiClientInvoked} caseFallback={step.CaseFallbackTriggered} httpAttempts={step.HttpAttemptCount} httpRetries={step.HttpRetryCount} status={step.StepStatus} code={EscapeString(step.StepErrorCode ?? "none")} directMeanings={step.ExtractedDirectMeaningCount} relations=[{string.Join("; ", step.ExtractedFormRelations.Select(r => $"{r.Kind}:{EscapeString(r.Relationship)}->{EscapeString(r.BaseLemma)}"))}]");
            foreach (var http in step.HttpAttempts)
            {
                AppendLine(sb, $"    Http[{http.AttemptOrder}/{http.StepAttemptIndex}] provider={EscapeString(http.ProviderName)} query=\"{EscapeString(http.QueryTerm)}\" status={http.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "none"} outcome={EscapeString(http.Outcome)} retry={http.IsRetry} uri=\"{EscapeString(http.RequestUri)}\"");
            }
        }

        AppendLine(sb, $"MeaningOrderChangedByRanking: {c.MeaningOrderChangedByRanking}");
        AppendLine(sb, $"PreRankingMeanings ({c.PreRankingMeanings.Count}):");
        foreach (var m in c.PreRankingMeanings)
        {
            AppendLine(sb, $"  Pre[{m.OrderIndex}] origIdx={m.OriginalProviderIndex} id=\"{EscapeString(m.MeaningId)}\" pos=\"{EscapeString(m.PartOfSpeech ?? "none")}\" tier={m.RelevanceTier} demotionLabel=\"{EscapeString(m.TriggeringDemotionLabel ?? "none")}\" labels=[{string.Join(", ", m.UsageLabels.Select(EscapeString))}] def=\"{EscapeString(m.Definition)}\" trans=\"{EscapeString(m.Translation ?? "none")}\"");
        }

        AppendLine(sb, $"RankedMeanings ({c.RankedMeanings.Count}):");
        foreach (var m in c.RankedMeanings)
        {
            AppendLine(sb, $"  Rank[{m.OrderIndex}] origIdx={m.OriginalProviderIndex} id=\"{EscapeString(m.MeaningId)}\" pos=\"{EscapeString(m.PartOfSpeech ?? "none")}\" tier={m.RelevanceTier} demotionLabel=\"{EscapeString(m.TriggeringDemotionLabel ?? "none")}\" labels=[{string.Join(", ", m.UsageLabels.Select(EscapeString))}] def=\"{EscapeString(m.Definition)}\" trans=\"{EscapeString(m.Translation ?? "none")}\"");
        }
    }

    private static void AppendLine(StringBuilder sb, string line)
    {
        sb.Append(line);
        sb.Append('\n');
    }

    private static string EscapeString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
