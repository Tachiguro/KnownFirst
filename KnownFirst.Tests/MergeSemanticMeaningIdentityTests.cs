using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;
using static KnownFirst.Tests.MergePreflightFixtures;

namespace KnownFirst.Tests;

/// <summary>
/// Focused, planner-independent unit tests for the new Slice 3 identity policies
/// (<see cref="SemanticMeaningIdentityPolicy"/>, <see cref="ExactMeaningVariantIdentityPolicy"/>,
/// <see cref="AnswerVariantIdentityPolicy"/>, <see cref="FutureCardIdentityPolicy"/>) introduced for the
/// approved Word -&gt; SemanticMeaning -&gt; AnswerVariant model (KF-MEANING-001). Expected outcomes are
/// reasoned about directly from the input fields, never derived by calling the planner.
/// </summary>
[TestClass]
public sealed class MergeSemanticMeaningIdentityTests
{
    private static readonly VocabularyIdentity Word = new("WORD-HASH");

    // ---- FutureCardIdentity ----

    [TestMethod]
    public void FutureCardIdentity_DistinctSemanticMeanings_ProduceSeparateIdentities()
    {
        var meaningA = new SemanticMeaningIdentity("MEANING-A");
        var meaningB = new SemanticMeaningIdentity("MEANING-B");

        var cardA = FutureCardIdentityPolicy.Compute(meaningA, BackupCardDirection.TermToMeaning);
        var cardB = FutureCardIdentityPolicy.Compute(meaningB, BackupCardDirection.TermToMeaning);

        Assert.AreNotEqual(cardA, cardB, "Distinct SemanticMeanings of one word must produce separate FutureCardIdentity values.");
    }

    [TestMethod]
    public void FutureCardIdentity_SynonymsOfOneMeaning_ShareOneIdentity()
    {
        var meaningIdentity = SemanticMeaningIdentityPolicy.Compute(
            PreparedItem("p-1", "v-1", definition: "financial institution"), Word);

        // Two rows that are mere synonym/answer-variant variations of the *same* meaning (different
        // DisplayTerm/aliases only) compute the same SemanticMeaningIdentity, because DisplayTerm/aliases
        // are not part of that identity's field set.
        var rowWithDisplayTermBank = PreparedItem("p-1", "v-1", definition: "financial institution");
        var rowWithDisplayTermSavings = rowWithDisplayTermBank with { DisplayTerm = "savings bank" };

        var identityForBank = SemanticMeaningIdentityPolicy.Compute(rowWithDisplayTermBank, Word);
        var identityForSavings = SemanticMeaningIdentityPolicy.Compute(rowWithDisplayTermSavings, Word);

        Assert.AreEqual(identityForBank, identityForSavings);

        var futureCardForBank = FutureCardIdentityPolicy.Compute(identityForBank, BackupCardDirection.TermToMeaning);
        var futureCardForSavings = FutureCardIdentityPolicy.Compute(identityForSavings, BackupCardDirection.TermToMeaning);

        Assert.AreEqual(futureCardForBank, futureCardForSavings, "Synonyms of one meaning must share one FutureCardIdentity.");
        Assert.AreEqual(meaningIdentity, identityForBank);
    }

    [TestMethod]
    public void FutureCardIdentity_DifferentDirection_IsDistinct()
    {
        var meaning = new SemanticMeaningIdentity("MEANING-A");

        var termToMeaning = FutureCardIdentityPolicy.Compute(meaning, BackupCardDirection.TermToMeaning);
        var meaningToTerm = FutureCardIdentityPolicy.Compute(meaning, BackupCardDirection.MeaningToTerm);

        Assert.AreNotEqual(termToMeaning, meaningToTerm);
    }

    // ---- SemanticMeaningIdentity ----

    [TestMethod]
    public void SemanticMeaningIdentity_ExcludesNotesAliasesAttributionConfirmation()
    {
        var baseline = PreparedItem("p-1", "v-1", definition: "financial institution");
        var withNoteAndAliases = baseline with
        {
            AdditionalNote = "informal usage",
            AcceptedAliases = ["financial house", "lender"],
            DictionaryExample = "I deposited money at the bank.",
            ConfirmedByUser = false,
            Source = new BackupSourceReference("Manual", "", "", null, "user-entered")
        };

        Assert.AreEqual(
            SemanticMeaningIdentityPolicy.Compute(baseline, Word),
            SemanticMeaningIdentityPolicy.Compute(withNoteAndAliases, Word),
            "Notes, aliases, examples, confirmation, and attribution/provider metadata must never split a semantic meaning.");
    }

    [TestMethod]
    public void SemanticMeaningIdentity_TopicOrDomainParameter_DistinguishesMeanings_WhenSupplied()
    {
        // Forward-compatible: the current schema never persists topic/domain (every planner call site
        // passes the default ""), but the policy itself must already distinguish meanings correctly once
        // a future schema change supplies real topic data.
        var item = PreparedItem("p-1", "v-1", definition: "the edge of a river");

        var financeTopic = SemanticMeaningIdentityPolicy.Compute(item, Word, canonicalTopicOrDomain: "finance");
        var geographyTopic = SemanticMeaningIdentityPolicy.Compute(item, Word, canonicalTopicOrDomain: "geography");
        var noTopic = SemanticMeaningIdentityPolicy.Compute(item, Word);

        Assert.AreNotEqual(financeTopic, geographyTopic);
        Assert.AreNotEqual(financeTopic, noTopic);
        Assert.AreNotEqual(geographyTopic, noTopic);
    }

    [TestMethod]
    public void SemanticMeaningIdentity_NeverInfersTopicFromDefinitionText()
    {
        // Two definitions that a human reader would recognize as "finance" vs "geography" topics, with
        // no topic field supplied, must still be compared purely on their own Definition/Translation
        // text — if they happened to be textually identical they would collapse, and if distinct they
        // remain distinct purely because the text differs, never because of an inferred topic.
        var financeSense = PreparedItem("p-1", "v-1", definition: "a financial institution that holds money");
        var geographySense = PreparedItem("p-2", "v-1", definition: "a financial institution that holds money");

        Assert.AreEqual(
            SemanticMeaningIdentityPolicy.Compute(financeSense, Word),
            SemanticMeaningIdentityPolicy.Compute(geographySense, Word),
            "Identical Definition text must dedupe regardless of any topic a reader might infer from it.");
    }

    [TestMethod]
    public void SemanticMeaningIdentity_TranslationTextAlone_DoesNotSplitIdentity()
    {
        // The core defect-1 correction: Translation must never, by itself, distinguish semantic meanings.
        var bankRiver = PreparedItem("p-1", "v-1", definition: "financial institution", translation: "Bank (Fluss)");
        var bankFinancial = PreparedItem("p-2", "v-1", definition: "financial institution", translation: "Bank (Geld)");

        Assert.AreEqual(
            SemanticMeaningIdentityPolicy.Compute(bankRiver, Word),
            SemanticMeaningIdentityPolicy.Compute(bankFinancial, Word),
            "Translation/answer text alone must not split a SemanticMeaning.");
    }

    [TestMethod]
    public void SemanticMeaningIdentity_DefinitionTextAlone_DoesNotSplitIdentity_WhenSameProviderSenseId()
    {
        // Checklist item 1: same stable provider sense id, differently-worded Definition text — must still
        // be one SemanticMeaning. Final focused review correction: Definition must never, by itself,
        // distinguish semantic meanings, exactly like Translation (§ above).
        var bankFinancialWordedOne = PreparedItem("p-1", "v-1", definition: "a place that holds money") with { ProviderMeaningId = "sense-42" };
        var bankFinancialWordedTwo = PreparedItem("p-2", "v-1", definition: "a financial institution") with { ProviderMeaningId = "sense-42" };

        Assert.AreEqual(
            SemanticMeaningIdentityPolicy.Compute(bankFinancialWordedOne, Word),
            SemanticMeaningIdentityPolicy.Compute(bankFinancialWordedTwo, Word),
            "Definition wording alone must not split a SemanticMeaning when a stable provider sense id already confirms the same sense.");
    }

    [TestMethod]
    public void SemanticMeaningIdentity_DifferentProviderSenseIds_ProduceDistinctIdentities()
    {
        // Checklist item 5: different stable provider sense ids are strong distinct-sense evidence.
        var senseOne = PreparedItem("p-1", "v-1", definition: "a definition") with { ProviderMeaningId = "sense-1" };
        var senseTwo = PreparedItem("p-2", "v-1", definition: "a definition") with { ProviderMeaningId = "sense-2" };

        Assert.AreNotEqual(
            SemanticMeaningIdentityPolicy.Compute(senseOne, Word),
            SemanticMeaningIdentityPolicy.Compute(senseTwo, Word),
            "Distinct stable provider sense ids must produce distinct SemanticMeaning identities.");
    }

    [TestMethod]
    public void HasReliableSenseDiscriminator_TrueWhenAnyStrongFieldPresent()
    {
        var withProviderSense = PreparedItem("p-1", "v-1", definition: null) with { ProviderMeaningId = "sense-1" };
        var withGrammar = PreparedItem("p-1", "v-1", definition: null) with { GrammaticalRelationship = "plural" };
        var withAcronym = PreparedItem("p-1", "v-1", definition: null) with { AcronymExpansion = "Central Intelligence Agency" };
        var withNothing = PreparedItem("p-1", "v-1", definition: null);

        Assert.IsTrue(SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(withProviderSense));
        Assert.IsTrue(SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(withGrammar));
        Assert.IsTrue(SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(withAcronym));
        Assert.IsTrue(SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(withNothing, canonicalTopicOrDomain: "finance"));
        Assert.IsFalse(SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(withNothing));
    }

    [TestMethod]
    public void HasReliableSenseDiscriminator_DefinitionAloneIsNotReliable()
    {
        // Final focused review correction: free-text Definition wording — present, absent, matching, or
        // differing — must never by itself count as a reliable sense discriminator. Only a stable provider
        // sense id, a persisted topic/domain, a grammatical/part-of-speech discriminator, or an acronym
        // expansion qualifies.
        var withDefinitionOnly = PreparedItem("p-1", "v-1", definition: "a definition");

        Assert.IsFalse(SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(withDefinitionOnly));
    }

    // ---- ExactMeaningVariantIdentity ----

    [TestMethod]
    public void ExactMeaningVariantIdentity_ProtectsTranslationText()
    {
        var semanticIdentity = new SemanticMeaningIdentity("SHARED-MEANING");
        var row1 = PreparedItem("p-1", "v-1", translation: "Bank (Fluss)");
        var row2 = PreparedItem("p-1", "v-1", translation: "Bank (Geld)");

        Assert.AreNotEqual(
            ExactMeaningVariantIdentityPolicy.Compute(row1, semanticIdentity),
            ExactMeaningVariantIdentityPolicy.Compute(row2, semanticIdentity),
            "ExactMeaningVariantIdentity must protect all persisted content, including Translation.");
    }

    [TestMethod]
    public void ExactMeaningVariantIdentity_ProtectsDefinitionText()
    {
        // Final focused review correction: once Definition was removed from SemanticMeaningIdentity, it
        // must be protected directly here instead — otherwise two rows differing only in Definition would
        // wrongly collapse to one exact duplicate/variant.
        var semanticIdentity = new SemanticMeaningIdentity("SHARED-MEANING");
        var row1 = PreparedItem("p-1", "v-1", definition: "a place that holds money");
        var row2 = PreparedItem("p-1", "v-1", definition: "a financial institution");

        Assert.AreNotEqual(
            ExactMeaningVariantIdentityPolicy.Compute(row1, semanticIdentity),
            ExactMeaningVariantIdentityPolicy.Compute(row2, semanticIdentity),
            "ExactMeaningVariantIdentity must protect all persisted content, including Definition.");
    }

    [TestMethod]
    public void ExactMeaningVariantIdentity_DiffersOnNoteEvenWhenSemanticMeaningIsShared()
    {
        var semanticIdentity = new SemanticMeaningIdentity("SHARED-MEANING");
        var row1 = PreparedItem("p-1", "v-1", definition: "financial institution");
        var row2 = row1 with { AdditionalNote = "colloquial usage" };

        var exact1 = ExactMeaningVariantIdentityPolicy.Compute(row1, semanticIdentity);
        var exact2 = ExactMeaningVariantIdentityPolicy.Compute(row2, semanticIdentity);

        Assert.AreNotEqual(exact1, exact2, "A different note must still distinguish the exact persisted-content identity.");
    }

    [TestMethod]
    public void ExactMeaningVariantIdentity_AliasOrderDoesNotAffectIdentity()
    {
        var semanticIdentity = new SemanticMeaningIdentity("SHARED-MEANING");
        var row1 = PreparedItem("p-1", "v-1") with { AcceptedAliases = ["alpha", "beta"] };
        var row2 = PreparedItem("p-1", "v-1") with { AcceptedAliases = ["beta", "alpha"] };

        Assert.AreEqual(
            ExactMeaningVariantIdentityPolicy.Compute(row1, semanticIdentity),
            ExactMeaningVariantIdentityPolicy.Compute(row2, semanticIdentity));
    }

    // ---- AnswerVariantIdentity ----

    [TestMethod]
    public void AnswerVariantIdentity_EquivalentNormalizedText_Deduplicates()
    {
        var semanticIdentity = new SemanticMeaningIdentity("MEANING");

        var identity1 = AnswerVariantIdentityPolicy.Compute(semanticIdentity, "  bank  ", "en");
        var identity2 = AnswerVariantIdentityPolicy.Compute(semanticIdentity, "bank", "en");

        Assert.AreEqual(identity1, identity2, "Equivalent normalized answer text must deduplicate.");
    }

    [TestMethod]
    public void AnswerVariantIdentity_DistinctText_IsPreserved()
    {
        var semanticIdentity = new SemanticMeaningIdentity("MEANING");

        var bank = AnswerVariantIdentityPolicy.Compute(semanticIdentity, "bank", "en");
        var financialInstitution = AnswerVariantIdentityPolicy.Compute(semanticIdentity, "financial institution", "en");

        Assert.AreNotEqual(bank, financialInstitution, "Distinct answer text must remain preserved (not collapsed).");
    }

    [TestMethod]
    public void AnswerVariantIdentity_DifferentSemanticMeaning_IsDistinctEvenForSameText()
    {
        var meaningA = new SemanticMeaningIdentity("MEANING-A");
        var meaningB = new SemanticMeaningIdentity("MEANING-B");

        var variantA = AnswerVariantIdentityPolicy.Compute(meaningA, "bank", "en");
        var variantB = AnswerVariantIdentityPolicy.Compute(meaningB, "bank", "en");

        Assert.AreNotEqual(variantA, variantB, "The same text for two different senses must not collapse into one AnswerVariant.");
    }

    // ---- Answer-role reconciliation (§6) ----

    [TestMethod]
    public void AnswerVariantRolePrecedence_RequiredOutranksAcceptedAlias()
    {
        Assert.AreEqual(AnswerVariantRole.RequiredAnswerVariant, AnswerVariantRolePrecedencePolicy.Reconcile(AnswerVariantRole.AcceptedAlias, AnswerVariantRole.RequiredAnswerVariant));
        Assert.AreEqual(AnswerVariantRole.RequiredAnswerVariant, AnswerVariantRolePrecedencePolicy.Reconcile(AnswerVariantRole.RequiredAnswerVariant, AnswerVariantRole.AcceptedAlias));
    }

    [TestMethod]
    public void AnswerVariantRolePrecedence_TwoAcceptedAliases_StayAlias()
    {
        Assert.AreEqual(AnswerVariantRole.AcceptedAlias, AnswerVariantRolePrecedencePolicy.Reconcile(AnswerVariantRole.AcceptedAlias, AnswerVariantRole.AcceptedAlias));
    }

    [TestMethod]
    public void AnswerVariantRolePrecedence_PrimaryAnswerIsNotPartOfTheRanking()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AnswerVariantRolePrecedencePolicy.Reconcile(AnswerVariantRole.PrimaryAnswer, AnswerVariantRole.AcceptedAlias));
    }
}
