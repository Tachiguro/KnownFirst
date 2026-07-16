namespace KnownFirst.Services;

public interface IDashboardService
{
    Task<DashboardStatistics> GetStatisticsAsync();
}
