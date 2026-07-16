using KnownFirst.Core.Language;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LanguagePreferencePolicyTests
{
    [TestMethod]
    public void Resolve_WhenSavedEnglishAndDeviceGerman_ReturnsEnglish()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Resolve("en", "de-DE"));
    }

    [TestMethod]
    public void Resolve_WhenSavedGermanAndDeviceEnglish_ReturnsGerman()
    {
        Assert.AreEqual("de", LanguagePreferencePolicy.Resolve("de", "en-US"));
    }

    [TestMethod]
    public void Resolve_WhenNoSavedValueAndDeviceGerman_ReturnsGerman()
    {
        Assert.AreEqual("de", LanguagePreferencePolicy.Resolve(null, "de-DE"));
    }

    [TestMethod]
    public void Resolve_WhenNoSavedValueAndDeviceEnglish_ReturnsEnglish()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Resolve(null, "en-US"));
    }

    [TestMethod]
    public void Resolve_WhenNoSavedValueAndDeviceFrench_ReturnsEnglish()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Resolve(null, "fr-FR"));
    }

    [TestMethod]
    public void Resolve_WhenSavedValueIsInvalid_ReturnsEnglish()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Resolve("fr", "de-DE"));
    }

    [TestMethod]
    public void Normalize_WhenCultureIsDeDe_ReturnsGerman()
    {
        Assert.AreEqual("de", LanguagePreferencePolicy.Normalize("de-DE"));
    }

    [TestMethod]
    public void Normalize_WhenCultureIsDeAt_ReturnsGerman()
    {
        Assert.AreEqual("de", LanguagePreferencePolicy.Normalize("de-AT"));
    }

    [TestMethod]
    public void Normalize_WhenCultureIsEnUs_ReturnsEnglish()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Normalize("en-US"));
    }

    [TestMethod]
    public void Normalize_WhenCultureIsEnGb_ReturnsEnglish()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Normalize("en-GB"));
    }

    [TestMethod]
    public void Normalize_WhenCultureIsUnsupported_ReturnsEnglish()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Normalize("pl-PL"));
    }

    [TestMethod]
    public void Normalize_WhenValueContainsWhitespace_NormalizesValue()
    {
        Assert.AreEqual("de", LanguagePreferencePolicy.Normalize("  de-DE  "));
    }

    [TestMethod]
    public void Normalize_WhenValueUsesDifferentCase_NormalizesValue()
    {
        Assert.AreEqual("en", LanguagePreferencePolicy.Normalize("EN-gb"));
    }
}
