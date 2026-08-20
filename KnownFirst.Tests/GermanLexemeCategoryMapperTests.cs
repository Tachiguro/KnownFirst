using KnownFirst.Core.Text;
using KnownFirst.Tools.GermanLexicon;

namespace KnownFirst.Tests;

[TestClass]
public sealed class GermanLexemeCategoryMapperTests
{
    [TestMethod]
    public void Map_SupportedCategories_ReturnMatchingLexemeCategory()
    {
        Assert.AreEqual(GermanLexemeCategory.Noun, GermanLexemeCategoryMapper.Map("NN"));
        Assert.AreEqual(GermanLexemeCategory.Verb, GermanLexemeCategoryMapper.Map("V"));
        Assert.AreEqual(GermanLexemeCategory.Adjective, GermanLexemeCategoryMapper.Map("ADJ"));
    }

    [TestMethod]
    public void Map_UnsupportedUpstreamCategories_FailClosedToNull()
    {
        Assert.IsNull(GermanLexemeCategoryMapper.Map("ART"));
        Assert.IsNull(GermanLexemeCategoryMapper.Map("NNP"));
        Assert.IsNull(GermanLexemeCategoryMapper.Map("ADV"));
        Assert.IsNull(GermanLexemeCategoryMapper.Map("TRUNC"));
    }
}
