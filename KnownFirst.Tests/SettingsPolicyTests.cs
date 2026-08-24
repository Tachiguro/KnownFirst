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
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(10)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(20)]
    [DataRow(25)]
    [DataRow(30)]
    [DataRow(50)]
    public void Normalize_WhenPreparationLimitIsWithinOneToFifty_ReturnsValueUnchanged(int validLimit)
    {
        Assert.AreEqual(validLimit, PreparationLimitPolicy.Normalize(validLimit));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(-50)]
    [DataRow(51)]
    [DataRow(99)]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void Normalize_WhenPreparationLimitIsOutsideRange_ReturnsProductDefaultFive(int outOfRangeLimit)
    {
        Assert.AreEqual(5, PreparationLimitPolicy.Normalize(outOfRangeLimit));
    }

    [TestMethod]
    public void PreparationLimitPolicy_ConstantsAndPresetsMatchContract()
    {
        Assert.AreEqual(1, PreparationLimitPolicy.MinimumLimit);
        Assert.AreEqual(50, PreparationLimitPolicy.MaximumLimit);
        Assert.AreEqual(5, PreparationLimitPolicy.DefaultLimit);
        Assert.AreEqual(5, PreparationLimitPolicy.RecommendedLimit);
        Assert.AreEqual(15, PreparationLimitPolicy.HighBudgetWarningThreshold);
        CollectionAssert.AreEqual(new[] { 1, 5, 10 }, PreparationLimitPolicy.Presets.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 5, 10 }, PreparationLimitPolicy.SupportedLimits.ToArray());
    }

    [TestMethod]
    [DataRow(1, true)]
    [DataRow(5, true)]
    [DataRow(10, true)]
    [DataRow(0, false)]
    [DataRow(2, false)]
    [DataRow(15, false)]
    [DataRow(20, false)]
    [DataRow(30, false)]
    [DataRow(50, false)]
    [DataRow(99, false)]
    public void IsPreset_IdentifiesExactlyPresetsOneFiveTen(int value, bool expectedPreset)
    {
        Assert.AreEqual(expectedPreset, PreparationLimitPolicy.IsPreset(value));
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(5, false)]
    [DataRow(10, false)]
    [DataRow(15, false)]
    [DataRow(16, true)]
    [DataRow(20, true)]
    [DataRow(30, true)]
    [DataRow(50, true)]
    [DataRow(0, false)]
    [DataRow(-1, false)]
    [DataRow(51, false)]
    [DataRow(99, false)]
    public void RequiresHighBudgetWarning_ReturnsTrueOnlyForValidValuesAboveFifteen(int value, bool expectedWarning)
    {
        Assert.AreEqual(expectedWarning, PreparationLimitPolicy.RequiresHighBudgetWarning(value));
    }

    [TestMethod]
    [DataRow(1, true)]
    [DataRow(5, true)]
    [DataRow(15, true)]
    [DataRow(50, true)]
    [DataRow(0, false)]
    [DataRow(-1, false)]
    [DataRow(51, false)]
    public void IsValid_ReturnsTrueOnlyForValuesWithinOneToFifty(int value, bool expectedValid)
    {
        Assert.AreEqual(expectedValid, PreparationLimitPolicy.IsValid(value));
    }

}
