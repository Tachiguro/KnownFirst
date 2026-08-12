using KnownFirst.Core.Preparation;
using KnownFirst.Services.Lexical;

namespace KnownFirst.Services.Isolation;

/// <summary>
/// Debug/GUI-test-only deterministic dictionary provider (KF-MEANING-002 GUI hardening, Part 3). Returns
/// fixed, in-process canned meanings instantly — never a network call — so GUI-test seeding can populate a
/// preparation candidate with multiple provider meanings deterministically. Registered under the exact
/// same <see cref="WiktionaryLookupProvider.Name"/> so the app's normal lookup request construction
/// (which always names that provider) resolves to this substitute instead, only when
/// <see cref="GuiTestScenarioSeed.IsActive"/> is true.
/// </summary>
public sealed class GuiTestSeedLookupProvider : IDictionaryLookupProvider
{
    public const string SeedTerm = "bank";
    private static int _invocationCount;

    public static int InvocationCount => Volatile.Read(ref _invocationCount);

    public string ProviderName => WiktionaryLookupProvider.Name;

    public int ProviderSchemaVersion => 1;

    public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invocationCount);
        var meanings = new List<LexicalMeaning>
        {
            new("gui-test-financial-institution", "noun",
                "An establishment that holds money for customers and lends money to borrowers.",
                "Bank (financial institution)", null, []),
            new("gui-test-river-edge", "noun",
                "The land alongside a river or lake.", "Ufer", null, []),
            new("gui-test-data-repository", "noun",
                "A large stored collection of data or information.", "Datenbank", null, []),
        };

        var result = new LexicalResult(
            LexicalLookupStatus.Success,
            request.NormalizedLemma,
            request.Term,
            request.TokenKind,
            request.SourceLanguage,
            request.ExplanationLanguage,
            AcronymExpansion: null,
            meanings,
            ProviderName,
            SourceProject: "gui-test.local",
            PageTitle: request.Term,
            RevisionId: 1,
            Attribution: "GUI test fixture data",
            LookupAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LookupMode: request.LookupMode,
            TargetLanguage: request.TargetLanguage);

        return Task.FromResult(result);
    }
}
