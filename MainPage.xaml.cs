using KnownFirst.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnownFirst;

public partial class MainPage : ContentPage
{
    private readonly INavigationHistoryService _navigationHistory;
    private readonly ILogger<MainPage> _logger;
    private bool _isNavigatingBack;

    public MainPage(
        INavigationHistoryService navigationHistory,
        ILogger<MainPage> logger)
    {
        InitializeComponent();
        _navigationHistory = navigationHistory;
        _logger = logger;
    }

    protected override bool OnBackButtonPressed()
    {
        if (_navigationHistory.TryDismissOverlay())
        {
            return true;
        }

        if (_navigationHistory.IsHome)
        {
            return base.OnBackButtonPressed();
        }

        if (!_isNavigatingBack)
        {
            _isNavigatingBack = true;
            _ = NavigateBackInBlazorAsync();
        }

        return true;
    }

    private static void OnBlazorWebViewInitialized(
        object? sender,
        BlazorWebViewInitializedEventArgs eventArgs)
    {
#if WINDOWS
        var webView = eventArgs.WebView;
        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            return;
        }

        webView.CoreWebView2Initialized += (_, _) =>
        {
            if (webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            }
        };
#endif
    }

    private async Task NavigateBackInBlazorAsync()
    {
        if (!_navigationHistory.TryBeginBackNavigation(out var targetRoute, out var sourceRoute))
        {
            _isNavigatingBack = false;
            return;
        }

        try
        {
            var dispatched = await blazorWebView.TryDispatchAsync(services =>
            {
                var navigationManager = services.GetRequiredService<NavigationManager>();
                navigationManager.NavigateTo(targetRoute, replace: true);
            });

            if (!dispatched)
            {
                _navigationHistory.CancelBackNavigation(sourceRoute);
                _logger.LogWarning("Android Back could not be dispatched because the Blazor WebView is not running.");
            }
        }
        catch (Exception exception)
        {
            _navigationHistory.CancelBackNavigation(sourceRoute);
            _logger.LogError(exception, "Android Back could not navigate within the application.");
        }
        finally
        {
            _isNavigatingBack = false;
        }
    }
}
