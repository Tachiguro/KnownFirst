using KnownFirst.Tools.GermanLexicon;

namespace KnownFirst.Tests;

/// <summary>F4 contract: only an exact, fully-qualified 40-hex-character Git commit SHA is accepted.</summary>
[TestClass]
public sealed class UpstreamCommitValidationTests
{
    [TestMethod]
    public void EnsureValid_Exact40HexCharacters_DoesNotThrow()
    {
        UpstreamCommitValidation.EnsureValid("1780890c0fd25a989201c96000af323cd201fa5c");
    }

    [TestMethod]
    public void EnsureValid_TooShort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid("1780890c0fd25a989201c96000af323cd201fa5"));
    }

    [TestMethod]
    public void EnsureValid_TooLong_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid("1780890c0fd25a989201c96000af323cd201fa5cc"));
    }

    [TestMethod]
    public void EnsureValid_NonHexCharacters_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid("g780890c0fd25a989201c96000af323cd201fa5c"));
    }

    [TestMethod]
    public void EnsureValid_UppercaseHex_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid("1780890C0FD25A989201C96000AF323CD201FA5C"));
    }

    [TestMethod]
    public void EnsureValid_BranchName_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid("main"));
    }

    [TestMethod]
    public void EnsureValid_RefName_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid("refs/heads/main"));
    }

    [TestMethod]
    public void EnsureValid_AbbreviatedHash_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid("1780890"));
    }

    [TestMethod]
    public void EnsureValid_EmptyString_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpstreamCommitValidation.EnsureValid(""));
    }
}
