using KnownFirst.Data;
using KnownFirst.Core.Language;
using KnownFirst.Core.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using KnownFirst.Services.Isolation;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using KnownFirst.Services.DataSafety;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using System.Diagnostics.CodeAnalysis;

namespace KnownFirst;

public static class MauiProgram
{
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Microsoft.AspNetCore.Components.Web.HeadOutlet))]
    public static MauiApp CreateMauiApp()
    {
#if KNOWNFIRST_ANDROID_GUI_TEST
        GuiTestProfile.ConfigureAndroidGuiTestProfileForCurrentProcess(FileSystem.AppDataDirectory);
#endif
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
        if (GuiTestProfile.IsActive)
        {
            bootstrapLogger.LogWarning(
                "GUI test profile is active. Database and preferences are isolated under {GuiTestProfileRoot}.",
                GuiTestProfile.RootPath);
        }

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.AddSingleton<IBuildIdentityService>(buildIdentity);
        builder.Services.AddSingleton<IPreferences>(_ =>
        {
            IPreferences preferences = GuiTestProfile.IsActive
                ? new IsolatedFilePreferences(GuiTestProfile.RootPath)
                : Preferences.Default;
            GuiTestProfile.InitializeAndroidGuiTestPreferences(preferences, buildIdentity.Identity.Version);
            return preferences;
        });
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
        builder.Services.AddSingleton<IWhatsNewPreferenceStore, MauiWhatsNewPreferenceStore>();
        builder.Services.AddSingleton<IReleaseNotesService, ReleaseNotesService>();
        builder.Services.AddSingleton<IBugReportLauncherService, BugReportLauncherService>();
        builder.Services.AddSingleton<IKnownFirstDatabase, KnownFirstDatabase>();
        builder.Services.AddSingleton<IBackupPlatformInfo, MauiBackupPlatformInfo>();
        builder.Services.AddSingleton<IBackupService, BackupService>();
#if WINDOWS
#if KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED
        if (GuiTestPortableArchive.IsActive)
        {
            // GUI-test-only: replaces only the interactive native Save/Open picker boundary. Real
            // archive capture, mapping, writing, validation, and import still run through the
            // genuine BackupService — see GuiTestPortableArchiveFileService.
            builder.Services.AddSingleton<IPortableArchiveFileService, GuiTestPortableArchiveFileService>();
        }
        else
        {
            builder.Services.AddSingleton<IPortableArchiveFileService, WindowsPortableArchiveFileService>();
        }
#else
        builder.Services.AddSingleton<IPortableArchiveFileService, WindowsPortableArchiveFileService>();
#endif
#elif ANDROID
        builder.Services.AddSingleton<IPortableArchiveFileService, AndroidPortableArchiveFileService>();
#endif
        builder.Services.AddSingleton<IDashboardService, DashboardService>();
        builder.Services.AddSingleton<ISentenceSegmenter, DeterministicSentenceSegmenter>();
        builder.Services.AddSingleton<TextAnalyzer>();
        // German Enhanced Term Recognition (Package 2): the production offline IGermanLexicon,
        // lazily backed by the embedded german-lexicon.v2.kfgl bundle. Registering it does not
        // load the ~11 MB bundle; only an actual enhanced German lookup does.
        builder.Services.AddSingleton<IGermanLexicon, BundledGermanLexicon>();
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
#if KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED || KNOWNFIRST_ANDROID_GUI_TEST
        if (GuiTestScenarioSeed.IsActive || GuiTestProfile.IsAndroidGuiTestProfile)
        {
            // GUI-test seed scenarios need deterministic, in-process, network-free meanings —
            // never the real Wiktionary/Wikipedia providers — so this substitutes the entire
            // provider set instead of adding to it.
            builder.Services.AddSingleton<GuiTestSeedLookupProvider>();
            builder.Services.AddSingleton<IDictionaryLookupProvider>(sp => sp.GetRequiredService<GuiTestSeedLookupProvider>());
            builder.Services.AddSingleton<ILexicalLookupProvider>(sp => sp.GetRequiredService<GuiTestSeedLookupProvider>());
            builder.Services.AddSingleton<ILexicalLookupProviderResolver, LexicalLookupProviderResolver>();
        }
        else
        {
            builder.Services.AddLexicalProviders();
        }
#else
        builder.Services.AddLexicalProviders();
#endif

        builder.Services.AddSingleton<ILexicalEnrichmentService, LexicalEnrichmentService>();
#if KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED
        if (GuiTestFaultInjection.IsActive)
        {
            var checkpoint = GuiTestFaultInjection.Resolve()!;
            builder.Services.AddSingleton<IPreparationFaultInjector>(
                _ => new GuiTestFaultInjection.SingleShotInjector(checkpoint));
        }
#endif
        builder.Services.AddSingleton<IPreparationService, PreparationService>();
        builder.Services.AddSingleton<KnownFirst.Services.Time.ILearningTimezoneResolver, KnownFirst.Services.Time.LearningTimezoneResolver>();
        builder.Services.AddSingleton<ILearningService, LearningService>();
        builder.Services.AddSingleton<IWorkflowStateService, WorkflowStateService>();
        builder.Services.AddSingleton<IWorkflowChangeNotifier, WorkflowChangeNotifier>();
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
#if KNOWNFIRST_GUI_TEST_PROFILE_SUPPORTED
            if (GuiTestScenarioSeed.IsActive)
            {
                startupLogger.LogWarning(
                    "GUI test scenario seeding is active ({SeedScenario}); seeding deterministic data before first render.",
                    Environment.GetEnvironmentVariable(GuiTestScenarioSeed.EnvironmentVariableName));
                // CreateMauiApp runs on the UI thread with a synchronization context installed, so
                // awaiting the seed inline and blocking on it deadlocks: its continuations would be
                // posted back to the very thread that is waiting. Running it on the thread pool keeps
                // every continuation off the UI thread while still completing before the first render.
                Task.Run(() => GuiTestScenarioSeed.SeedAsync(app.Services)).GetAwaiter().GetResult();
            }
#endif
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
