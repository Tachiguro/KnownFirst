using System.Globalization;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;

namespace KnownFirst.Components.Pages;

/// <summary>
/// Persisted context text split around its persisted UTF-16 target span.
/// </summary>
public readonly record struct VocabularyContextSegments(string Before, string Target, string After);

/// <summary>
/// Pure presentation helpers for the Vocabulary Word Detail page.
/// Maps persisted enum values to localization keys and formats persisted values without fabricating data.
/// </summary>
public static class VocabularyWordDetailPresentation
{
    public static string GetTargetKindKey(LearningTargetKind kind) => kind switch
    {
        LearningTargetKind.Definition => "VocabularyDetail_TargetKindDefinition",
        LearningTargetKind.Translation => "VocabularyDetail_TargetKindTranslation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string GetCardStateKey(Fsrs6CardState state) => state switch
    {
        Fsrs6CardState.New => "VocabularyDetail_CardStateNew",
        Fsrs6CardState.Learning => "VocabularyDetail_CardStateLearning",
        Fsrs6CardState.Review => "VocabularyDetail_CardStateReview",
        Fsrs6CardState.Relearning => "VocabularyDetail_CardStateRelearning",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static string GetRatingKey(ReviewRating rating) => rating switch
    {
        ReviewRating.Again => "Learn_Again",
        ReviewRating.Hard => "Learn_Hard",
        ReviewRating.Good => "Learn_Good",
        ReviewRating.Easy => "Learn_Easy",
        _ => throw new ArgumentOutOfRangeException(nameof(rating), rating, null)
    };

    public static string GetRequirementKey(AnswerVariantRequirement requirement) => requirement switch
    {
        AnswerVariantRequirement.Required => "VocabularyDetail_VariantRequired",
        AnswerVariantRequirement.AcceptedOnly => "VocabularyDetail_VariantAcceptedOnly",
        _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement, null)
    };

    public static string GetTokenKindKey(TokenKind kind) => kind switch
    {
        TokenKind.Acronym => "TokenKind_Acronym",
        TokenKind.Abbreviation => "TokenKind_Abbreviation",
        TokenKind.TechnicalTerm => "TokenKind_TechnicalTerm",
        _ => "TokenKind_Word"
    };

    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// ISO-8601 UTC instant for the machine-readable <c>datetime</c> attribute.
    /// </summary>
    public static string ToMachineTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Human-readable timestamp in the device's local time zone and the current UI culture.
    /// </summary>
    public static string ToDisplayTimestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>
    /// Splits persisted context text around its persisted target span, or returns
    /// <see langword="null"/> when the span is empty or out of bounds so the text is shown unhighlighted.
    /// </summary>
    public static VocabularyContextSegments? TrySplitContext(VocabularyContextSnapshotDetail context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var text = context.Text;
        var start = context.TargetStart;
        var length = context.TargetLength;

        if (start < 0 || length <= 0 || start > text.Length || length > text.Length - start)
        {
            return null;
        }

        return new VocabularyContextSegments(
            text[..start],
            text.Substring(start, length),
            text[(start + length)..]);
    }

    public static string GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus status) => status switch
    {
        AddAcceptedAnswerVariantStatus.Success => "VocabularyDetail_EquivalentAnswerSuccess",
        AddAcceptedAnswerVariantStatus.DuplicateAnswer => "VocabularyDetail_EquivalentAnswerDuplicate",
        AddAcceptedAnswerVariantStatus.BlockedByActiveWorkflow => "VocabularyDetail_EquivalentAnswerWorkflowBlocked",
        AddAcceptedAnswerVariantStatus.EmptyAnswerText => "VocabularyDetail_EquivalentAnswerEmpty",
        AddAcceptedAnswerVariantStatus.AnswerTextTooLong => "VocabularyDetail_EquivalentAnswerTooLong",
        AddAcceptedAnswerVariantStatus.InvalidAnswerLanguage => "VocabularyDetail_EquivalentAnswerLanguageMismatch",
        _ => "VocabularyDetail_EquivalentAnswerFailure"
    };

    public static string GetEligibilityNoticeKey(TargetEquivalentAnswerEligibility eligibility) => eligibility switch
    {
        TargetEquivalentAnswerEligibility.TruncatedVariants => "VocabularyDetail_EquivalentAnswerTruncatedNotice",
        TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous => "VocabularyDetail_EquivalentAnswerUnavailableNotice",
        _ => string.Empty
    };
}
