namespace KnownFirst.Services;

public sealed record DashboardStatistics(
    int DocumentCount,
    int UnreviewedWordCount,
    int KnownWordCount,
    int UnknownBacklogWordCount,
    int PreparedAndLearningWordCount)
{
    public static DashboardStatistics Empty { get; } = new(0, 0, 0, 0, 0);
}
