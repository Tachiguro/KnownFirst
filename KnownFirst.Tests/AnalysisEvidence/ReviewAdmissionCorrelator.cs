using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;

namespace KnownFirst.Tests.AnalysisEvidence;

public static class ReviewAdmissionCorrelator
{
    public static async Task<ReviewAdmissionEvidence> CorrelateAsync(
        IKnownFirstDatabase database,
        TextReviewService service,
        ImportTextRequest request,
        TextAnalyzer? analyzer = null,
        bool enableGermanCompoundDecomposition = false,
        IGermanLexicon? germanLexicon = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(request);

        analyzer ??= new TextAnalyzer();
        var analyzerEvidence = AnalysisEvidenceCollector.Collect(
            request.Content,
            sourceLanguage: request.TextLanguage,
            enableGermanCompoundDecomposition: enableGermanCompoundDecomposition,
            germanLexicon: germanLexicon,
            analyzer: analyzer);

        var preWords = await database.ReadAsync(async connection =>
        {
            return await connection.Table<WordEntity>()
                .Where(w => w.Language == request.TextLanguage)
                .ToListAsync();
        });

        var preImportVocabulary = preWords
            .OrderBy(w => w.NormalizedTerm, StringComparer.Ordinal)
            .Select(w => new PreImportVocabularyEntry(
                w.NormalizedTerm,
                w.CanonicalTerm,
                w.Status,
                w.TotalOccurrenceCount,
                w.DocumentCount,
                w.Id))
            .ToArray();

        var preImportByNormalized = preImportVocabulary.ToDictionary(w => w.Identity, StringComparer.Ordinal);

        var importResult = await service.ImportAsync(request);

        if (importResult.Outcome == ImportAnalysisOutcome.Accepted)
        {
            var (reviewCandidates, postWords, docOccurrences) = await database.ReadAsync(async connection =>
            {
                var candidates = await connection.Table<ReviewCandidateEntity>()
                    .Where(rc => rc.SessionId == importResult.SessionId)
                    .OrderBy(rc => rc.Order)
                    .ToListAsync();

                var words = await connection.Table<WordEntity>()
                    .Where(w => w.Language == request.TextLanguage)
                    .ToListAsync();

                var occurrences = await connection.Table<WordOccurrenceEntity>()
                    .Where(o => o.DocumentId == importResult.DocumentId)
                    .OrderBy(o => o.Order)
                    .ToListAsync();

                return (candidates, words, occurrences);
            });

            var postWordsById = postWords.ToDictionary(w => w.Id);
            var postWordsByNormalized = postWords.ToDictionary(w => w.NormalizedTerm, StringComparer.Ordinal);

            var persistedReviewCandidates = reviewCandidates
                .OrderBy(rc => rc.Order)
                .Select(rc =>
                {
                    var word = postWordsById[rc.WordId];
                    var wordOccurrencesCount = docOccurrences.Count(o => o.WordId == rc.WordId);
                    return new PersistedReviewCandidateEvidence(
                        rc.Order,
                        rc.WordId,
                        word.NormalizedTerm,
                        word.CanonicalTerm,
                        word.Status,
                        rc.PreviousWordStatus,
                        rc.WasWordCreatedForSession,
                        wordOccurrencesCount);
                })
                .ToArray();

            var persistedReviewByIdentity = persistedReviewCandidates.ToDictionary(rc => rc.Identity, StringComparer.Ordinal);

            var candidateAdmissions = analyzerEvidence.Candidates
                .Select(candidate =>
                {
                    preImportByNormalized.TryGetValue(candidate.Identity, out var preEntry);
                    persistedReviewByIdentity.TryGetValue(candidate.Identity, out var persistedReview);
                    postWordsByNormalized.TryGetValue(candidate.Identity, out var postWord);

                    var contributionCount = postWord is null
                        ? 0
                        : docOccurrences.Count(o => o.WordId == postWord.Id);

                    ReviewAdmissionDisposition disposition;
                    if (preEntry is null)
                    {
                        disposition = ReviewAdmissionDisposition.AdmittedNewWord;
                    }
                    else if (preEntry.Status == WordStatus.Unreviewed)
                    {
                        disposition = ReviewAdmissionDisposition.AdmittedExistingUnreviewed;
                    }
                    else if (preEntry.Status == WordStatus.UnknownBacklog)
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingUnknownBacklog;
                    }
                    else if (preEntry.Status == WordStatus.Known)
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingKnown;
                    }
                    else if (preEntry.Status == WordStatus.Ignored)
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingIgnored;
                    }
                    else
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingEstablishedStatus;
                    }

                    return new CandidateAdmissionEvidence(
                        candidate.Identity,
                        candidate.CanonicalTerm,
                        preEntry?.Status,
                        disposition,
                        persistedReview?.Order,
                        postWord?.Id ?? preEntry?.WordId,
                        persistedReview?.WasWordCreatedForSession ?? false,
                        preEntry?.TotalOccurrenceCount ?? 0,
                        postWord?.TotalOccurrenceCount ?? 0,
                        preEntry?.DocumentCount ?? 0,
                        postWord?.DocumentCount ?? 0,
                        contributionCount);
                })
                .ToArray();

            return new ReviewAdmissionEvidence(
                analyzerEvidence,
                importResult,
                preImportVocabulary,
                candidateAdmissions,
                persistedReviewCandidates);
        }
        else if (importResult.Outcome == ImportAnalysisOutcome.NoNewVocabulary)
        {
            var postWords = await database.ReadAsync(async connection =>
            {
                return await connection.Table<WordEntity>()
                    .Where(w => w.Language == request.TextLanguage)
                    .ToListAsync();
            });
            var postWordsByNormalized = postWords.ToDictionary(w => w.NormalizedTerm, StringComparer.Ordinal);

            var candidateAdmissions = analyzerEvidence.Candidates
                .Select(candidate =>
                {
                    preImportByNormalized.TryGetValue(candidate.Identity, out var preEntry);
                    postWordsByNormalized.TryGetValue(candidate.Identity, out var postWord);

                    ReviewAdmissionDisposition disposition;
                    if (preEntry is null)
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingEstablishedStatus;
                    }
                    else if (preEntry.Status == WordStatus.UnknownBacklog)
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingUnknownBacklog;
                    }
                    else if (preEntry.Status == WordStatus.Known)
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingKnown;
                    }
                    else if (preEntry.Status == WordStatus.Ignored)
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingIgnored;
                    }
                    else
                    {
                        disposition = ReviewAdmissionDisposition.NotAdmittedExistingEstablishedStatus;
                    }

                    return new CandidateAdmissionEvidence(
                        candidate.Identity,
                        candidate.CanonicalTerm,
                        preEntry?.Status,
                        disposition,
                        PersistedReviewOrder: null,
                        PersistedWordId: postWord?.Id ?? preEntry?.WordId,
                        WasWordCreatedForSession: false,
                        TotalOccurrenceCountBefore: preEntry?.TotalOccurrenceCount ?? 0,
                        TotalOccurrenceCountAfter: postWord?.TotalOccurrenceCount ?? preEntry?.TotalOccurrenceCount ?? 0,
                        DocumentCountBefore: preEntry?.DocumentCount ?? 0,
                        DocumentCountAfter: postWord?.DocumentCount ?? preEntry?.DocumentCount ?? 0,
                        OccurrenceContributionCount: 0);
                })
                .ToArray();

            return new ReviewAdmissionEvidence(
                analyzerEvidence,
                importResult,
                preImportVocabulary,
                candidateAdmissions,
                Array.Empty<PersistedReviewCandidateEvidence>());
        }
        else
        {
            return new ReviewAdmissionEvidence(
                analyzerEvidence,
                importResult,
                preImportVocabulary,
                Array.Empty<CandidateAdmissionEvidence>(),
                Array.Empty<PersistedReviewCandidateEvidence>());
        }
    }
}
