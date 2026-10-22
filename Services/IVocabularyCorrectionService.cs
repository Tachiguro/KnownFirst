namespace KnownFirst.Services;

using KnownFirst.Models;

/// <summary>
/// Provides transactional vocabulary correction services for existing persisted vocabulary entities.
/// </summary>
public interface IVocabularyCorrectionService
{
    /// <summary>
    /// Hard upper bound on accepted answer text length in characters.
    /// </summary>
    public const int MaxAnswerTextLength = 200;

    /// <summary>
    /// Atomically validates and adds a single user-confirmed semantically equivalent AcceptedOnly
    /// answer variant to an existing persisted LearningTarget under real Word and Sense ownership.
    /// </summary>
    Task<AddAcceptedAnswerVariantResult> AddAcceptedAnswerVariantAsync(
        AddAcceptedAnswerVariantRequest request,
        CancellationToken cancellationToken = default);
}
