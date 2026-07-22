using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.IO;
using KnownFirst.Services.Lexical.Wikipedia;
using KnownFirst.Services.Lexical;

namespace KnownFirst.Tests;

[TestClass]
public class WikipediaArchitectureTests
{
    private static readonly string SolutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [TestMethod]
    public void Architecture_WikipediaApiClient_DoesNotImplementILexicalLookupProvider()
    {
        Assert.IsFalse(typeof(ILexicalLookupProvider).IsAssignableFrom(typeof(WikipediaApiClient)));
    }

    [TestMethod]
    public void Architecture_WikipediaLookupProvider_DoesNotExist()
    {
        var type = typeof(WikipediaApiClient).Assembly.GetType("KnownFirst.Services.Lexical.Wikipedia.WikipediaLookupProvider");
        Assert.IsNull(type);
    }

    [TestMethod]
    public void Architecture_LexicalLookupProviderResolver_DoesNotContainWikipedia()
    {
        var text = File.ReadAllText(Path.Combine(SolutionDir, "KnownFirst", "Services", "Lexical", "LexicalLookupProviderResolver.cs"));
        Assert.IsFalse(text.Contains("Wikipedia"));
    }

    [TestMethod]
    public void Architecture_LexicalEnrichmentService_NotChangedForFallback()
    {
        var text = File.ReadAllText(Path.Combine(SolutionDir, "KnownFirst", "Services", "Lexical", "LexicalEnrichmentService.cs"));
        Assert.IsFalse(text.Contains("Wikipedia"));
    }

    [TestMethod]
    public void Architecture_NoAngleSharpInWikipedia()
    {
        var csproj = File.ReadAllText(Path.Combine(SolutionDir, "KnownFirst", "KnownFirst.csproj"));
        // AngleSharp is not in Wikipedia
        var text = File.ReadAllText(Path.Combine(SolutionDir, "KnownFirst", "Services", "Lexical", "Wikipedia", "WikipediaApiClient.cs"));
        Assert.IsFalse(text.Contains("AngleSharp"));
    }

    [TestMethod]
    public void Architecture_NoJsonDocumentOrNode()
    {
        var text = File.ReadAllText(Path.Combine(SolutionDir, "KnownFirst", "Services", "Lexical", "Wikipedia", "WikipediaApiClient.cs"));
        Assert.IsFalse(text.Contains("JsonDocument"));
        Assert.IsFalse(text.Contains("JsonNode"));
    }

    [TestMethod]
    public void Architecture_NoReflectionDeserialization()
    {
        var text = File.ReadAllText(Path.Combine(SolutionDir, "KnownFirst", "Services", "Lexical", "Wikipedia", "WikipediaApiClient.cs"));
        Assert.IsTrue(text.Contains("WikipediaJsonSerializerContext.Default"));
    }

    [TestMethod]
    public void Architecture_SchemaVersionIs7()
    {
        var text = File.ReadAllText(Path.Combine(SolutionDir, "KnownFirst", "Data", "DatabaseSchema.cs"));
        Assert.IsTrue(text.Contains("public const int CurrentVersion = 7;"));
    }
}
