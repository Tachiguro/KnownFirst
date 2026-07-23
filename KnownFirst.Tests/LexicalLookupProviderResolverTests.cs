using KnownFirst.Core.Preparation;
using KnownFirst.Services.Lexical;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LexicalLookupProviderResolverTests
{
    private sealed class FakeProvider(string name, int version) : ILexicalLookupProvider
    {
        public string ProviderName => name;
        public int ProviderSchemaVersion => version;
        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [TestMethod]
    public void Constructor_RejectsEmptyProviderName()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            new LexicalLookupProviderResolver([new FakeProvider("   ", 1)]));
        StringAssert.Contains(ex.Message, "valid non-empty name");
    }

    [TestMethod]
    public void Constructor_RejectsDuplicateProviderNames()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            new LexicalLookupProviderResolver([
                new FakeProvider("Wiktionary", 1),
                new FakeProvider("wiktionary", 2)
            ]));
        StringAssert.Contains(ex.Message, "already registered");
    }

    [TestMethod]
    public void TryResolve_ReturnsNullForEmptyProviderName()
    {
        var resolver = new LexicalLookupProviderResolver([new FakeProvider("Wiktionary", 1)]);
        Assert.IsNull(resolver.TryResolve(""));
        Assert.IsNull(resolver.TryResolve("   "));
    }

    [TestMethod]
    public void TryResolve_IsCaseInsensitive()
    {
        var expected = new FakeProvider("Wiktionary", 1);
        var resolver = new LexicalLookupProviderResolver([expected]);
        
        Assert.AreSame(expected, resolver.TryResolve("wiktionary"));
        Assert.AreSame(expected, resolver.TryResolve("WIKTIONARY"));
        Assert.AreSame(expected, resolver.TryResolve("Wiktionary"));
    }

    [TestMethod]
    public void TryResolve_ReturnsNullForUnknownProvider()
    {
        var resolver = new LexicalLookupProviderResolver([new FakeProvider("Wiktionary", 1), new FakeProvider("Wikipedia", 1)]);
        Assert.IsNull(resolver.TryResolve("UnknownProvider"));
    }

    [TestMethod]
    public void Resolve_ThrowsForEmptyProviderName()
    {
        var resolver = new LexicalLookupProviderResolver([new FakeProvider("Wiktionary", 1)]);
        Assert.ThrowsExactly<ArgumentException>(() => resolver.Resolve(""));
    }

    [TestMethod]
    public void Resolve_ThrowsForUnknownProvider()
    {
        var resolver = new LexicalLookupProviderResolver([new FakeProvider("Wiktionary", 1), new FakeProvider("Wikipedia", 1)]);
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => resolver.Resolve("UnknownProvider"));
        StringAssert.Contains(ex.Message, "not registered");
    }

    [TestMethod]
    public void Resolve_ResolvesWikipedia()
    {
        var expected = new FakeProvider("Wikipedia", 1);
        var resolver = new LexicalLookupProviderResolver([new FakeProvider("Wiktionary", 1), expected]);
        Assert.AreSame(expected, resolver.Resolve("Wikipedia"));
    }

    [TestMethod]
    public void Resolve_ResolvesWiktionary()
    {
        var expected = new FakeProvider("Wiktionary", 1);
        var resolver = new LexicalLookupProviderResolver([expected, new FakeProvider("Wikipedia", 1)]);
        Assert.AreSame(expected, resolver.Resolve("Wiktionary"));
    }
}
