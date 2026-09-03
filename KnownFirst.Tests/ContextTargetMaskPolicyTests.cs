using System.Globalization;
using KnownFirst.Core.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class ContextTargetMaskPolicyTests
{
    [TestMethod]
    public void CreateMask_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.AreEqual(string.Empty, ContextTargetMaskPolicy.CreateMask(null));
        Assert.AreEqual(string.Empty, ContextTargetMaskPolicy.CreateMask(string.Empty));
    }

    [TestMethod]
    public void CreateMask_StandardAscii_ReturnsExactUnderscoreLength()
    {
        // 5 characters
        Assert.AreEqual("_____", ContextTargetMaskPolicy.CreateMask("house"));

        // 3 characters
        Assert.AreEqual("___", ContextTargetMaskPolicy.CreateMask("cat"));

        // 10 characters
        Assert.AreEqual("__________", ContextTargetMaskPolicy.CreateMask("Wohnzimmer"));
    }

    [TestMethod]
    public void CreateMask_ComposedAccentedCharacters_CountsEachGraphemeAsOne()
    {
        // "schön": 5 text elements (s, c, h, ö, n)
        Assert.AreEqual("_____", ContextTargetMaskPolicy.CreateMask("schön"));

        // "Straße": 6 text elements (S, t, r, a, ß, e)
        Assert.AreEqual("______", ContextTargetMaskPolicy.CreateMask("Straße"));

        // "café": 4 text elements (c, a, f, é)
        Assert.AreEqual("____", ContextTargetMaskPolicy.CreateMask("café"));
    }

    [TestMethod]
    public void CreateMask_DecomposedCombiningMarks_CountsBasePlusCombiningAsOneTextElement()
    {
        // "scho\u0308n": 'o' + combining diaeresis U+0308 = 1 text element, total 5 text elements
        var decomposedSchon = "scho\u0308n";
        Assert.AreEqual(6, decomposedSchon.Length); // 6 UTF-16 code units
        Assert.AreEqual(5, new StringInfo(decomposedSchon).LengthInTextElements);
        Assert.AreEqual("_____", ContextTargetMaskPolicy.CreateMask(decomposedSchon));

        // "cafe\u0301": 'e' + combining acute U+0301 = 1 text element, total 4 text elements
        var decomposedCafe = "cafe\u0301";
        Assert.AreEqual(5, decomposedCafe.Length); // 5 UTF-16 code units
        Assert.AreEqual(4, new StringInfo(decomposedCafe).LengthInTextElements);
        Assert.AreEqual("____", ContextTargetMaskPolicy.CreateMask(decomposedCafe));
    }

    [TestMethod]
    public void CreateMask_SupplementaryPlaneSurrogatePair_CountsAsSingleTextElement()
    {
        // "😀" (U+1F600 Grinning Face): 2 UTF-16 code units, 1 text element
        var emoji = "\uD83D\uDE00";
        Assert.AreEqual(2, emoji.Length);
        Assert.AreEqual(1, new StringInfo(emoji).LengthInTextElements);
        Assert.AreEqual("_", ContextTargetMaskPolicy.CreateMask(emoji));

        // "Word😀": 4 ASCII chars + 1 emoji = 5 text elements
        var wordWithEmoji = "Word" + emoji;
        Assert.AreEqual(6, wordWithEmoji.Length);
        Assert.AreEqual(5, new StringInfo(wordWithEmoji).LengthInTextElements);
        Assert.AreEqual("_____", ContextTargetMaskPolicy.CreateMask(wordWithEmoji));
    }

    [TestMethod]
    public void CreateMask_PunctuationAndHyphenatedWords_MasksEveryTextElement()
    {
        // "Wi-Fi": 5 text elements including hyphen
        Assert.AreEqual("_____", ContextTargetMaskPolicy.CreateMask("Wi-Fi"));

        // "e-mail": 6 text elements
        Assert.AreEqual("______", ContextTargetMaskPolicy.CreateMask("e-mail"));
    }

    [TestMethod]
    public void CreateMask_CustomMaskCharacter_UsesSpecifiedCharacter()
    {
        Assert.AreEqual("***", ContextTargetMaskPolicy.CreateMask("cat", '*'));
        Assert.AreEqual("#####", ContextTargetMaskPolicy.CreateMask("house", '#'));
    }
}
