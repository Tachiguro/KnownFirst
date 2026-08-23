using System.Globalization;
using KnownFirst.Core.Settings;
using KnownFirst.Services.Time;

namespace KnownFirst.Tests;

/// <summary>
/// Focused contract tests for the curated learning-timezone catalog and its deterministic
/// user-facing label formatting. Every assertion uses fixed UTC instants so the results never
/// depend on the developer wall clock or on OS-provided display names.
/// </summary>
[TestClass]
public sealed class LearningTimezoneCatalogTests
{
    private static readonly DateTime NorthernSummerInstantUtc =
        new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime NorthernWinterInstantUtc =
        new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Catalog_IsNotEmpty()
    {
        Assert.IsGreaterThan(0, LearningTimezoneCatalog.Options.Count);
    }

    [TestMethod]
    public void Catalog_ContainsNoDuplicateTimezoneIds()
    {
        var duplicates = LearningTimezoneCatalog.Options
            .GroupBy(option => option.TimezoneId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.IsEmpty(duplicates, "Duplicate catalog timezone IDs: " + string.Join(", ", duplicates));
    }

    [TestMethod]
    public void Catalog_ContainsNoDuplicateCityResourceKeys()
    {
        var duplicates = LearningTimezoneCatalog.Options
            .GroupBy(option => option.CityResourceKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.IsEmpty(duplicates, "Duplicate catalog city resource keys: " + string.Join(", ", duplicates));
    }

    [TestMethod]
    public void Catalog_StoresCanonicalTimezoneIdentityAndNeverAUtcOffset()
    {
        foreach (var option in LearningTimezoneCatalog.Options)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(option.TimezoneId),
                "A catalog entry must declare a timezone ID.");
            Assert.DoesNotContain("+", option.TimezoneId, StringComparison.Ordinal);
            Assert.DoesNotContain(":", option.TimezoneId, StringComparison.Ordinal);
            Assert.IsFalse(
                option.TimezoneId.StartsWith("GMT", StringComparison.Ordinal),
                option.TimezoneId + " looks like a fixed-offset identity rather than a canonical zone.");

            if (!string.Equals(option.TimezoneId, "UTC", StringComparison.Ordinal))
            {
                Assert.Contains("/", option.TimezoneId, StringComparison.Ordinal);
            }
        }
    }

    [TestMethod]
    public void Catalog_UsesTheSharedCityResourceKeyPrefix()
    {
        foreach (var option in LearningTimezoneCatalog.Options)
        {
            Assert.StartsWith(
                LearningTimezoneCatalog.CityResourceKeyPrefix,
                option.CityResourceKey,
                StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void Catalog_EveryCuratedIdResolvesOnTheCurrentPlatform()
    {
        var unresolved = new List<string>();

        foreach (var option in LearningTimezoneCatalog.Options)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(option.TimezoneId);
                Assert.IsNotNull(zone);
            }
            catch (TimeZoneNotFoundException)
            {
                unresolved.Add(option.TimezoneId);
            }
            catch (InvalidTimeZoneException)
            {
                unresolved.Add(option.TimezoneId);
            }
        }

        Assert.IsEmpty(unresolved, "Unresolvable catalog timezone IDs: " + string.Join(", ", unresolved));
    }

    [TestMethod]
    public void Catalog_ContainsTimezoneIdRecognisesCuratedAndRejectsUnknownIdentities()
    {
        Assert.IsTrue(LearningTimezoneCatalog.ContainsTimezoneId("Europe/Berlin"));
        Assert.IsFalse(LearningTimezoneCatalog.ContainsTimezoneId("Not/AZone"));
        Assert.IsFalse(LearningTimezoneCatalog.ContainsTimezoneId(null));
        Assert.IsFalse(LearningTimezoneCatalog.ContainsTimezoneId("   "));
    }

    [TestMethod]
    public void Catalog_CoversAtLeastTwoIndependentDaylightSavingZones()
    {
        Assert.IsTrue(LearningTimezoneCatalog.ContainsTimezoneId("Europe/Berlin"));
        Assert.IsTrue(LearningTimezoneCatalog.ContainsTimezoneId("America/New_York"));
    }

    [TestMethod]
    public void FormatUtcOffset_IsDeterministicAndCultureIndependent()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.AreEqual("UTC+00:00", LearningTimezoneLabelFormatter.FormatUtcOffset(TimeSpan.Zero));
            Assert.AreEqual("UTC+02:00", LearningTimezoneLabelFormatter.FormatUtcOffset(TimeSpan.FromHours(2)));
            Assert.AreEqual("UTC+05:30", LearningTimezoneLabelFormatter.FormatUtcOffset(TimeSpan.FromMinutes(330)));
            Assert.AreEqual("UTC-05:00", LearningTimezoneLabelFormatter.FormatUtcOffset(TimeSpan.FromHours(-5)));
            Assert.AreEqual("UTC-03:30", LearningTimezoneLabelFormatter.FormatUtcOffset(TimeSpan.FromMinutes(-210)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [TestMethod]
    public void FormatOptionLabel_UsesTheProductLabelShape()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        Assert.AreEqual(
            "(UTC+02:00) Berlin",
            LearningTimezoneLabelFormatter.FormatOptionLabel(berlin, NorthernSummerInstantUtc, "Berlin"));
    }

    [TestMethod]
    public void FormatOptionLabel_EuropeBerlinFollowsDaylightSavingTimeAtFixedInstants()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        Assert.AreEqual(
            "(UTC+02:00) Berlin",
            LearningTimezoneLabelFormatter.FormatOptionLabel(berlin, NorthernSummerInstantUtc, "Berlin"));
        Assert.AreEqual(
            "(UTC+01:00) Berlin",
            LearningTimezoneLabelFormatter.FormatOptionLabel(berlin, NorthernWinterInstantUtc, "Berlin"));
    }

    [TestMethod]
    public void FormatOptionLabel_AmericaNewYorkFollowsDaylightSavingTimeAtFixedInstants()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        Assert.AreEqual(
            "(UTC-04:00) New York",
            LearningTimezoneLabelFormatter.FormatOptionLabel(newYork, NorthernSummerInstantUtc, "New York"));
        Assert.AreEqual(
            "(UTC-05:00) New York",
            LearningTimezoneLabelFormatter.FormatOptionLabel(newYork, NorthernWinterInstantUtc, "New York"));
    }

    [TestMethod]
    public void FormatOptionLabel_NonWholeHourAndZeroOffsetZonesAreRenderedCorrectly()
    {
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var utc = TimeZoneInfo.FindSystemTimeZoneById("UTC");

        Assert.AreEqual(
            "(UTC+05:30) Kolkata",
            LearningTimezoneLabelFormatter.FormatOptionLabel(kolkata, NorthernSummerInstantUtc, "Kolkata"));
        Assert.AreEqual(
            "(UTC+00:00) UTC",
            LearningTimezoneLabelFormatter.FormatOptionLabel(utc, NorthernWinterInstantUtc, "UTC"));
    }

    [TestMethod]
    public void Resolver_ResolvesExplicitCuratedIdentityWithoutFallingBackToSystem()
    {
        var resolver = new LearningTimezoneResolver();

        var resolved = resolver.ResolveEffectiveTimeZone(LearningTimezoneMode.Explicit, "Asia/Tokyo");

        Assert.AreEqual(TimeSpan.FromHours(9), resolved.GetUtcOffset(NorthernSummerInstantUtc));
        Assert.AreEqual(TimeSpan.FromHours(9), resolved.GetUtcOffset(NorthernWinterInstantUtc));
    }

    [TestMethod]
    public void Resolver_PreservesDefensiveSystemFallbackForUnresolvableStoredIdentities()
    {
        var resolver = new LearningTimezoneResolver();

        var resolved = resolver.ResolveEffectiveTimeZone(LearningTimezoneMode.Explicit, "Not/AZone");

        Assert.AreEqual(TimeZoneInfo.Local.Id, resolved.Id);
    }

    [TestMethod]
    public void Resolver_SystemModeUsesTheOperatingSystemTimeZoneOnly()
    {
        var resolver = new LearningTimezoneResolver();

        var resolved = resolver.ResolveEffectiveTimeZone(LearningTimezoneMode.System, "Asia/Tokyo");

        Assert.AreEqual(TimeZoneInfo.Local.Id, resolved.Id);
        Assert.AreEqual(TimeZoneInfo.Local.Id, resolver.GetSystemTimeZoneId());
    }
}
