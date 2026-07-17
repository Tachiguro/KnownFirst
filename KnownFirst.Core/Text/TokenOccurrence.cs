namespace KnownFirst.Core.Text;

public sealed record TokenOccurrence(
    string SurfaceForm,
    string Identity,
    TokenKind Kind,
    int StartPosition,
    int Length,
    int Order,
    int SentenceOrder,
    string? CanonicalTerm = null,
    TechnicalTokenFamily TechnicalFamily = TechnicalTokenFamily.None,
    int? TechnicalInstanceYear = null,
    string? TechnicalInstanceIdentifier = null,
    string? TechnicalVariant = null);
