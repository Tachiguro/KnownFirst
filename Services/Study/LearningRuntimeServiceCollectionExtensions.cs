using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Services;
using KnownFirst.Services.Time;
using Microsoft.Extensions.DependencyInjection;

namespace KnownFirst.Services.Study;

public static class LearningRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddKnownFirstLearningRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFsrs6SchedulingService, Fsrs6SchedulingService>();
        services.AddSingleton(serviceProvider => new LearningService(
            serviceProvider.GetRequiredService<IKnownFirstDatabase>(),
            serviceProvider.GetRequiredService<SpellingAnswerComparer>(),
            serviceProvider.GetRequiredService<IClock>(),
            serviceProvider.GetRequiredService<IFsrs6SchedulingService>(),
            serviceProvider.GetService<IAppSettingsService>(),
            serviceProvider.GetService<ISchema8LearningFailureInjector>(),
            serviceProvider.GetService<ILearningTimezoneResolver>()));
        services.AddSingleton<ILearningService>(serviceProvider =>
            serviceProvider.GetRequiredService<LearningService>());
        return services;
    }
}
