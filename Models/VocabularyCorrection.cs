namespace KnownFirst.Models;

using KnownFirst.Data.Targets;

/// <summary>
/// Status codes for the user-confirmed accepted answer addition operation.
/// </summary>
public enum AddAcceptedAnswerVariantStatus
{
    Success = 0,
    InvalidWordId = 1,
    WordNotFound = 2,
    InvalidSenseId = 3,
    SenseNotFound = 4,
    InvalidTargetId = 5,
    TargetNotFound = 6,
    OwnershipMismatch = 7,
    EmptyAnswerText = 8,
    AnswerTextTooLong = 9,
    DuplicateAnswer = 10,
    InvalidAnswerLanguage = 11,
    BlockedByActiveWorkflow = 12
}

/// <summary>
/// Request to add a user-confirmed semantically equivalent AcceptedOnly answer variant to an existing LearningTarget.
/// </summary>
public sealed record AddAcceptedAnswerVariantRequest
{
    public AddAcceptedAnswerVariantRequest(
        int wordId,
        int senseId,
        int targetId,
        string answerText,
        string? expectedLanguage = null)
    {
        WordId = wordId;
        SenseId = senseId;
        TargetId = targetId;
        AnswerText = answerText;
        ExpectedLanguage = expectedLanguage;
    }

    public int WordId { get; init; }

    public int SenseId { get; init; }

    public int TargetId { get; init; }

    public string AnswerText { get; init; }

    public string? ExpectedLanguage { get; init; }

    public string? AnswerLanguage => ExpectedLanguage;
}

/// <summary>
/// Result envelope for adding an AcceptedOnly answer variant.
/// </summary>
public sealed record AddAcceptedAnswerVariantResult
{
    public AddAcceptedAnswerVariantStatus Status { get; init; }

    public PersistedTargetAnswerVariant? Variant { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsSuccess => Status == AddAcceptedAnswerVariantStatus.Success && Variant is not null;

    public bool IsBlockedByActiveWorkflow => Status == AddAcceptedAnswerVariantStatus.BlockedByActiveWorkflow;

    public bool IsDuplicate => Status == AddAcceptedAnswerVariantStatus.DuplicateAnswer;

    public bool IsOwnershipMismatch => Status == AddAcceptedAnswerVariantStatus.OwnershipMismatch;

    public bool IsNotFound => Status is AddAcceptedAnswerVariantStatus.WordNotFound
        or AddAcceptedAnswerVariantStatus.SenseNotFound
        or AddAcceptedAnswerVariantStatus.TargetNotFound;

    public bool IsInvalidId => Status is AddAcceptedAnswerVariantStatus.InvalidWordId
        or AddAcceptedAnswerVariantStatus.InvalidSenseId
        or AddAcceptedAnswerVariantStatus.InvalidTargetId;

    public bool IsValidationFailure => Status is AddAcceptedAnswerVariantStatus.EmptyAnswerText
        or AddAcceptedAnswerVariantStatus.AnswerTextTooLong
        or AddAcceptedAnswerVariantStatus.DuplicateAnswer
        or AddAcceptedAnswerVariantStatus.InvalidAnswerLanguage;

    public static AddAcceptedAnswerVariantResult Success(PersistedTargetAnswerVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return new AddAcceptedAnswerVariantResult
        {
            Status = AddAcceptedAnswerVariantStatus.Success,
            Variant = variant
        };
    }

    public static AddAcceptedAnswerVariantResult InvalidWordId(int wordId) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.InvalidWordId,
            ErrorMessage = $"WordId '{wordId}' is invalid. Positive integer required."
        };

    public static AddAcceptedAnswerVariantResult WordNotFound(int wordId) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.WordNotFound,
            ErrorMessage = $"Word with ID {wordId} was not found."
        };

    public static AddAcceptedAnswerVariantResult InvalidSenseId(int senseId) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.InvalidSenseId,
            ErrorMessage = $"SenseId '{senseId}' is invalid. Positive integer required."
        };

    public static AddAcceptedAnswerVariantResult SenseNotFound(int senseId) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.SenseNotFound,
            ErrorMessage = $"Sense with ID {senseId} was not found."
        };

    public static AddAcceptedAnswerVariantResult InvalidTargetId(int targetId) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.InvalidTargetId,
            ErrorMessage = $"TargetId '{targetId}' is invalid. Positive integer required."
        };

    public static AddAcceptedAnswerVariantResult TargetNotFound(int targetId) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.TargetNotFound,
            ErrorMessage = $"LearningTarget with ID {targetId} was not found."
        };

    public static AddAcceptedAnswerVariantResult OwnershipMismatch(string message) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.OwnershipMismatch,
            ErrorMessage = message
        };

    public static AddAcceptedAnswerVariantResult EmptyAnswerText() =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.EmptyAnswerText,
            ErrorMessage = "Answer text must not be empty or whitespace."
        };

    public static AddAcceptedAnswerVariantResult AnswerTextTooLong(int actualLength, int maxLength) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.AnswerTextTooLong,
            ErrorMessage = $"Answer text length ({actualLength}) exceeds maximum allowed length ({maxLength})."
        };

    public static AddAcceptedAnswerVariantResult DuplicateAnswer(string normalizedText) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.DuplicateAnswer,
            ErrorMessage = $"Target already contains an equivalent answer with normalized text '{normalizedText}'."
        };

    public static AddAcceptedAnswerVariantResult InvalidAnswerLanguage(string message) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.InvalidAnswerLanguage,
            ErrorMessage = message
        };

    public static AddAcceptedAnswerVariantResult BlockedByActiveWorkflow(string message) =>
        new()
        {
            Status = AddAcceptedAnswerVariantStatus.BlockedByActiveWorkflow,
            ErrorMessage = message
        };
}
