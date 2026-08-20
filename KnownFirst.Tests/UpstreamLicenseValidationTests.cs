using KnownFirst.Tools.GermanLexicon;

namespace KnownFirst.Tests;

/// <summary>
/// F5 contract: upstream LICENSE text must still designate CC BY-SA 4.0 (Attribution-ShareAlike
/// 4.0) before generation may proceed; a stale, missing, or changed marker fails closed.
/// </summary>
[TestClass]
public sealed class UpstreamLicenseValidationTests
{
    [TestMethod]
    public void EnsureValid_CurrentCcBySa40LicenseText_DoesNotThrow()
    {
        const string licenseText =
            "Attribution-ShareAlike 4.0 International\n\n" +
            "=======================================================================\n\n" +
            "Creative Commons Corporation (\"Creative Commons\") is not a law firm...";

        UpstreamLicenseValidation.EnsureValid(licenseText);
    }

    [TestMethod]
    public void EnsureValid_StaleMitOnlyText_ThrowsInvalidOperationException()
    {
        const string staleMitText =
            "MIT License\n\n" +
            "Copyright (c) Some Author\n\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy...";

        Assert.ThrowsExactly<InvalidOperationException>(() => UpstreamLicenseValidation.EnsureValid(staleMitText));
    }

    [TestMethod]
    public void EnsureValid_TextMissingExpectedMarker_ThrowsInvalidOperationException()
    {
        const string unrelatedText = "This is some unrelated license text that names no known license family.";

        Assert.ThrowsExactly<InvalidOperationException>(() => UpstreamLicenseValidation.EnsureValid(unrelatedText));
    }

    [TestMethod]
    public void EnsureValid_EmptyString_ThrowsInvalidOperationException()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => UpstreamLicenseValidation.EnsureValid(""));
    }

    [TestMethod]
    public void EnsureValid_Null_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => UpstreamLicenseValidation.EnsureValid(null!));
    }
}
