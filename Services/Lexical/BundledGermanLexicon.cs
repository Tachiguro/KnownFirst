using KnownFirst.Core.Text;
using KnownFirst.Core.Text.German;

namespace KnownFirst.Services.Lexical;

/// <summary>
/// Production, offline <see cref="IGermanLexicon"/> implementation backed by the single German
/// morphology lexicon bundle (<c>german-lexicon.v2.kfgl</c>) embedded into this assembly's
/// manifest resources at build time under <see cref="ResourceName"/> — one explicit, stable
/// resource identity, never assembly/resource scanning.
///
/// Loading and parsing the ~11 MB bundle is deferred until the first actual lookup: enhanced
/// German term recognition being disabled, or analyzing non-German text, never touches the
/// embedded resource. Initialization is thread-safe and happens at most once; every lookup after
/// the first reuses the already-loaded <see cref="GeneratedGermanLexicon"/>.
/// </summary>
public sealed class BundledGermanLexicon : IGermanLexicon
{
    public const string ResourceName = "KnownFirst.Resources.German.german-lexicon.v2.kfgl";

    private readonly Lazy<GeneratedGermanLexicon> _lexicon;

    public BundledGermanLexicon()
        : this(OpenEmbeddedResourceStream)
    {
    }

    /// <summary>Testable seam: supply an alternate resource-stream opener to exercise lazy-loading and failure behavior without a second copy of the production bundle.</summary>
    internal BundledGermanLexicon(Func<Stream> openResourceStream)
    {
        ArgumentNullException.ThrowIfNull(openResourceStream);
        _lexicon = new Lazy<GeneratedGermanLexicon>(() =>
        {
            using var stream = openResourceStream();
            return GeneratedGermanLexicon.Load(stream);
        });
    }

    public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
        _lexicon.Value.TryLookupLemma(form, out entry);

    public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry) =>
        _lexicon.Value.TryLookupStem(componentForm, out entry);

    private static Stream OpenEmbeddedResourceStream() =>
        typeof(BundledGermanLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded German lexicon resource '{ResourceName}' was not found in this " +
                "assembly. Enhanced German term recognition cannot proceed without the shipped " +
                "lexicon bundle.");
}
