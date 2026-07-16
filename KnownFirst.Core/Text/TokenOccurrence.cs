namespace KnownFirst.Core.Text;

public sealed record TokenOccurrence(
    string SurfaceForm,
    string Identity,
    TokenKind Kind,
    int StartPosition,
    int Length,
    int Order,
    int SentenceOrder);
