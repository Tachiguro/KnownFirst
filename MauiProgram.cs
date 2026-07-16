using KnownFirst.Data;
using KnownFirst.Core.Language;
using KnownFirst.Core.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Services;
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
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
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
        builder.Services.AddSingleton<TextAnalyzer>();
        builder.Services.AddSingleton<ITextReviewService, TextReviewService>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<ISpacedRepetitionScheduler, SimpleSpacedRepetitionScheduler>();
        builder.Services.AddSingleton<SpellingAnswerComparer>();
        builder.Services.AddSingleton<AcronymExpansionDetector>();
        builder.Services.AddSingleton<MeaningRanker>();
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
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        app.Services.GetRequiredService<ILanguageSelectionService>().Initialize();

        return app;
    }
}
