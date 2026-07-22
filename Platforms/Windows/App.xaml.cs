using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnownFirst.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        private static void OnUnhandledException(
            object sender,
            Microsoft.UI.Xaml.UnhandledExceptionEventArgs eventArgs)
        {
            try
            {
                var services = IPlatformApplication.Current?.Services;
                services?.GetService<ILogger<App>>()?.LogCritical(
                    DiagnosticEventIds.WinUiUnhandledException,
                    eventArgs.Exception,
                    "An unhandled WinUI exception occurred.");
                services?.GetService<IAppDiagnosticsService>()?.Flush();
            }
            catch
            {
                // Fatal diagnostics are best effort and must not mask the original exception.
            }
        }
    }

}
