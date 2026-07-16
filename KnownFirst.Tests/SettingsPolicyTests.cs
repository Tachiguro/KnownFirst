using KnownFirst.Core.Settings;

namespace KnownFirst.Tests;

[TestClass]
public sealed class SettingsPolicyTests
{
    [TestMethod]
    public void Normalize_WhenThemeIsSystem_ReturnsSystem()
    {
        Assert.AreEqual(ThemePreference.System, ThemePreferencePolicy.Normalize(0));
    }

    [TestMethod]
    public void Normalize_WhenThemeIsLight_ReturnsLight()
    {
        Assert.AreEqual(ThemePreference.Light, ThemePreferencePolicy.Normalize(1));
    }

    [TestMethod]
    public void Normalize_WhenThemeIsDark_ReturnsDark()
    {
        Assert.AreEqual(ThemePreference.Dark, ThemePreferencePolicy.Normalize(2));
    }

    [TestMethod]
    public void Normalize_WhenThemeIsInvalid_ReturnsSystem()
    {
        Assert.AreEqual(ThemePreference.System, ThemePreferencePolicy.Normalize(99));
    }

    [TestMethod]
    public void Normalize_WhenPreparationLimitIsFive_ReturnsFive()
    {
        Assert.AreEqual(5, PreparationLimitPolicy.Normalize(5));
    }

    [TestMethod]
    public void Normalize_WhenPreparationLimitIsTen_ReturnsTen()
    {
        Assert.AreEqual(10, PreparationLimitPolicy.Normalize(10));
    }

    [TestMethod]
    public void Normalize_WhenPreparationLimitIsTwenty_ReturnsTwenty()
    {
        Assert.AreEqual(20, PreparationLimitPolicy.Normalize(20));
    }

    [TestMethod]
    public void Normalize_WhenPreparationLimitIsFifty_ReturnsFifty()
    {
        Assert.AreEqual(50, PreparationLimitPolicy.Normalize(50));
    }

    [TestMethod]
    public void Normalize_WhenPreparationLimitIsUnsupported_ReturnsTen()
    {
        Assert.AreEqual(10, PreparationLimitPolicy.Normalize(25));
    }

}
