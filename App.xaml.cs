using KnownFirst.Services;
using Microsoft.Extensions.Logging;

namespace KnownFirst;

public partial class App : Application
{
    private readonly INavigationHistoryService _navigationHistory;
    private readonly ILogger<MainPage> _logger;

    public App(
        IThemeService themeService,
        INavigationHistoryService navigationHistory,
        ILogger<MainPage> logger)
    {
        InitializeComponent();
        themeService.Initialize(this);
        _navigationHistory = navigationHistory;
        _logger = logger;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage(_navigationHistory, _logger)) { Title = "KnownFirst" };
    }
}
