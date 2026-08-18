using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

[TestClass]
public sealed class VocabularyCandidateProvenanceTests
{
    private const string Content = "Die Schreibmaschine steht hier. Der Text bleibt gleich.";
    private const string SourceSurfaceForm = "Schreibmaschine";
    private const string ComponentForm = "Schreib";
    private const string DerivedIdentity = "W:schreiben";
    private const string DerivedCanonicalTerm = "schreiben";

    private readonly TextAnalyzer _analyzer = new();

    [TestMethod]
    public void DirectCandidate_RetainsLiteralOccurrencesAndDirectProvenance()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var candidate = FindSourceCandidate(analysis);

        Assert.AreEqual(CandidateProvenanceKind.Direct, candidate.Provenance);
        Assert.IsFalse(candidate.IsDerived);
        Assert.IsEmpty(candidate.DerivedEvidence);
        Assert.IsNotEmpty(candidate.Occurrences);
        Assert.AreEqual(
            candidate.Occurrences[0].SurfaceForm,
            Content.Substring(candidate.Occurrences[0].StartPosition, candidate.Occurrences[0].Length));
    }

    [TestMethod]
    public void DerivedCandidate_HasNoLiteralOccurrencesAndRetainsSourceEvidence()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var sourceOccurrence = FindSourceOccurrence(analysis);
        var derived = CreateDerivedCandidate(CreateEvidence(sourceOccurrence));

        Assert.AreEqual(CandidateProvenanceKind.DerivedFromCompound, derived.Provenance);
        Assert.IsTrue(derived.IsDerived);
        Assert.IsEmpty(derived.Occurrences);
        Assert.HasCount(1, derived.DerivedEvidence);

        var evidence = derived.DerivedEvidence[0];
        Assert.AreEqual(sourceOccurrence.Identity, evidence.SourceIdentity);
        Assert.AreEqual(SourceSurfaceForm, evidence.SourceSurfaceForm);
        Assert.AreEqual(ComponentForm, evidence.ComponentForm);
        Assert.AreEqual(sourceOccurrence.SentenceOrder, evidence.SourceSentenceOrder);
        Assert.AreEqual(
            SourceSurfaceForm,
            Content.Substring(evidence.SourceStartPosition, evidence.SourceLength));
    }

    [TestMethod]
    public void DerivedCandidate_HasNoEncounteredFormsWithoutLiteralOccurrences()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var derived = CreateDerivedCandidate(CreateEvidence(FindSourceOccurrence(analysis)));

        Assert.IsEmpty(derived.EncounteredForms);
    }

    [TestMethod]
    public void AnalysisInvariantValidator_AcceptsDerivedCandidateWithValidSourceEvidence()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var derived = CreateDerivedCandidate(CreateEvidence(FindSourceOccurrence(analysis)));

        var failures = AnalysisInvariantValidator.Validate(Content, WithDerivedCandidate(analysis, derived));

        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateWithOutOfRangeSourceEvidence()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var evidence = CreateEvidence(FindSourceOccurrence(analysis)) with
        {
            SourceStartPosition = Content.Length + 5
        };

        var failures = AnalysisInvariantValidator.Validate(
            Content,
            WithDerivedCandidate(analysis, CreateDerivedCandidate(evidence)));

        AssertContainsCode(failures, "DerivedEvidenceRangeOutsideDocument");
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateWithSourceSubstringMismatch()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var evidence = CreateEvidence(FindSourceOccurrence(analysis)) with
        {
            SourceSurfaceForm = ComponentForm
        };

        var failures = AnalysisInvariantValidator.Validate(
            Content,
            WithDerivedCandidate(analysis, CreateDerivedCandidate(evidence)));

        AssertContainsCode(failures, "DerivedEvidenceSubstringMismatch");
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateWithSentenceOrderMismatch()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var sourceOccurrence = FindSourceOccurrence(analysis);
        var evidence = CreateEvidence(sourceOccurrence) with
        {
            SourceSentenceOrder = sourceOccurrence.SentenceOrder + 1
        };

        var failures = AnalysisInvariantValidator.Validate(
            Content,
            WithDerivedCandidate(analysis, CreateDerivedCandidate(evidence)));

        AssertContainsCode(failures, "DerivedEvidenceSentenceOrderMismatch");
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateWithZeroLengthSourceEvidence()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var sourceOccurrence = FindSourceOccurrence(analysis);
        var evidence = CreateEvidence(sourceOccurrence) with
        {
            SourceLength = 0,
            SourceSurfaceForm = string.Empty
        };

        var failures = AnalysisInvariantValidator.Validate(
            Content,
            WithDerivedCandidate(analysis, CreateDerivedCandidate(evidence)));

        AssertContainsCode(failures, "DerivedEvidenceSourceLengthInvalid");
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateWithMissingSourceSurfaceForm()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var sourceOccurrence = FindSourceOccurrence(analysis);
        var evidence = CreateEvidence(sourceOccurrence) with
        {
            SourceSurfaceForm = "   "
        };

        var failures = AnalysisInvariantValidator.Validate(
            Content,
            WithDerivedCandidate(analysis, CreateDerivedCandidate(evidence)));

        AssertContainsCode(failures, "DerivedEvidenceSourceSurfaceMissing");
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateWithMissingSourceIdentity()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var sourceOccurrence = FindSourceOccurrence(analysis);
        var evidence = CreateEvidence(sourceOccurrence) with
        {
            SourceIdentity = "   "
        };

        var failures = AnalysisInvariantValidator.Validate(
            Content,
            WithDerivedCandidate(analysis, CreateDerivedCandidate(evidence)));

        AssertContainsCode(failures, "DerivedEvidenceSourceIdentityMissing");
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateWithMissingComponentForm()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var sourceOccurrence = FindSourceOccurrence(analysis);
        var evidence = CreateEvidence(sourceOccurrence) with
        {
            ComponentForm = "   "
        };

        var failures = AnalysisInvariantValidator.Validate(
            Content,
            WithDerivedCandidate(analysis, CreateDerivedCandidate(evidence)));

        AssertContainsCode(failures, "DerivedEvidenceComponentFormMissing");
    }

    [TestMethod]
    public void AnalysisInvariantValidator_RejectsDerivedCandidateThatCarriesLiteralOccurrences()
    {
        var analysis = _analyzer.Analyze(Content, "de");
        var sourceOccurrence = FindSourceOccurrence(analysis);
        var derived = CreateDerivedCandidate(CreateEvidence(sourceOccurrence)) with
        {
            Occurrences = new[]
            {
                sourceOccurrence with
                {
                    Identity = DerivedIdentity,
                    CanonicalTerm = DerivedCanonicalTerm
                }
            }
        };

        var failures = AnalysisInvariantValidator.Validate(Content, WithDerivedCandidate(analysis, derived));

        AssertContainsCode(failures, "DerivedCandidateCarriesLiteralOccurrences");
    }

    private static void AssertContainsCode(IReadOnlyList<AnalysisInvariantFailure> failures, string code) =>
        Assert.IsTrue(
            failures.Any(failure => string.Equals(failure.Code, code, StringComparison.Ordinal)),
            $"Expected invariant failure '{code}' but received: {string.Join(", ", failures.Select(failure => failure.Code))}.");

    private static VocabularyCandidate FindSourceCandidate(TextAnalysisResult analysis) =>
        analysis.Candidates.Single(candidate =>
            candidate.Occurrences.Any(occurrence =>
                string.Equals(occurrence.SurfaceForm, SourceSurfaceForm, StringComparison.Ordinal)));

    private static TokenOccurrence FindSourceOccurrence(TextAnalysisResult analysis) =>
        FindSourceCandidate(analysis).Occurrences.Single(occurrence =>
            string.Equals(occurrence.SurfaceForm, SourceSurfaceForm, StringComparison.Ordinal));

    private static DerivedTermEvidence CreateEvidence(TokenOccurrence sourceOccurrence) =>
        new(
            sourceOccurrence.Identity,
            sourceOccurrence.SurfaceForm,
            sourceOccurrence.StartPosition,
            sourceOccurrence.Length,
            sourceOccurrence.SentenceOrder,
            ComponentForm);

    private static VocabularyCandidate CreateDerivedCandidate(DerivedTermEvidence evidence) =>
        new(
            DerivedIdentity,
            DerivedCanonicalTerm,
            TokenKind.Word,
            new Dictionary<string, int>(StringComparer.Ordinal),
            Array.Empty<TokenOccurrence>())
        {
            Provenance = CandidateProvenanceKind.DerivedFromCompound,
            DerivedEvidence = new[] { evidence }
        };

    private static TextAnalysisResult WithDerivedCandidate(
        TextAnalysisResult analysis,
        VocabularyCandidate derived) =>
        analysis with
        {
            Candidates = analysis.Candidates.Append(derived).ToArray()
        };
}
