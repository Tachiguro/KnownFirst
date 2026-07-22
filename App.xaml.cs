using KnownFirst.Services;
using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KnownFirst;

public partial class App : Application
{
    private readonly INavigationHistoryService _navigationHistory;
    private readonly ILogger<App> _logger;
    private readonly ILogger<MainPage> _mainPageLogger;
    private readonly IAppDiagnosticsService _diagnostics;

    public App(
        IThemeService themeService,
        INavigationHistoryService navigationHistory,
        ILogger<App> logger,
        ILogger<MainPage> mainPageLogger,
        IAppDiagnosticsService diagnostics)
    {
        InitializeComponent();
        themeService.Initialize(this);
        _navigationHistory = navigationHistory;
        _logger = logger;
        _mainPageLogger = mainPageLogger;
        _diagnostics = diagnostics;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage(_navigationHistory, _mainPageLogger))
        {
            Title = "KnownFirst"
        };
        window.Created += (_, _) => _logger.LogDebug("The main application window was created.");
        window.Resumed += (_, _) => _logger.LogDebug("The main application window resumed.");
        window.Stopped += (_, _) => _logger.LogDebug("The main application window stopped.");
        window.Destroying += (_, _) =>
        {
            _logger.LogInformation(
                DiagnosticEventIds.Shutdown,
                "Application shutdown is beginning.");
            _diagnostics.Flush();
        };
        return window;
    }
}
