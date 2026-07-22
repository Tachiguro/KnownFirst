using Microsoft.Extensions.Logging;

namespace KnownFirst.Services.Study;

public sealed class StartupMaintenanceService(
    ILearningService learningService,
    ILogger<StartupMaintenanceService> logger) : IStartupMaintenanceService
{
    private int _started;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await learningService.RunMaintenanceAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Background document cleanup could not be completed.");
            }
        });
    }
}
