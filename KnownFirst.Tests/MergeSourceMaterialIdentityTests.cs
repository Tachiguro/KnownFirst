using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeSourceMaterialIdentityTests
{
    private static BackupSourceMaterial CreateMaterial(
        string id = "source-1",
        string title = "Some Title",
        string textLanguage = "en",
        string explanationLanguage = "de",
        BackupLexicalLookupMode lookupMode = BackupLexicalLookupMode.Translation,
        string? targetLanguage = "de",
        string contentSha256 = "ABC123",
        string originalText = "sample text") =>
        new(
            id,
            title,
            textLanguage,
            explanationLanguage,
            lookupMode,
            targetLanguage,
            originalText,
            contentSha256,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            1,
            [],
            []);

    [TestMethod]
    public void DefinitionMode_CanonicalizesTargetLanguageToEmpty()
    {
        var withTarget = CreateMaterial(lookupMode: BackupLexicalLookupMode.Definition, targetLanguage: "de");
        var withNullTarget = CreateMaterial(lookupMode: BackupLexicalLookupMode.Definition, targetLanguage: null);

        var identity1 = SourceMaterialIdentityPolicy.Compute(withTarget);
        var identity2 = SourceMaterialIdentityPolicy.Compute(withNullTarget);

        Assert.AreEqual(identity1, identity2, "Definition mode must canonicalize TargetLanguage to empty regardless of the raw archive value.");
    }

    [TestMethod]
    public void Title_DoesNotAffectIdentity()
    {
        var material1 = CreateMaterial(title: "Title A");
        var material2 = CreateMaterial(title: "Completely Different Title");

        Assert.AreEqual(SourceMaterialIdentityPolicy.Compute(material1), SourceMaterialIdentityPolicy.Compute(material2));
    }

    [TestMethod]
    public void SameTextAndConfiguration_Deduplicates()
    {
        var material1 = CreateMaterial(id: "a", contentSha256: "SAME_HASH");
        var material2 = CreateMaterial(id: "b", contentSha256: "SAME_HASH");

        Assert.AreEqual(SourceMaterialIdentityPolicy.Compute(material1), SourceMaterialIdentityPolicy.Compute(material2));
    }

    [TestMethod]
    public void SameText_DefinitionVersusTranslation_RemainsDistinct()
    {
        var definition = CreateMaterial(contentSha256: "SAME_HASH", lookupMode: BackupLexicalLookupMode.Definition, targetLanguage: "de");
        var translation = CreateMaterial(contentSha256: "SAME_HASH", lookupMode: BackupLexicalLookupMode.Translation, targetLanguage: "de");

        Assert.AreNotEqual(SourceMaterialIdentityPolicy.Compute(definition), SourceMaterialIdentityPolicy.Compute(translation));
    }

    [TestMethod]
    public void SameText_DifferentTargetLanguages_RemainsDistinct()
    {
        var toGerman = CreateMaterial(contentSha256: "SAME_HASH", lookupMode: BackupLexicalLookupMode.Translation, targetLanguage: "de");
        var toRussian = CreateMaterial(contentSha256: "SAME_HASH", lookupMode: BackupLexicalLookupMode.Translation, targetLanguage: "ru");

        Assert.AreNotEqual(SourceMaterialIdentityPolicy.Compute(toGerman), SourceMaterialIdentityPolicy.Compute(toRussian));
    }

    [TestMethod]
    public void ContentHashCase_DoesNotAffectIdentity()
    {
        var lower = CreateMaterial(contentSha256: "abc123def456");
        var upper = CreateMaterial(contentSha256: "ABC123DEF456");

        Assert.AreEqual(SourceMaterialIdentityPolicy.Compute(lower), SourceMaterialIdentityPolicy.Compute(upper));
    }

    [TestMethod]
    public void LanguageCodeCase_DoesNotAffectIdentity()
    {
        var lower = CreateMaterial(textLanguage: "en", targetLanguage: "de");
        var upper = CreateMaterial(textLanguage: "EN", targetLanguage: "DE");

        Assert.AreEqual(SourceMaterialIdentityPolicy.Compute(lower), SourceMaterialIdentityPolicy.Compute(upper));
    }

    [TestMethod]
    public void DifferentContent_ProducesDifferentIdentity()
    {
        var material1 = CreateMaterial(contentSha256: "HASH_ONE");
        var material2 = CreateMaterial(contentSha256: "HASH_TWO");

        Assert.AreNotEqual(SourceMaterialIdentityPolicy.Compute(material1), SourceMaterialIdentityPolicy.Compute(material2));
    }

    [TestMethod]
    public void DifferentTextLanguage_ProducesDifferentIdentity()
    {
        var english = CreateMaterial(textLanguage: "en");
        var german = CreateMaterial(textLanguage: "de");

        Assert.AreNotEqual(SourceMaterialIdentityPolicy.Compute(english), SourceMaterialIdentityPolicy.Compute(german));
    }
}
