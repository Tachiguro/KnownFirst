using KnownFirst.Core.Text;
using KnownFirst.Services.Lexical;

namespace KnownFirst.Tests;

/// <summary>
/// German Enhanced Term Recognition Package 2: contract for the app-side production
/// <see cref="IGermanLexicon"/> wrapper backed by the embedded <c>german-lexicon.v2.kfgl</c>
/// resource. Exercises the real shipped bundle bytes (embedded into this test assembly under the
/// identical logical name KnownFirst.csproj uses), never a second copy of the file.
/// </summary>
[TestClass]
public sealed class BundledGermanLexiconTests
{
    [TestMethod]
    public void TryLookupLemma_RealEmbeddedBundle_ResolvesMaschine()
    {
        var lexicon = new BundledGermanLexicon();

        Assert.IsTrue(lexicon.TryLookupLemma("Maschine", out var entry));
        Assert.AreEqual("Maschine", entry!.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, entry.Category);
    }

    [TestMethod]
    public void TryLookupStem_RealEmbeddedBundle_ResolvesSchreibToSchreiben()
    {
        var lexicon = new BundledGermanLexicon();

        Assert.IsTrue(lexicon.TryLookupStem("Schreib", out var entry));
        Assert.AreEqual("schreiben", entry!.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, entry.Category);
    }

    [TestMethod]
    public void TryLookupLemma_UnknownForm_FailsClosedRatherThanThrowing()
    {
        var lexicon = new BundledGermanLexicon();

        Assert.IsFalse(lexicon.TryLookupLemma("Zeppelinwerft", out var entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void ResourceName_IsExplicitStableAndPresentInThisAssembly()
    {
        Assert.AreEqual(
            "KnownFirst.Resources.German.german-lexicon.v2.kfgl",
            BundledGermanLexicon.ResourceName);

        var resourceNames = typeof(BundledGermanLexicon).Assembly.GetManifestResourceNames();
        CollectionAssert.Contains(resourceNames, BundledGermanLexicon.ResourceName);
    }

    [TestMethod]
    public void Construction_DoesNotOpenTheResourceStream()
    {
        var openCount = 0;

        _ = new BundledGermanLexicon(() =>
        {
            openCount++;
            return OpenRealResourceStream();
        });

        Assert.AreEqual(0, openCount);
    }

    [TestMethod]
    public void RepeatedLookups_OpenTheResourceStreamOnlyOnce()
    {
        var openCount = 0;
        var lexicon = new BundledGermanLexicon(() =>
        {
            openCount++;
            return OpenRealResourceStream();
        });

        lexicon.TryLookupLemma("Maschine", out _);
        lexicon.TryLookupLemma("Arbeit", out _);
        lexicon.TryLookupStem("Schreib", out _);
        lexicon.TryLookupLemma("NichtVorhanden", out _);

        Assert.AreEqual(1, openCount);
    }

    [TestMethod]
    public void MissingResource_FailsExplicitlyOnFirstLookupNotOnConstruction()
    {
        var lexicon = new BundledGermanLexicon(() =>
            throw new InvalidOperationException("Simulated missing embedded resource."));

        // Reaching this line proves construction alone did not force the (failing) load.
        Assert.ThrowsExactly<InvalidOperationException>(() => lexicon.TryLookupLemma("Maschine", out _));
    }

    private static Stream OpenRealResourceStream() =>
        typeof(BundledGermanLexicon).Assembly.GetManifestResourceStream(BundledGermanLexicon.ResourceName)!;
}
