namespace KnownFirst.Services;

using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using SQLite;

/// <summary>
/// Provides transactional Schema-14 vocabulary correction operations under real entity hierarchy.
/// </summary>
public sealed class VocabularyCorrectionService : IVocabularyCorrectionService
{
    private readonly IKnownFirstDatabase _database;
    private readonly IClock? _clock;

    public VocabularyCorrectionService(IKnownFirstDatabase database, IClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock;
    }

    public async Task<AddAcceptedAnswerVariantResult> AddAcceptedAnswerVariantAsync(
        AddAcceptedAnswerVariantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return await _database.RunInTransactionAsync(conn =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nowUtc = _clock?.UtcNow ?? DateTime.UtcNow;
            if (nowUtc.Kind != DateTimeKind.Utc)
            {
                nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            }

            return AddAcceptedAnswerVariant(conn, request, nowUtc);
        }).ConfigureAwait(false);
    }

    internal static AddAcceptedAnswerVariantResult AddAcceptedAnswerVariant(
        SQLiteConnection connection,
        AddAcceptedAnswerVariantRequest request,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(request);

        // 1. Structural ID validation
        if (request.WordId <= 0)
        {
            return AddAcceptedAnswerVariantResult.InvalidWordId(request.WordId);
        }

        if (request.SenseId <= 0)
        {
            return AddAcceptedAnswerVariantResult.InvalidSenseId(request.SenseId);
        }

        if (request.TargetId <= 0)
        {
            return AddAcceptedAnswerVariantResult.InvalidTargetId(request.TargetId);
        }

        // 2. Answer text validation and Unicode normalization
        if (string.IsNullOrWhiteSpace(request.AnswerText))
        {
            return AddAcceptedAnswerVariantResult.EmptyAnswerText();
        }

        var displayText = request.AnswerText.Trim().Normalize(NormalizationForm.FormC);
        if (displayText.Length == 0)
        {
            return AddAcceptedAnswerVariantResult.EmptyAnswerText();
        }

        if (displayText.Length > IVocabularyCorrectionService.MaxAnswerTextLength)
        {
            return AddAcceptedAnswerVariantResult.AnswerTextTooLong(
                displayText.Length,
                IVocabularyCorrectionService.MaxAnswerTextLength);
        }

        var normalizedAnswer = LearningTargetRepository.NormalizeAnswerText(displayText);
        if (normalizedAnswer.Length == 0)
        {
            return AddAcceptedAnswerVariantResult.EmptyAnswerText();
        }

        // 3. Workflow safety guard: fail closed if any active workflow session exists
        var hasActiveReview = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ReviewSessions WHERE Status = ?", (int)ReviewSessionStatus.Active) > 0;
        var hasActivePrep = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM PreparationSessions WHERE Status = ?", (int)PreparationSessionStatus.Active) > 0;
        var hasActiveLearning = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM LearningSessions WHERE Status = ?", (int)LearningSessionStatus.Active) > 0;

        if (hasActiveReview || hasActivePrep || hasActiveLearning)
        {
            return AddAcceptedAnswerVariantResult.BlockedByActiveWorkflow("An active workflow session is in progress.");
        }

        // 4. Verify Word existence
        var wordExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Words WHERE Id = ?", request.WordId) > 0;
        if (!wordExists)
        {
            return AddAcceptedAnswerVariantResult.WordNotFound(request.WordId);
        }

        // 5. Verify Sense existence and ownership
        var senseRows = connection.Query<SenseRow>(
            "SELECT Id, WordId, SourceLanguage, ExplanationLanguage FROM Senses WHERE Id = ?",
            request.SenseId);
        if (senseRows.Count == 0)
        {
            return AddAcceptedAnswerVariantResult.SenseNotFound(request.SenseId);
        }

        var sense = senseRows[0];
        if (sense.WordId != request.WordId)
        {
            return AddAcceptedAnswerVariantResult.OwnershipMismatch(
                $"Sense {request.SenseId} belongs to Word {sense.WordId}, not requested Word {request.WordId}.");
        }

        // 6. Verify LearningTarget existence and ownership
        var targetRows = connection.Query<TargetRow>(
            "SELECT Id, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut FROM LearningTargets WHERE Id = ?",
            request.TargetId);
        if (targetRows.Count == 0)
        {
            return AddAcceptedAnswerVariantResult.TargetNotFound(request.TargetId);
        }

        var target = targetRows[0];
        if (target.SenseId != request.SenseId)
        {
            return AddAcceptedAnswerVariantResult.OwnershipMismatch(
                $"LearningTarget {request.TargetId} belongs to Sense {target.SenseId}, not requested Sense {request.SenseId}.");
        }

        // 7. Determine answer language from verified target semantics and validate request expectations
        if (string.IsNullOrWhiteSpace(target.TargetLanguage))
        {
            return AddAcceptedAnswerVariantResult.InvalidAnswerLanguage("LearningTarget has no valid target answer language.");
        }

        var targetAnswerLang = target.TargetLanguage.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(request.ExpectedLanguage))
        {
            var expectedLang = request.ExpectedLanguage.Trim().ToLowerInvariant();
            if (!string.Equals(expectedLang, targetAnswerLang, StringComparison.OrdinalIgnoreCase))
            {
                return AddAcceptedAnswerVariantResult.InvalidAnswerLanguage(
                    $"Requested answer language '{request.ExpectedLanguage}' does not match target language '{target.TargetLanguage}'.");
            }
        }

        // 8. Reject duplicate answers on this target
        var existingVariants = LearningTargetRepository.GetAnswerVariants(connection, request.TargetId);
        if (existingVariants.Any(v => string.Equals(v.NormalizedText, normalizedAnswer, StringComparison.OrdinalIgnoreCase)))
        {
            return AddAcceptedAnswerVariantResult.DuplicateAnswer(normalizedAnswer);
        }

        // 9. Atomic insertion via LearningTargetRepository
        var draft = new TargetAnswerVariantDraft(
            AnswerLanguage: targetAnswerLang,
            DisplayText: displayText,
            Requirement: AnswerVariantRequirement.AcceptedOnly,
            IsPreferred: false,
            SourceMeaningId: null,
            NormalizedText: normalizedAnswer);

        var persisted = LearningTargetRepository.AddAnswerVariant(
            connection,
            request.TargetId,
            draft,
            nowUtc);

        return AddAcceptedAnswerVariantResult.Success(persisted);
    }

    private sealed class SenseRow
    {
        public int Id { get; set; }
        public int WordId { get; set; }
        public string SourceLanguage { get; set; } = string.Empty;
        public string ExplanationLanguage { get; set; } = string.Empty;
    }

    private sealed class TargetRow
    {
        public int Id { get; set; }
        public int SenseId { get; set; }
        public int TargetKind { get; set; }
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public int TypingOptOut { get; set; }
    }
}
