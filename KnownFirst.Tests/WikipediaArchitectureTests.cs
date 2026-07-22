using KnownFirst.Services.Lexical;
using KnownFirst.Services.Lexical.Wikipedia;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public class WikipediaArchitectureTests
{
    private static readonly string RepositoryDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "KnownFirst"));

    [TestMethod]
    public void Architecture_WikipediaApiClient_DoesNotImplementILexicalLookupProvider()
    {
        Assert.IsFalse(typeof(ILexicalLookupProvider).IsAssignableFrom(typeof(WikipediaApiClient)));
    }

    [TestMethod]
    public void Architecture_WikipediaLookupProvider_ExistsAndIsRegistered()
    {
        var type = typeof(WikipediaApiClient).Assembly.GetType("KnownFirst.Services.Lexical.Wikipedia.WikipediaLookupProvider");
        Assert.IsNotNull(type);
        
        Assert.IsTrue(typeof(ILexicalLookupProvider).IsAssignableFrom(type));
        Assert.IsFalse(typeof(IDictionaryLookupProvider).IsAssignableFrom(type));
    }

    [TestMethod]
    public void Architecture_WikipediaProductionFiles_DoNotUseForbiddenApis()
    {
        var wikipediaDir = Path.Combine(RepositoryDir, "Services", "Lexical", "Wikipedia");
        var texts = Directory.EnumerateFiles(wikipediaDir, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .ToArray();

        Assert.AreNotEqual(0, texts.Length);
        AssertForbidden(texts, "AngleSharp");
        AssertForbidden(texts, "QuerySelector");
        AssertForbidden(texts, "JsonDocument");
        AssertForbidden(texts, "JsonNode");
        AssertForbidden(texts, "dynamic");
        AssertForbidden(texts, "list=search");
        AssertForbidden(texts, "http://");
    }

    [TestMethod]
    public void Architecture_WikipediaDeserialization_UsesSourceGeneratedContext()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryDir, "Services", "Lexical", "Wikipedia", "WikipediaApiClient.cs"));

        StringAssert.Contains(
            text,
            "JsonSerializer.DeserializeAsync(stream, WikipediaJsonSerializerContext.Default.WikipediaApiResponse, cts.Token)");
    }

    [TestMethod]
    public void Architecture_MauiProgram_RegistersWikipediaProvider()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryDir, "MauiProgram.cs"));

        Assert.IsTrue(text.Contains("WikipediaApiClient", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("IWikipediaApiClient", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("WikipediaLookupProvider", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Architecture_LexicalLookupProviderResolver_DoesNotContainWikipedia()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryDir, "Services", "Lexical", "LexicalLookupProviderResolver.cs"));

        Assert.IsFalse(text.Contains("Wikipedia", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Architecture_LexicalEnrichmentService_NotChangedForFallback()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryDir, "Services", "Lexical", "LexicalEnrichmentService.cs"));

        Assert.IsFalse(text.Contains("Wikipedia", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Architecture_SchemaVersionIs7()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryDir, "Data", "DatabaseSchema.cs"));

        Assert.IsTrue(text.Contains("public const int CurrentVersion = 7;", StringComparison.Ordinal));
    }

    private static void AssertForbidden(IEnumerable<(string Path, string Text)> files, string forbidden)
    {
        foreach (var file in files)
        {
            Assert.IsFalse(
                file.Text.Contains(forbidden, StringComparison.Ordinal),
                $"{forbidden} must not appear in {file.Path}.");
        }
    }
}
