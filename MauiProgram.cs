using KnownFirst.Data;
using KnownFirst.Core.Language;
using KnownFirst.Core.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace KnownFirst;

public static class MauiProgram
{
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Microsoft.AspNetCore.Components.Web.HeadOutlet))]
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        var buildIdentity = new BuildIdentityService();
        var diagnosticOptions = DiagnosticLogConfiguration.Create(buildIdentity);
        var fileLoggerProvider = new RollingFileLoggerProvider(diagnosticOptions);
        var bootstrapLogger = fileLoggerProvider.CreateLogger("KnownFirst.Startup");
        bootstrapLogger.LogInformation(
            DiagnosticEventIds.StartupBeginning,
            "Application startup is beginning. Configuration = {BuildConfiguration}, target = {TargetFramework}",
            diagnosticOptions.BuildConfiguration,
            diagnosticOptions.TargetFramework);

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.AddSingleton<IBuildIdentityService>(buildIdentity);
        builder.Services.AddSingleton(fileLoggerProvider);
        builder.Services.AddSingleton<IAppDiagnosticsService, AppDiagnosticsService>();
        builder.Services.AddSingleton<RuntimeExceptionMonitor>();
        builder.Services.AddSingleton<ILanguagePreferenceStore, MauiLanguagePreferenceStore>();
        builder.Services.AddSingleton<IDeviceCultureProvider, SystemDeviceCultureProvider>();
        builder.Services.AddSingleton<IUiCultureContext, SystemUiCultureContext>();
        builder.Services.AddSingleton<ILanguageDiagnostics, LanguageDiagnostics>();
        builder.Services.AddSingleton<ILanguageSelectionService, LanguageSelectionService>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<INavigationHistoryService, NavigationHistoryService>();
        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddSingleton<ISettingsFeedbackService, SettingsFeedbackService>();
        builder.Services.AddSingleton<IKnownFirstDatabase, KnownFirstDatabase>();
        builder.Services.AddSingleton<IDashboardService, DashboardService>();
        builder.Services.AddSingleton<ISentenceSegmenter, DeterministicSentenceSegmenter>();
        builder.Services.AddSingleton<TextAnalyzer>();
        builder.Services.AddSingleton<ITextReviewService, TextReviewService>();
#if DEBUG
        builder.Services.AddSingleton<DebugLearningClock>(_ => new(TimeProvider.System));
        builder.Services.AddSingleton<IClock>(services =>
            services.GetRequiredService<DebugLearningClock>());
#else
        builder.Services.AddSingleton<IClock, SystemClock>();
#endif
        builder.Services.AddSingleton<ISpacedRepetitionScheduler, SimpleSpacedRepetitionScheduler>();
        builder.Services.AddSingleton<SpellingAnswerComparer>();
        builder.Services.AddSingleton<AcronymExpansionDetector>();
        builder.Services.AddSingleton<MeaningRanker>();
        builder.Services.AddSingleton<ILexicalDiagnosticLog, LexicalDiagnosticLog>();
        builder.Services.AddSingleton<ILexicalCacheRepository, LexicalCacheRepository>();
        builder.Services.AddSingleton<WiktionaryHtmlParser>();
        builder.Services.AddSingleton<IAsyncDelay, SystemAsyncDelay>();
        builder.Services.AddSingleton(new HttpClient());
        builder.Services.AddSingleton<IDictionaryLookupProvider, WiktionaryLookupProvider>();
        builder.Services.AddSingleton<ILexicalEnrichmentService, LexicalEnrichmentService>();
        builder.Services.AddSingleton<IPreparationService, PreparationService>();
        builder.Services.AddSingleton<ILearningService, LearningService>();
        builder.Services.AddSingleton<IWorkflowStateService, WorkflowStateService>();
        builder.Services.AddSingleton<IStartupMaintenanceService, StartupMaintenanceService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
#if DEBUG || KNOWNFIRST_DIAGNOSTICS
        builder.Logging.AddDebug();
#endif
        builder.Logging.SetMinimumLevel(diagnosticOptions.MinimumLevel);
        builder.Logging.AddProvider(fileLoggerProvider);

        try
        {
            var app = builder.Build();
            var startupLogger = app.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("KnownFirst.Startup");
            app.Services.GetRequiredService<RuntimeExceptionMonitor>().Start();
            app.Services.GetRequiredService<ILanguageSelectionService>().Initialize();
            startupLogger.LogDebug(
                "Application services were built and startup services were resolved. Session = {SessionId}",
                fileLoggerProvider.SessionId);
            return app;
        }
        catch (Exception exception)
        {
            bootstrapLogger.LogCritical(
                DiagnosticEventIds.StartupFailed,
                exception,
                "Application startup failed while building or resolving services.");
            fileLoggerProvider.Flush();
            throw;
        }
    }
}
