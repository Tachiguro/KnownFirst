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
        Assert.IsTrue(ex.Message.Contains("valid non-empty name"));
    }

    [TestMethod]
    public void Constructor_RejectsDuplicateProviderNames()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            new LexicalLookupProviderResolver([
                new FakeProvider("Wiktionary", 1),
                new FakeProvider("wiktionary", 2)
            ]));
        Assert.IsTrue(ex.Message.Contains("already registered"));
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
        var resolver = new LexicalLookupProviderResolver([new FakeProvider("Wiktionary", 1)]);
        Assert.IsNull(resolver.TryResolve("Wikipedia"));
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
        var resolver = new LexicalLookupProviderResolver([new FakeProvider("Wiktionary", 1)]);
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => resolver.Resolve("Wikipedia"));
        Assert.IsTrue(ex.Message.Contains("not registered"));
    }
}
