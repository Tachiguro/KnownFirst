using KnownFirst.Data;
using KnownFirst.Core.Language;
using KnownFirst.Services;
using Microsoft.Extensions.Logging;

namespace KnownFirst;

public static class MauiProgram
{
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

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        app.Services.GetRequiredService<ILanguageSelectionService>().Initialize();

        return app;
    }
}
