using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;

namespace KnownFirst;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemBarTheme(Resources?.Configuration);
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplySystemBarTheme(newConfig);
    }

    private void ApplySystemBarTheme(Configuration? configuration)
    {
        var window = Window;
        var decorView = window?.DecorView;

        if (window is null || decorView is null)
        {
            return;
        }

        var isDark = (configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var backgroundColor = Android.Graphics.Color.ParseColor(isDark ? "#121916" : "#F4F7F5");

#pragma warning disable CA1422
        window.SetStatusBarColor(backgroundColor);
        window.SetNavigationBarColor(backgroundColor);
#pragma warning restore CA1422

        var insetsController = WindowCompat.GetInsetsController(window, decorView);
        if (insetsController is null)
        {
            return;
        }

        insetsController.AppearanceLightStatusBars = !isDark;
        insetsController.AppearanceLightNavigationBars = !isDark;
    }
}
