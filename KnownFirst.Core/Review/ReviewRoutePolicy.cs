namespace KnownFirst.Core.Review;

public static class ReviewRoutePolicy
{
    private static readonly HashSet<string> BlockedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "import-text",
        "learn",
        "prepare-words"
    };

    public static bool IsBlocked(string? route, bool hasActiveReview)
    {
        if (!hasActiveReview || string.IsNullOrWhiteSpace(route))
        {
            return false;
        }

        var normalizedRoute = route.Trim().Trim('/');
        var suffixIndex = normalizedRoute.IndexOfAny(['?', '#']);
        if (suffixIndex >= 0)
        {
            normalizedRoute = normalizedRoute[..suffixIndex];
        }

        return BlockedRoutes.Contains(normalizedRoute);
    }
}
