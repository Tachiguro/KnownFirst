using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

[TestClass]
public sealed class GermanCompoundDecomposerTests
{
    private readonly IGermanLexicon _lexicon = new FixtureGermanLexicon();

    [TestMethod]
    public void TryDecompose_Schreibmaschine_UniquelyDecomposesToSchreibenAndMaschine()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Schreibmaschine", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Schreib", decomposition.Components[0].ComponentForm);
        Assert.AreEqual("schreiben", decomposition.Components[0].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, decomposition.Components[0].Category);
        Assert.AreEqual("maschine", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Maschine", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
    }

    [TestMethod]
    public void TryDecompose_Waschmaschine_UniquelyDecomposesToWaschenAndMaschine()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Waschmaschine", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Wasch", decomposition.Components[0].ComponentForm);
        Assert.AreEqual("waschen", decomposition.Components[0].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, decomposition.Components[0].Category);
        Assert.AreEqual("maschine", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Maschine", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
    }

    [TestMethod]
    public void TryDecompose_Arbeitszimmer_DecomposesToArbeitAndZimmerViaLinkingS()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Arbeitszimmer", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Arbeit", decomposition.Components[0].ComponentForm);
        Assert.AreEqual("Arbeit", decomposition.Components[0].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[0].Category);
        Assert.AreEqual("zimmer", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Zimmer", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
    }

    [TestMethod]
    public void TryDecompose_Sicherheitsmanagement_DecomposesToSicherheitAndManagementViaLinkingS()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Sicherheitsmanagement", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Sicherheit", decomposition.Components[0].ComponentForm);
        Assert.AreEqual("Sicherheit", decomposition.Components[0].Lemma);
        Assert.AreEqual("management", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Management", decomposition.Components[1].Lemma);
    }

    [TestMethod]
    public void TryDecompose_Bundesland_DecomposesToBundAndLandViaLinkingEs()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Bundesland", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Bund", decomposition.Components[0].ComponentForm);
        Assert.AreEqual("Bund", decomposition.Components[0].Lemma);
        Assert.AreEqual("land", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Land", decomposition.Components[1].Lemma);
    }

    [TestMethod]
    public void TryDecompose_LiteralMatchWinsEvenWhenALinkingFallbackWouldAlsoResolve()
    {
        // "Testes" is itself a direct lexicon entry here, and stripping "s" ("Teste") or "es"
        // ("Test") would ALSO independently resolve if fallback were ever attempted. Literal
        // resolution must win outright and no fallback interpretation may even be considered.
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Testeszimmer", new LiteralPrecedenceFixtureLexicon(), out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Testes", decomposition.Components[0].ComponentForm);
        Assert.AreEqual("Testes", decomposition.Components[0].Lemma);
        Assert.AreEqual("zimmer", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Zimmer", decomposition.Components[1].Lemma);
    }

    [TestMethod]
    public void TryDecompose_UnsupportedLinkingElementN_FailsClosed()
    {
        // "n" is a candidate linking element from the plan but was not shipped in this package;
        // a compound that would only decompose via an "n" Fugen must fail closed.
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Sicherheitnzimmer", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_ThreeRealComponents_UniquelyDecomposesInOrder()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Schreibsicherheitzimmer", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(3, decomposition.Components);
        Assert.AreEqual("schreiben", decomposition.Components[0].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Verb, decomposition.Components[0].Category);
        Assert.AreEqual("Sicherheit", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
        Assert.AreEqual("Zimmer", decomposition.Components[2].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[2].Category);
    }

    [TestMethod]
    public void TryDecompose_FourRealComponents_UniquelyDecomposesInOrder()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Schreibwaschsicherheitzimmer", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(4, decomposition.Components);
        Assert.AreEqual("schreiben", decomposition.Components[0].Lemma);
        Assert.AreEqual("waschen", decomposition.Components[1].Lemma);
        Assert.AreEqual("Sicherheit", decomposition.Components[2].Lemma);
        Assert.AreEqual("Zimmer", decomposition.Components[3].Lemma);
    }

    [TestMethod]
    public void TryDecompose_MoreThanFourRequiredComponents_FailsClosed()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "AaBbCcDdEe", new FiveComponentFixtureLexicon(), out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_ComponentSpanShorterThanTwoCharacters_IsNeverAttempted()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "AZimmer", new SingleCharacterComponentFixtureLexicon(), out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_FinalComponentResolvingOnlyAsVerb_FailsClosedBecauseFinalMustBeNoun()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Maschineschreib", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_AmbiguousFallbackInterpretationForOneSpan_FailsClosed()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Testeszimmer", new OverlappingFallbackFixtureLexicon(), out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_AmbiguousCompletePartitionsAcrossComponentCounts_FailsClosed()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Abcdef", new CrossPartitionAmbiguousFixtureLexicon(), out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_Fenstergriffe_DeinflectsFinalPluralToGriffViaLexiconConfirmedE()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Fenstergriffe", _lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.HasCount(2, decomposition.Components);
        Assert.AreEqual("Fenster", decomposition.Components[0].Lemma);
        Assert.AreEqual("griff", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Griff", decomposition.Components[1].Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, decomposition.Components[1].Category);
    }

    [TestMethod]
    public void TryDecompose_Fenstergriffn_UnsupportedDeinflectionSuffix_FailsClosed()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Fenstergriffn", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_UnknownRightComponent_PreventsDecomposition()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Schreibpferd", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_UnknownLeftComponent_PreventsDecomposition()
    {
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Katzenmaschine", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_MultipleValidTwoComponentSplits_FailsClosed()
    {
        var lexicon = new AmbiguousFixtureLexicon();

        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Xyzabc", lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_RightComponentInitialUppercaseProbe_MatchesExactCaseAfterFirstLetterOnly()
    {
        var lexicon = new UppercaseProbeFixtureLexicon(includeProperlyCasedNoun: true);

        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Hausmaus", lexicon, out var decomposition);

        Assert.IsTrue(succeeded);
        Assert.IsNotNull(decomposition);
        Assert.AreEqual("Haus", decomposition.Components[0].ComponentForm);
        Assert.AreEqual("Haus", decomposition.Components[0].Lemma);
        Assert.AreEqual("maus", decomposition.Components[1].ComponentForm);
        Assert.AreEqual("Maus", decomposition.Components[1].Lemma);
    }

    [TestMethod]
    public void TryDecompose_RightComponentUppercaseProbe_DoesNotBroadenIntoFullCaseInsensitiveMatch()
    {
        var lexicon = new UppercaseProbeFixtureLexicon(includeProperlyCasedNoun: false);

        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Hausmaus", lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_WholeWordIsItselfASingleLexiconEntry_FailsClosedBecauseSplitIsRequired()
    {
        // "Zimmer" alone resolves directly as one lexicon noun; a decomposition requires
        // genuinely splitting the source compound into at least two components, so a bare
        // single-entry match must never be accepted as a trivial one-component "decomposition"
        // of itself.
        var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
            "Zimmer", _lexicon, out var decomposition);

        Assert.IsFalse(succeeded);
        Assert.IsNull(decomposition);
    }

    [TestMethod]
    public void TryDecompose_RepeatedCalls_AreDeterministic()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var succeeded = ConservativeGermanCompoundDecomposer.TryDecompose(
                "Schreibmaschine", _lexicon, out var decomposition);

            Assert.IsTrue(succeeded);
            Assert.IsNotNull(decomposition);
            Assert.AreEqual("schreiben", decomposition.Components[0].Lemma);
            Assert.AreEqual("Maschine", decomposition.Components[1].Lemma);
        }
    }

    /// <summary>
    /// Test-local fake lexicon that deliberately creates two distinct, equally valid two-component
    /// splits of the word "Xyzabc" ("Xyz"+"Abc" and "Xyza"+"Bc") so the decomposer's fail-closed
    /// ambiguity rule can be exercised without broadening the production <see cref="FixtureGermanLexicon"/>.
    /// </summary>
    private sealed class AmbiguousFixtureLexicon : IGermanLexicon
    {
        private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
            new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
            {
                ["Xyz"] = new("Xyz", GermanLexemeCategory.Noun),
                ["Xyza"] = new("Xyza", GermanLexemeCategory.Noun),
                ["Abc"] = new("Abc", GermanLexemeCategory.Noun),
                ["Bc"] = new("Bc", GermanLexemeCategory.Noun)
            };

        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            Lexemes.TryGetValue(form, out entry);

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Test-local fake lexicon isolating the right-hand initial-uppercase lookup probe: with
    /// <paramref name="includeProperlyCasedNoun"/> true it exposes exactly the properly-cased noun
    /// "Maus" the probe is expected to find; with it false it exposes only an all-uppercase "MAUS"
    /// key that a correct minimal (first-letter-only) probe must never match.
    /// </summary>
    private sealed class UppercaseProbeFixtureLexicon(bool includeProperlyCasedNoun) : IGermanLexicon
    {
        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry)
        {
            if (string.Equals(form, "Haus", StringComparison.Ordinal))
            {
                entry = new GermanLexemeEntry("Haus", GermanLexemeCategory.Noun);
                return true;
            }

            if (includeProperlyCasedNoun && string.Equals(form, "Maus", StringComparison.Ordinal))
            {
                entry = new GermanLexemeEntry("Maus", GermanLexemeCategory.Noun);
                return true;
            }

            if (!includeProperlyCasedNoun && string.Equals(form, "MAUS", StringComparison.Ordinal))
            {
                entry = new GermanLexemeEntry("MAUS", GermanLexemeCategory.Noun);
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Test-local fake lexicon exposing exactly five short, mutually non-combinable components
    /// ("Aa".."Ee"), none of which can be merged into a shorter valid entry, so the only way to
    /// fully cover a five-piece word is a five-component partition. Used to prove that
    /// <see cref="ConservativeGermanCompoundDecomposer.MaxComponents"/> (4) fails the search
    /// closed rather than accepting a fifth component.
    /// </summary>
    private sealed class FiveComponentFixtureLexicon : IGermanLexicon
    {
        private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
            new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
            {
                ["Aa"] = new("Aa", GermanLexemeCategory.Noun),
                ["Bb"] = new("Bb", GermanLexemeCategory.Noun),
                ["Cc"] = new("Cc", GermanLexemeCategory.Noun),
                ["Dd"] = new("Dd", GermanLexemeCategory.Noun),
                ["Ee"] = new("Ee", GermanLexemeCategory.Noun)
            };

        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            Lexemes.TryGetValue(form, out entry);

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Test-local fake lexicon exposing a single one-character entry "A" plus a normal noun
    /// "Zimmer", so that "AZimmer" would decompose to A+Zimmer only if one-character component
    /// spans were permitted. Used to prove the minimum literal component span length (2) prunes
    /// such spans before any lexicon lookup is even attempted.
    /// </summary>
    private sealed class SingleCharacterComponentFixtureLexicon : IGermanLexicon
    {
        private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
            new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
            {
                ["A"] = new("A", GermanLexemeCategory.Noun),
                ["Zimmer"] = new("Zimmer", GermanLexemeCategory.Noun)
            };

        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            Lexemes.TryGetValue(form, out entry);

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Test-local fake lexicon in which "Testes" is itself a direct entry, while stripping "s"
    /// ("Teste") or "es" ("Test") would also independently resolve if fallback were attempted.
    /// Used to prove that literal resolution wins outright and fallback is never even considered
    /// once literal resolution succeeds.
    /// </summary>
    private sealed class LiteralPrecedenceFixtureLexicon : IGermanLexicon
    {
        private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
            new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
            {
                ["Testes"] = new("Testes", GermanLexemeCategory.Noun),
                ["Teste"] = new("Teste", GermanLexemeCategory.Noun),
                ["Test"] = new("Test", GermanLexemeCategory.Noun),
                ["Zimmer"] = new("Zimmer", GermanLexemeCategory.Noun)
            };

        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            Lexemes.TryGetValue(form, out entry);

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Test-local fake lexicon in which the span "Testes" admits two distinct, independently
    /// lexicon-confirmed fallback interpretations: stripping "s" yields "Teste", and stripping
    /// "es" yields "Test" — both real entries here. Used to prove that more than one valid
    /// fallback interpretation for the same component span fails closed rather than guessing
    /// between them.
    /// </summary>
    private sealed class OverlappingFallbackFixtureLexicon : IGermanLexicon
    {
        private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
            new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
            {
                ["Teste"] = new("Teste", GermanLexemeCategory.Noun),
                ["Test"] = new("Test", GermanLexemeCategory.Noun),
                ["Zimmer"] = new("Zimmer", GermanLexemeCategory.Noun)
            };

        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            Lexemes.TryGetValue(form, out entry);

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Test-local fake lexicon in which "Abcdef" admits two structurally different complete
    /// partitions: a two-component reading ("Abc"+"def") and a three-component reading
    /// ("Ab"+"cd"+"ef"). Used to prove that competing full partitions of different component
    /// counts fail closed rather than preferring either one.
    /// </summary>
    private sealed class CrossPartitionAmbiguousFixtureLexicon : IGermanLexicon
    {
        private static readonly IReadOnlyDictionary<string, GermanLexemeEntry> Lexemes =
            new Dictionary<string, GermanLexemeEntry>(StringComparer.Ordinal)
            {
                ["Abc"] = new("Abc", GermanLexemeCategory.Noun),
                ["Def"] = new("Def", GermanLexemeCategory.Noun),
                ["Ab"] = new("Ab", GermanLexemeCategory.Noun),
                ["Cd"] = new("Cd", GermanLexemeCategory.Noun),
                ["Ef"] = new("Ef", GermanLexemeCategory.Noun)
            };

        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            Lexemes.TryGetValue(form, out entry);

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
        {
            entry = null;
            return false;
        }
    }
}
