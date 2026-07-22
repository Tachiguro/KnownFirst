namespace KnownFirst.Services;

public sealed class ActiveReviewExistsException()
    : InvalidOperationException("Another text review is already active.");
