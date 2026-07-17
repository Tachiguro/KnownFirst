using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SQLite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnownFirst.Services;

public sealed class TextReviewService(
    IKnownFirstDatabase database,
    TextAnalyzer analyzer,
    ILogger<TextReviewService>? logger = null) : ITextReviewService
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.Ordinal)
    {
        "de",
        "en"
    };

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ILogger<TextReviewService> _logger =
        logger ?? NullLogger<TextReviewService>.Instance;

    public async Task<ImportAnalysisResult> ImportAsync(ImportTextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug(
            "Text import analysis started. TitleLength = {TitleLength}, content length = {ContentLength}, text language = {TextLanguage}, explanation language = {ExplanationLanguage}, lookup mode = {LookupMode}",
            request.Title.Length,
            request.Content.Length,
            request.TextLanguage,
            request.ExplanationLanguage,
            request.LookupMode);

        try
        {
            ValidateImport(request);
            var analysis = analyzer.Analyze(request.Content, request.TextLanguage);
            var contentFingerprint = CreateContentFingerprint(request.Content);
            _logger.LogDebug(
                "Text analysis completed. Fingerprint = {ContentFingerprint}, sentence count = {SentenceCount}, candidate count = {CandidateCount}, occurrence count = {OccurrenceCount}, duration milliseconds = {DurationMilliseconds}",
                contentFingerprint,
                analysis.Sentences.Count,
                analysis.Candidates.Count,
                analysis.OccurrenceCount,
                stopwatch.ElapsedMilliseconds);

            await _operationGate.WaitAsync();
            try
            {
                var result = await database.RunInTransactionAsync(connection =>
                    CreateImport(connection, request, analysis, contentFingerprint));
                _logger.LogInformation(
                    "Text import transaction completed. Outcome = {ImportOutcome}, document ID = {DocumentId}, session ID = {SessionId}, candidate count = {CandidateCount}, duration milliseconds = {DurationMilliseconds}",
                    result.Outcome,
                    result.DocumentId,
                    result.SessionId,
                    result.CandidateCount,
                    stopwatch.ElapsedMilliseconds);
                return result;
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Text import analysis failed. ContentLength = {ContentLength}, duration milliseconds = {DurationMilliseconds}",
                request.Content.Length,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public Task<ActiveReviewSummary?> GetActiveReviewAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<ReviewSessionEntity>()
            .Where(candidate => candidate.Status == ReviewSessionStatus.Active)
            .FirstOrDefaultAsync();

        if (session is null)
        {
            return null;
        }

        var document = await connection.FindAsync<DocumentEntity>(session.DocumentId);
        return document is null ? null : CreateActiveSummary(session, document);
    });

    public Task<ReviewCandidateDetails?> GetCurrentCandidateAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<ReviewSessionEntity>()
            .Where(candidate => candidate.Status == ReviewSessionStatus.Active)
            .FirstOrDefaultAsync();

        if (session is null)
        {
            return null;
        }

        var candidate = await connection.Table<ReviewCandidateEntity>()
            .Where(item => item.SessionId == session.Id && item.Status == WordStatus.Unreviewed)
            .OrderBy(item => item.Order)
            .FirstOrDefaultAsync();

        if (candidate is null)
        {
            return null;
        }

        var word = await connection.FindAsync<WordEntity>(candidate.WordId)
            ?? throw new InvalidOperationException("The active review candidate has no word record.");
        var document = await connection.FindAsync<DocumentEntity>(session.DocumentId)
            ?? throw new InvalidOperationException("The active review session has no document.");
        var forms = await connection.Table<WordFormEntity>()
            .Where(form => form.WordId == word.Id)
            .OrderBy(form => form.Id)
            .ToListAsync();
        var occurrences = await connection.Table<WordOccurrenceEntity>()
            .Where(occurrence => occurrence.WordId == word.Id && occurrence.DocumentId == document.Id)
            .OrderBy(occurrence => occurrence.Order)
            .ToListAsync();
        var sentenceEntities = await connection.Table<SentenceSpanEntity>()
            .Where(sentence => sentence.DocumentId == document.Id)
            .OrderBy(sentence => sentence.Order)
            .ToListAsync();
        var sentencesById = sentenceEntities.ToDictionary(sentence => sentence.Id);
        var sentenceSpans = sentenceEntities.Select(sentence => new TextSpan(
                sentence.StartPosition,
                sentence.Length,
                sentence.Order))
            .ToArray();
        var analyzedOccurrences = occurrences.Select(occurrence =>
        {
            var sentence = sentencesById.GetValueOrDefault(occurrence.SentenceSpanId)
                ?? throw new InvalidOperationException("A review occurrence has no sentence span.");
            return new TokenOccurrence(
                occurrence.SurfaceForm,
                word.NormalizedTerm,
                word.TokenKind,
                occurrence.StartPosition,
                occurrence.Length,
                occurrence.Order,
                sentence.Order,
                word.CanonicalTerm,
                occurrence.TechnicalFamily,
                occurrence.TechnicalInstanceYear,
                string.IsNullOrWhiteSpace(occurrence.TechnicalInstanceIdentifier)
                    ? null
                    : occurrence.TechnicalInstanceIdentifier,
                string.IsNullOrWhiteSpace(occurrence.TechnicalVariant)
                    ? null
                    : occurrence.TechnicalVariant);
        }).ToArray();
        var contexts = ContextSelectionPolicy.Select(
                document.Content,
                sentenceSpans,
                analyzedOccurrences,
                word.NormalizedTerm)
            .Where(context => context.IsSelected)
            .Select(context =>
            {
                var relativeStart = context.OccurrenceStartPosition - context.SentenceStartPosition;
                return new ReviewContext(
                    context.OccurrenceStartPosition,
                    context.OccurrenceLength,
                    context.SentenceText[..relativeStart],
                    context.Target,
                    context.SentenceText[(relativeStart + context.OccurrenceLength)..]);
            })
            .ToArray();

        return new ReviewCandidateDetails(
            document.Id,
            word.Id,
            word.NormalizedTerm,
            word.CanonicalTerm,
            word.TokenKind,
            EncounteredFormPolicy.Deduplicate(
                word.TokenKind,
                forms.Select(form => form.SurfaceForm)),
            occurrences.Count,
            contexts,
            session.ReviewedCount,
            session.TotalCandidates,
            session.ReviewedCount > 0);
    });

    public async Task<ReviewDecisionResult> DecideAsync(int wordId, WordStatus status)
    {
        if (status is not (WordStatus.Known or WordStatus.UnknownBacklog or WordStatus.Ignored))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        await _operationGate.WaitAsync();
        try
        {
            var result = await database.RunInTransactionAsync(connection =>
                PersistDecision(connection, wordId, status));
            _logger.LogInformation(
                "Word review decision persisted. WordId = {WordId}, status = {WordStatus}, review complete = {ReviewComplete}",
                wordId,
                status,
                result.IsComplete);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> UndoPreviousDecisionAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            var undone = await database.RunInTransactionAsync(UndoPreviousDecision);
            _logger.LogInformation("Previous word review decision undo completed. Undone = {Undone}", undone);
            return undone;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DiscardActiveImportAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                DiscardActiveImport(connection);
                return true;
            });
            _logger.LogInformation("The active text import was discarded.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<CompletedReviewSummary?> GetLatestCompletedReviewAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<ReviewSessionEntity>()
            .Where(candidate => candidate.Status == ReviewSessionStatus.Completed)
            .OrderByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync();

        if (session is null)
        {
            return null;
        }

        var document = await connection.FindAsync<DocumentEntity>(session.DocumentId);
        return document is null ? null : CreateCompletedSummary(session, document.Title);
    });

#if DEBUG
    public Task<DocumentAnalysisReport?> GetAnalysisReportAsync(int? documentId = null) =>
        database.ReadAsync(async connection =>
        {
            DocumentEntity? document;
            if (documentId.HasValue)
            {
                document = await connection.FindAsync<DocumentEntity>(documentId.Value);
            }
            else
            {
                var activeSession = await connection.Table<ReviewSessionEntity>()
                    .Where(session => session.Status == ReviewSessionStatus.Active)
                    .FirstOrDefaultAsync();
                document = activeSession is not null
                    ? await connection.FindAsync<DocumentEntity>(activeSession.DocumentId)
                    : await connection.Table<DocumentEntity>()
                        .OrderByDescending(item => item.Id)
                        .FirstOrDefaultAsync();
            }

            if (document is null)
            {
                return null;
            }

            var analysis = analyzer.Analyze(document.Content, document.TextLanguage);
            var diagnostics = analysis.Diagnostics
                ?? throw new InvalidOperationException("DEBUG word-analysis diagnostics were not created.");
            var sentences = analysis.Sentences.Select(sentence => new AnalysisSentenceDetails(
                    sentence,
                    document.Content.Substring(sentence.StartPosition, sentence.Length),
                    IsRangeInside(document.Content.Length, sentence.StartPosition, sentence.Length)))
                .ToArray();
            var summary = new AnalysisDocumentSummary(
                document.Id,
                document.Title,
                document.TextLanguage,
                document.ExplanationLanguage,
                document.Content.Length,
                document.ContentFingerprint,
                analysis.Sentences.Count,
                diagnostics.TokenDecisions.Count(decision => decision.IsIncluded),
                diagnostics.TokenDecisions.Count(decision => !decision.IsIncluded),
                analysis.Candidates.Count,
                analysis.OccurrenceCount);
            return new DocumentAnalysisReport(summary, sentences, diagnostics);
        });
#endif

    public Task<ReviewDiagnosticsSnapshot> GetDiagnosticsAsync() => database.ReadAsync(async connection =>
    {
        var documents = await connection.Table<DocumentEntity>().OrderBy(item => item.Id).ToListAsync();
        var sentences = await connection.Table<SentenceSpanEntity>().OrderBy(item => item.Id).ToListAsync();
        var words = await connection.Table<WordEntity>().OrderBy(item => item.Id).ToListAsync();
        var forms = await connection.Table<WordFormEntity>().OrderBy(item => item.Id).ToListAsync();
        var occurrences = await connection.Table<WordOccurrenceEntity>().OrderBy(item => item.Id).ToListAsync();
        var sessions = await connection.Table<ReviewSessionEntity>().OrderBy(item => item.Id).ToListAsync();
        var lexicalCache = await connection.Table<LexicalCacheEntity>().OrderBy(item => item.Id).ToListAsync();
        var preparationSessions = await connection.Table<PreparationSessionEntity>().OrderBy(item => item.Id).ToListAsync();
        var preparationCandidates = await connection.Table<PreparationCandidateEntity>().OrderBy(item => item.Id).ToListAsync();
        var meanings = await connection.Table<MeaningEntity>().OrderBy(item => item.Id).ToListAsync();
        var learningCards = await connection.Table<LearningCardEntity>().OrderBy(item => item.Id).ToListAsync();
        var learningReviews = await connection.Table<LearningReviewEntity>().OrderBy(item => item.Id).ToListAsync();
        var learningSessions = await connection.Table<LearningSessionEntity>().OrderBy(item => item.Id).ToListAsync();
        var contextSnapshots = await connection.Table<ContextSnapshotEntity>().OrderBy(item => item.Id).ToListAsync();
        var documentsById = documents.ToDictionary(item => item.Id);
        var sentencesById = sentences.ToDictionary(item => item.Id);
        var wordsById = words.ToDictionary(item => item.Id);
        var sessionsByDocumentId = sessions.ToDictionary(item => item.DocumentId);
        var activeDocumentIds = sessions
            .Where(item => item.Status == ReviewSessionStatus.Active)
            .Select(item => item.DocumentId)
            .ToHashSet();
        var activeSession = sessions.FirstOrDefault(item => item.Status == ReviewSessionStatus.Active);
        ActiveReviewSummary? activeSummary = null;

        if (activeSession is not null)
        {
            var document = documents.FirstOrDefault(item => item.Id == activeSession.DocumentId);
            if (document is not null)
            {
                activeSummary = CreateActiveSummary(activeSession, document);
            }
        }

        return new ReviewDiagnosticsSnapshot(
            database.DatabasePath,
            documents.Select(document => new DiagnosticsDocument(
                document.Id,
                document.Title,
                document.TextLanguage,
                document.ExplanationLanguage,
                document.Content.Length,
                sentences.Count(sentence => sentence.DocumentId == document.Id),
                document.ImportedAt,
                sessionsByDocumentId.GetValueOrDefault(document.Id)?.Status)).ToArray(),
            sentences.Select(sentence =>
            {
                var document = documentsById.GetValueOrDefault(sentence.DocumentId);
                return new DiagnosticsSentence(
                    sentence.Id,
                    sentence.DocumentId,
                    document?.Title ?? string.Empty,
                    sentence.StartPosition,
                    sentence.Length,
                    sentence.Order,
                    document is null
                        ? string.Empty
                        : CreateSentencePreview(document.Content, sentence));
            }).ToArray(),
            words.Select(word => new DiagnosticsCandidate(
                word.Id,
                word.Language,
                word.CanonicalTerm,
                word.NormalizedTerm,
                word.TokenKind,
                word.Status,
                word.TotalOccurrenceCount,
                forms.Where(form => form.WordId == word.Id).Select(form => form.SurfaceForm).ToArray())).ToArray(),
            occurrences.Select(occurrence =>
            {
                var document = documentsById.GetValueOrDefault(occurrence.DocumentId);
                var sentence = sentencesById.GetValueOrDefault(occurrence.SentenceSpanId);
                var word = wordsById.GetValueOrDefault(occurrence.WordId);
                var context = document is not null && sentence is not null
                    ? CreateDiagnosticContext(document.Content, sentence, occurrence)
                    : (Before: string.Empty, After: string.Empty);

                return new DiagnosticsOccurrence(
                    occurrence.Id,
                    occurrence.WordId,
                    occurrence.DocumentId,
                    occurrence.SentenceSpanId,
                    word?.CanonicalTerm ?? string.Empty,
                    document?.Title ?? string.Empty,
                    context.Before,
                    occurrence.SurfaceForm,
                    context.After,
                    occurrence.StartPosition,
                    occurrence.Length,
                    occurrence.Order,
                    activeDocumentIds.Contains(occurrence.DocumentId));
            }).ToArray(),
            sessions.Select(session => new DiagnosticsSession(
                session.Id,
                session.DocumentId,
                documentsById.GetValueOrDefault(session.DocumentId)?.Title ?? string.Empty,
                session.Status,
                session.ReviewedCount,
                session.TotalCandidates,
                Math.Max(0, session.TotalCandidates - session.ReviewedCount),
                session.StartedAt,
                session.CompletedAt)).ToArray(),
            lexicalCache.Select(entry => new DiagnosticsLexicalCache(
                entry.Id,
                entry.NormalizedLemma,
                entry.SourceLanguage,
                entry.ExplanationLanguage,
                entry.TokenKind,
                entry.Provider,
                entry.SourceProject,
                entry.PageTitle,
                entry.RevisionId,
                entry.FetchedAtUtc)).ToArray(),
            preparationSessions.Select(session => new DiagnosticsPreparationSession(
                session.Id,
                session.Status,
                session.Method,
                session.CompletedItems,
                session.TotalItems,
                session.UpdatedAtUtc)).ToArray(),
            preparationCandidates.Select(candidate => new DiagnosticsPreparationCandidate(
                candidate.Id,
                candidate.SessionId,
                candidate.WordId,
                wordsById.GetValueOrDefault(candidate.WordId)?.CanonicalTerm ?? string.Empty,
                candidate.Order,
                candidate.Status,
                candidate.SelectedMeaningIndex,
                CreateDiagnosticMeanings(candidate.ResultJson),
                candidate.LookupAttemptCount,
                candidate.LastErrorCode)).ToArray(),
            meanings.Select(meaning => new DiagnosticsPreparedMeaning(
                meaning.Id,
                meaning.WordId,
                meaning.DisplayTerm,
                meaning.SelectedMeaningId,
                string.IsNullOrWhiteSpace(meaning.AcronymExpansion) ? null : meaning.AcronymExpansion,
                string.IsNullOrWhiteSpace(meaning.Translation) ? null : meaning.Translation,
                meaning.Definition,
                meaning.Source,
                meaning.SourceProject,
                meaning.SourcePageTitle,
                meaning.ConfirmedByUser,
                meaning.PreparedAt,
                string.IsNullOrWhiteSpace(meaning.EncounteredSurfaceForm)
                    ? null
                    : meaning.EncounteredSurfaceForm,
                string.IsNullOrWhiteSpace(meaning.GrammaticalRelationship)
                    ? null
                    : meaning.GrammaticalRelationship)).ToArray(),
            learningCards.Select(card => new DiagnosticsLearningCard(
                card.Id,
                card.WordId,
                card.MeaningId,
                card.Direction,
                card.State,
                card.DueAtUtc,
                card.IntervalDays,
                card.EaseFactor,
                card.LastRating)).ToArray(),
            learningReviews.Select(review => new DiagnosticsLearningReview(
                review.Id,
                review.CardId,
                review.SessionId,
                review.Rating,
                review.WasTypedAnswer,
                review.WasCorrect,
                review.ReviewedAtUtc,
                review.DueAtUtc,
                review.IntervalDays,
                review.EaseFactor)).ToArray(),
            learningSessions.Select(session => new DiagnosticsLearningSession(
                session.Id,
                session.Status,
                session.CompletedCards,
                session.TotalCards,
                session.AgainCount,
                session.HardCount,
                session.GoodCount,
                session.EasyCount,
                session.UpdatedAtUtc)).ToArray(),
            documents.Select(document =>
            {
                var hasActiveReview = sessions.Any(session => session.DocumentId == document.Id
                    && session.Status == ReviewSessionStatus.Active);
                var hasOccurrences = occurrences.Any(occurrence => occurrence.DocumentId == document.Id);
                var hasActiveSnapshots = contextSnapshots
                    .Where(snapshot => snapshot.SourceDocumentId == document.Id)
                    .Any(snapshot => wordsById.GetValueOrDefault(snapshot.WordId)?.Status != WordStatus.Known);
                return new DiagnosticsCleanupEligibility(
                    document.Id,
                    document.Title,
                    hasActiveReview,
                    hasOccurrences,
                    hasActiveSnapshots,
                    !hasActiveReview && !hasOccurrences && !hasActiveSnapshots);
            }).ToArray(),
            activeSummary);
    });

    private static IReadOnlyList<string> CreateDiagnosticMeanings(string resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return [];
        }

        try
        {
            var result = JsonSerializer.Deserialize<LexicalResult>(resultJson);
            return result?.Meanings
                .Select(meaning => string.IsNullOrWhiteSpace(meaning.Translation)
                    ? meaning.Definition
                    : $"{meaning.Translation} — {meaning.Definition}")
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return ["The stored lookup result could not be parsed."];
        }
    }

    private static ImportAnalysisResult CreateImport(
        SQLiteConnection connection,
        ImportTextRequest request,
        TextAnalysisResult analysis,
        string contentFingerprint)
    {
        ValidateCoordinates(request.Content, analysis);

        if (connection.Table<ReviewSessionEntity>().Any(session => session.Status == ReviewSessionStatus.Active))
        {
            throw new ActiveReviewExistsException();
        }

        var duplicateDocument = connection.Table<DocumentEntity>()
            .Where(document => document.ContentFingerprint == contentFingerprint)
            .ToList()
            .Any(document => string.Equals(document.Content, request.Content, StringComparison.Ordinal));
        if (!duplicateDocument)
        {
            duplicateDocument = connection.Table<DocumentEntity>()
                .Where(document => document.ContentFingerprint == null || document.ContentFingerprint == string.Empty)
                .ToList()
                .Any(document => string.Equals(document.Content, request.Content, StringComparison.Ordinal));
        }

        if (duplicateDocument)
        {
            return new ImportAnalysisResult(ImportAnalysisOutcome.ExactDuplicate, 0, 0, 0);
        }

        var existingWords = connection.Table<WordEntity>()
            .Where(word => word.Language == request.TextLanguage)
            .ToList()
            .ToDictionary(word => word.NormalizedTerm, StringComparer.Ordinal);
        var newCandidates = analysis.Candidates
            .Where(candidate => !existingWords.TryGetValue(candidate.Identity, out var word)
                || !IsEstablishedVocabularyStatus(word.Status))
            .ToArray();

        if (newCandidates.Length == 0)
        {
            return new ImportAnalysisResult(ImportAnalysisOutcome.NoNewVocabulary, 0, 0, 0);
        }

        var now = DateTime.UtcNow;
        var document = new DocumentEntity
        {
            Title = request.Title.Trim(),
            TextLanguage = request.TextLanguage,
            ExplanationLanguage = request.ExplanationLanguage,
            LookupMode = request.LookupMode,
            TargetLanguage = request.TargetLanguage ?? string.Empty,
            Content = request.Content,
            ContentFingerprint = contentFingerprint,
            ImportedAt = now,
            WordCount = analysis.OccurrenceCount
        };
        connection.Insert(document);

        var sentenceIds = new Dictionary<int, int>();
        foreach (var sentence in analysis.Sentences)
        {
            var entity = new SentenceSpanEntity
            {
                DocumentId = document.Id,
                StartPosition = sentence.StartPosition,
                Length = sentence.Length,
                Order = sentence.Order
            };
            connection.Insert(entity);
            sentenceIds.Add(sentence.Order, entity.Id);
        }

        var session = new ReviewSessionEntity
        {
            DocumentId = document.Id,
            Status = ReviewSessionStatus.Active,
            StartedAt = now
        };
        connection.Insert(session);

        var reviewOrder = 0;
        var expectedPersistedOccurrenceCount = 0;
        foreach (var analyzedCandidate in analysis.Candidates)
        {
            existingWords.TryGetValue(analyzedCandidate.Identity, out var word);
            var wordWasCreated = word is null;

            if (word is not null && IsEstablishedVocabularyStatus(word.Status))
            {
                if (word.Status == WordStatus.UnknownBacklog)
                {
                    AddExistingUnknownContribution(
                        connection,
                        word,
                        document.Id,
                        sentenceIds,
                        analyzedCandidate);
                    expectedPersistedOccurrenceCount += analyzedCandidate.Occurrences.Count;
                }

                continue;
            }

            var previousStatus = word?.Status ?? WordStatus.Unreviewed;
            var previousOccurrenceCount = word?.TotalOccurrenceCount ?? 0;
            var previousDocumentCount = word?.DocumentCount ?? 0;
            var previousUpdatedAt = word?.UpdatedAt ?? default;
            if (word is null)
            {
                word = new WordEntity
                {
                    Language = request.TextLanguage,
                    CanonicalTerm = analyzedCandidate.CanonicalTerm,
                    NormalizedTerm = analyzedCandidate.Identity,
                    TokenKind = analyzedCandidate.Kind,
                    Status = WordStatus.Unreviewed,
                    TotalOccurrenceCount = analyzedCandidate.Occurrences.Count,
                    DocumentCount = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                connection.Insert(word);
            }
            else
            {
                word.TotalOccurrenceCount += analyzedCandidate.Occurrences.Count;
                word.DocumentCount++;
                word.UpdatedAt = now;
                connection.Update(word);
            }

            AddSurfaceForms(connection, word.Id, analyzedCandidate.SurfaceForms);
            AddOccurrences(
                connection,
                word.Id,
                document.Id,
                sentenceIds,
                analyzedCandidate.Occurrences);
            expectedPersistedOccurrenceCount += analyzedCandidate.Occurrences.Count;

            connection.Insert(new ReviewCandidateEntity
            {
                SessionId = session.Id,
                WordId = word.Id,
                Order = reviewOrder++,
                Status = WordStatus.Unreviewed,
                PreviousWordStatus = previousStatus,
                PreviousTotalOccurrenceCount = previousOccurrenceCount,
                PreviousDocumentCount = previousDocumentCount,
                PreviousUpdatedAt = previousUpdatedAt,
                WasWordCreatedForSession = wordWasCreated
            });
        }

        session.TotalCandidates = reviewOrder;
        connection.Update(session);
        ValidatePersistedOccurrenceCount(
            connection,
            document.Id,
            expectedPersistedOccurrenceCount);
        return new ImportAnalysisResult(
            ImportAnalysisOutcome.Accepted,
            document.Id,
            session.Id,
            reviewOrder);
    }

    private static ReviewDecisionResult PersistDecision(
        SQLiteConnection connection,
        int wordId,
        WordStatus status)
    {
        var session = connection.Table<ReviewSessionEntity>()
            .FirstOrDefault(candidate => candidate.Status == ReviewSessionStatus.Active)
            ?? throw new InvalidOperationException("There is no active review session.");
        var current = connection.Table<ReviewCandidateEntity>()
            .Where(candidate => candidate.SessionId == session.Id && candidate.Status == WordStatus.Unreviewed)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The active review has no unresolved candidate.");

        if (current.WordId != wordId)
        {
            throw new InvalidOperationException("The submitted word is not the current review candidate.");
        }

        var word = connection.Find<WordEntity>(wordId)
            ?? throw new InvalidOperationException("The current review candidate has no word record.");
        session.DecisionSequence++;
        session.ReviewedCount++;
        IncrementStatusCount(session, status, 1);
        current.Status = status;
        current.DecisionSequence = session.DecisionSequence;
        current.DecidedAt = DateTime.UtcNow;
        word.Status = status;
        word.UpdatedAt = DateTime.UtcNow;
        connection.Update(current);
        connection.Update(word);

        if (session.ReviewedCount < session.TotalCandidates)
        {
            connection.Update(session);
            return new ReviewDecisionResult(false, null);
        }

        session.Status = ReviewSessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        connection.Update(session);
        var document = connection.Find<DocumentEntity>(session.DocumentId)
            ?? throw new InvalidOperationException("The completed review session has no document.");
        var summary = CreateCompletedSummary(session, document.Title);
        CompleteSession(connection, session);
        return new ReviewDecisionResult(true, summary);
    }

    private static bool UndoPreviousDecision(SQLiteConnection connection)
    {
        var session = connection.Table<ReviewSessionEntity>()
            .FirstOrDefault(candidate => candidate.Status == ReviewSessionStatus.Active);
        if (session is null || session.ReviewedCount == 0)
        {
            return false;
        }

        var previous = connection.Table<ReviewCandidateEntity>()
            .Where(candidate => candidate.SessionId == session.Id && candidate.DecisionSequence > 0)
            .OrderByDescending(candidate => candidate.DecisionSequence)
            .FirstOrDefault();
        if (previous is null)
        {
            return false;
        }

        var word = connection.Find<WordEntity>(previous.WordId)
            ?? throw new InvalidOperationException("The previous review candidate has no word record.");
        IncrementStatusCount(session, previous.Status, -1);
        session.ReviewedCount--;
        session.DecisionSequence--;
        word.Status = previous.PreviousWordStatus;
        word.UpdatedAt = DateTime.UtcNow;
        previous.Status = WordStatus.Unreviewed;
        previous.DecisionSequence = 0;
        previous.DecidedAt = null;
        connection.Update(word);
        connection.Update(previous);
        connection.Update(session);
        return true;
    }

    private static void DiscardActiveImport(SQLiteConnection connection)
    {
        var session = connection.Table<ReviewSessionEntity>()
            .FirstOrDefault(candidate => candidate.Status == ReviewSessionStatus.Active);
        if (session is null)
        {
            return;
        }

        var candidates = connection.Table<ReviewCandidateEntity>()
            .Where(candidate => candidate.SessionId == session.Id)
            .ToList();
        var candidatesByWordId = candidates.ToDictionary(candidate => candidate.WordId);
        var documentOccurrences = connection.Table<WordOccurrenceEntity>()
            .Where(occurrence => occurrence.DocumentId == session.DocumentId)
            .ToList();

        foreach (var occurrenceGroup in documentOccurrences.GroupBy(occurrence => occurrence.WordId))
        {
            if (candidatesByWordId.TryGetValue(occurrenceGroup.Key, out var candidate))
            {
                if (candidate.WasWordCreatedForSession)
                {
                    continue;
                }

                var reusedWord = connection.Find<WordEntity>(candidate.WordId)
                    ?? throw new InvalidOperationException("A reused review candidate has no word record.");
                RollBackSurfaceForms(connection, reusedWord.Id, occurrenceGroup);
                reusedWord.Status = candidate.PreviousWordStatus;
                reusedWord.TotalOccurrenceCount = candidate.PreviousTotalOccurrenceCount;
                reusedWord.DocumentCount = candidate.PreviousDocumentCount;
                reusedWord.UpdatedAt = candidate.PreviousUpdatedAt;
                connection.Update(reusedWord);
                continue;
            }

            var existingUnknown = connection.Find<WordEntity>(occurrenceGroup.Key)
                ?? throw new InvalidOperationException("An existing Unknown contribution has no word record.");
            RollBackSurfaceForms(connection, existingUnknown.Id, occurrenceGroup);
            existingUnknown.TotalOccurrenceCount -= occurrenceGroup.Count();
            existingUnknown.DocumentCount--;
            if (existingUnknown.TotalOccurrenceCount < 0 || existingUnknown.DocumentCount < 0)
            {
                throw new InvalidOperationException("An existing Unknown contribution could not be rolled back safely.");
            }

            connection.Update(existingUnknown);
        }

        connection.Execute("DELETE FROM WordOccurrences WHERE DocumentId = ?", session.DocumentId);
        connection.Execute("DELETE FROM SentenceSpans WHERE DocumentId = ?", session.DocumentId);
        connection.Execute("DELETE FROM ReviewCandidates WHERE SessionId = ?", session.Id);

        foreach (var candidate in candidates)
        {
            if (candidate.WasWordCreatedForSession)
            {
                connection.Execute("DELETE FROM WordForms WHERE WordId = ?", candidate.WordId);
                connection.Execute("DELETE FROM ReviewStates WHERE WordId = ?", candidate.WordId);
                connection.Execute("DELETE FROM Meanings WHERE WordId = ?", candidate.WordId);
                connection.Delete<WordEntity>(candidate.WordId);
            }
        }

        connection.Delete(session);
        connection.Delete<DocumentEntity>(session.DocumentId);
    }

    private static void CompleteSession(SQLiteConnection connection, ReviewSessionEntity session)
    {
        var candidates = connection.Table<ReviewCandidateEntity>()
            .Where(candidate => candidate.SessionId == session.Id)
            .ToList();
        var candidateWordIds = candidates.Select(candidate => candidate.WordId).ToHashSet();
        var existingUnknownWordIds = connection.Table<WordOccurrenceEntity>()
            .Where(occurrence => occurrence.DocumentId == session.DocumentId)
            .ToList()
            .Select(occurrence => occurrence.WordId)
            .Where(wordId => !candidateWordIds.Contains(wordId))
            .Distinct()
            .ToArray();
        var affectedDocumentIds = new HashSet<int> { session.DocumentId };

        foreach (var candidate in candidates)
        {
            var word = connection.Find<WordEntity>(candidate.WordId)
                ?? throw new InvalidOperationException("A completed review candidate has no word record.");
            var occurrences = connection.Table<WordOccurrenceEntity>()
                .Where(occurrence => occurrence.WordId == word.Id)
                .OrderBy(occurrence => occurrence.Order)
                .ToList();
            affectedDocumentIds.UnionWith(occurrences.Select(occurrence => occurrence.DocumentId));

            if (word.Status is WordStatus.Known or WordStatus.Ignored)
            {
                foreach (var occurrence in occurrences)
                {
                    connection.Delete(occurrence);
                }

                word.TotalOccurrenceCount = 0;
                word.DocumentCount = 0;
                foreach (var form in connection.Table<WordFormEntity>().Where(form => form.WordId == word.Id))
                {
                    connection.Delete(form);
                }
            }

            connection.Update(word);
        }

        RemoveUnreferencedSentenceSpans(connection, affectedDocumentIds);

        connection.Execute("DELETE FROM ReviewCandidates WHERE SessionId = ?", session.Id);

        if (session.UnknownCount == 0 && existingUnknownWordIds.Length == 0)
        {
            connection.Execute("DELETE FROM WordOccurrences WHERE DocumentId = ?", session.DocumentId);
            connection.Execute("DELETE FROM SentenceSpans WHERE DocumentId = ?", session.DocumentId);
            connection.Delete(session);
            connection.Delete<DocumentEntity>(session.DocumentId);
        }
    }

    private static bool IsEstablishedVocabularyStatus(WordStatus status) => status != WordStatus.Unreviewed;

    private static void AddExistingUnknownContribution(
        SQLiteConnection connection,
        WordEntity word,
        int documentId,
        IReadOnlyDictionary<int, int> sentenceIds,
        VocabularyCandidate candidate)
    {
        word.TotalOccurrenceCount += candidate.Occurrences.Count;
        word.DocumentCount++;
        connection.Update(word);
        AddSurfaceForms(connection, word.Id, candidate.SurfaceForms);
        AddOccurrences(
            connection,
            word.Id,
            documentId,
            sentenceIds,
            candidate.Occurrences);
    }

    private static void AddSurfaceForms(
        SQLiteConnection connection,
        int wordId,
        IReadOnlyDictionary<string, int> surfaceForms)
    {
        foreach (var surfaceForm in surfaceForms)
        {
            var existingForm = connection.Table<WordFormEntity>().FirstOrDefault(form =>
                form.WordId == wordId && form.SurfaceForm == surfaceForm.Key);
            if (existingForm is null)
            {
                connection.Insert(new WordFormEntity
                {
                    WordId = wordId,
                    SurfaceForm = surfaceForm.Key,
                    OccurrenceCount = surfaceForm.Value
                });
            }
            else
            {
                existingForm.OccurrenceCount += surfaceForm.Value;
                connection.Update(existingForm);
            }
        }
    }

    private static void AddOccurrences(
        SQLiteConnection connection,
        int wordId,
        int documentId,
        IReadOnlyDictionary<int, int> sentenceIds,
        IReadOnlyList<TokenOccurrence> occurrences)
    {
        foreach (var occurrence in occurrences)
        {
            connection.Insert(new WordOccurrenceEntity
            {
                WordId = wordId,
                DocumentId = documentId,
                SentenceSpanId = sentenceIds[occurrence.SentenceOrder],
                StartPosition = occurrence.StartPosition,
                Length = occurrence.Length,
                SurfaceForm = occurrence.SurfaceForm,
                TechnicalFamily = occurrence.TechnicalFamily,
                TechnicalInstanceYear = occurrence.TechnicalInstanceYear,
                TechnicalInstanceIdentifier = occurrence.TechnicalInstanceIdentifier ?? string.Empty,
                TechnicalVariant = occurrence.TechnicalVariant ?? string.Empty,
                Order = occurrence.Order
            });
        }
    }

    private static void RollBackSurfaceForms(
        SQLiteConnection connection,
        int wordId,
        IEnumerable<WordOccurrenceEntity> occurrences)
    {
        foreach (var surfaceForm in occurrences.GroupBy(
                     occurrence => occurrence.SurfaceForm,
                     StringComparer.Ordinal))
        {
            var surfaceText = surfaceForm.Key;
            var contributionCount = surfaceForm.Count();
            var form = connection.Table<WordFormEntity>().FirstOrDefault(candidate =>
                candidate.WordId == wordId && candidate.SurfaceForm == surfaceText)
                ?? throw new InvalidOperationException("A contributed surface form could not be rolled back.");
            form.OccurrenceCount -= contributionCount;
            if (form.OccurrenceCount < 0)
            {
                throw new InvalidOperationException("A contributed surface form has an invalid occurrence count.");
            }

            if (form.OccurrenceCount == 0)
            {
                connection.Delete(form);
            }
            else
            {
                connection.Update(form);
            }
        }
    }

    private static void RemoveUnreferencedSentenceSpans(
        SQLiteConnection connection,
        IEnumerable<int> documentIds)
    {
        foreach (var documentId in documentIds.Distinct())
        {
            foreach (var sentence in connection.Table<SentenceSpanEntity>()
                         .Where(sentence => sentence.DocumentId == documentId)
                         .ToList())
            {
                var isReferenced = connection.Table<WordOccurrenceEntity>()
                    .Any(occurrence => occurrence.SentenceSpanId == sentence.Id);
                if (!isReferenced)
                {
                    connection.Delete(sentence);
                }
            }
        }
    }

    private static string CreateContentFingerprint(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string CreateSentencePreview(
        string content,
        SentenceSpanEntity sentence)
    {
        if (sentence.StartPosition < 0
            || sentence.Length < 0
            || sentence.StartPosition + sentence.Length > content.Length)
        {
            return string.Empty;
        }

        var preview = NormalizeDiagnosticText(
            content.Substring(sentence.StartPosition, sentence.Length));
        return preview.Length <= 160 ? preview : $"{preview[..157]}...";
    }

    private static (string Before, string After) CreateDiagnosticContext(
        string content,
        SentenceSpanEntity sentence,
        WordOccurrenceEntity occurrence)
    {
        var relativeStart = occurrence.StartPosition - sentence.StartPosition;
        if (relativeStart < 0
            || relativeStart + occurrence.Length > sentence.Length
            || occurrence.StartPosition + occurrence.Length > content.Length)
        {
            return (string.Empty, string.Empty);
        }

        var sentenceText = content.Substring(sentence.StartPosition, sentence.Length);
        var before = NormalizeDiagnosticText(sentenceText[..relativeStart]);
        var after = NormalizeDiagnosticText(sentenceText[(relativeStart + occurrence.Length)..]);
        if (before.Length > 80)
        {
            before = $"...{before[^77..]}";
        }

        if (after.Length > 80)
        {
            after = $"{after[..77]}...";
        }

        return (before, after);
    }

    private static string NormalizeDiagnosticText(string value) => value
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Replace('\t', ' ');

    private static ActiveReviewSummary CreateActiveSummary(
        ReviewSessionEntity session,
        DocumentEntity document) => new(
        session.Id,
        document.Id,
        document.Title,
        session.ReviewedCount,
        session.TotalCandidates);

    private static CompletedReviewSummary CreateCompletedSummary(
        ReviewSessionEntity session,
        string documentTitle) => new(
        session.Id,
        documentTitle,
        session.TotalCandidates,
        session.KnownCount,
        session.UnknownCount,
        session.IgnoredCount);

    private static void IncrementStatusCount(
        ReviewSessionEntity session,
        WordStatus status,
        int value)
    {
        switch (status)
        {
            case WordStatus.Known:
                session.KnownCount += value;
                break;
            case WordStatus.UnknownBacklog:
                session.UnknownCount += value;
                break;
            case WordStatus.Ignored:
                session.IgnoredCount += value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static void ValidateImport(ImportTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("A document title is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("Document text is required.", nameof(request));
        }

        if (!SupportedLanguages.Contains(request.TextLanguage)
            || !SupportedLanguages.Contains(request.ExplanationLanguage))
        {
            throw new ArgumentException("Only English and German are supported.", nameof(request));
        }

        LexicalLookupLanguagePolicy.Validate(
            request.TextLanguage,
            request.LookupMode,
            request.TargetLanguage);
    }

    private static void ValidateCoordinates(string content, TextAnalysisResult analysis)
    {
        var failures = AnalysisInvariantValidator.Validate(content, analysis);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Text analysis invariant validation failed: {string.Join("; ", failures.Select(failure => $"{failure.Code}: {failure.Explanation}"))}");
        }
    }

    private static void ValidatePersistedOccurrenceCount(
        SQLiteConnection connection,
        int documentId,
        int expectedCount)
    {
        var persistedCount = connection.Table<WordOccurrenceEntity>()
            .Count(occurrence => occurrence.DocumentId == documentId);
        if (persistedCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Text analysis invariant validation failed: PersistedOccurrenceCountMismatch: Expected {expectedCount} persisted occurrence rows but found {persistedCount}.");
        }
    }

    private static bool IsRangeInside(int contentLength, int start, int length) =>
        start >= 0 && length >= 0 && start <= contentLength - length;
}
