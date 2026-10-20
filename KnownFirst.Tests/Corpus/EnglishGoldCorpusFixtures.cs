using KnownFirst.Core.Text;
using KnownFirst.Tests.AnalysisEvidence;

namespace KnownFirst.Tests.Corpus;

public sealed record EnglishGoldCorpusCase(
    string Id,
    string Description,
    string Input,
    CorpusExpectationAuthority Authority,
    string SourceLanguage = "en");

public sealed record EnglishGoldCorpusCaseResult(
    string CaseId,
    string Description,
    CorpusExpectationAuthority Authority,
    AnalysisEvidenceDocument AnalyzerEvidence);

public static class EnglishGoldCorpusFixtures
{
    public static readonly EnglishGoldCorpusCase EN001_RepeatedWordsAndContexts = new(
        Id: "EN-001-repeated-words-and-contexts",
        Description: "Repeated English vocabulary across case variants, frequency-1 retention, and distinct single-sentence contexts with attached citations.",
        Input: "Information protects systems.[1] Protected information helps users.[2] INFORMATION remains available.",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly EnglishGoldCorpusCase EN002_UnicodeOffsetsApostrophesPunctuation = new(
        Id: "EN-002-unicode-offsets-apostrophes-punctuation",
        Description: "Exact UTF-16 offsets, composed and decomposed Unicode normalization to Form C, straight and typographic apostrophes, punctuation exclusion, and final sentence remainder.",
        Input: "She said, \"We don't copy a user\u2019s caf\u00e9 or cafe\u0301 notes\"",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior);

    public static readonly EnglishGoldCorpusCase EN003_BoundariesLineEndingsAbbreviationsDecimals = new(
        Id: "EN-003-boundaries-line-endings-abbreviations-decimals",
        Description: "Sentence boundaries across CRLF, LF, CR, and final remainder, supported English abbreviation and decimal period suppression, and standalone number exclusion.",
        Input: "Overview\r\nDr. Smith used e.g. version 3.14, i.e. option 7, in the U.S. lab.\nSecond line\rFinal note",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly EnglishGoldCorpusCase EN004_TechnicalTokensAndExclusions = new(
        Id: "EN-004-technical-tokens-and-exclusions",
        Description: "Technical tokens (OAuth2, IPv6, end-to-end, CVE family, SHA family) and URL, email address, and standalone number exclusions.",
        Input: "Verify OAuth2 and IPv6 with CVE-2026-12345, CVE, SHA-256, and SHA-512 at https://example.com/docs or security@example.org for 42 end-to-end checks.",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly EnglishGoldCorpusCase EN005_DuplicateContextsAndEnglishIdentityRules = new(
        Id: "EN-005-duplicate-contexts-and-english-identity-rules",
        Description: "Duplicate-context suppression, case-sensitive acronym versus ordinary word separation (IT/it, US/us), explicit English pronoun grouping (I/me/my), and non-stemming (network/networking).",
        Input: "IT helps us test network security. IT helps us test network security. In the US, it helps me and my networking team, so I learn.",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior);

    public static readonly IReadOnlyList<EnglishGoldCorpusCase> AllCases = new[]
    {
        EN001_RepeatedWordsAndContexts,
        EN002_UnicodeOffsetsApostrophesPunctuation,
        EN003_BoundariesLineEndingsAbbreviationsDecimals,
        EN004_TechnicalTokensAndExclusions,
        EN005_DuplicateContextsAndEnglishIdentityRules
    };

    public static EnglishGoldCorpusCaseResult ExecuteCase(
        EnglishGoldCorpusCase corpusCase,
        TextAnalyzer? analyzer = null)
    {
        ArgumentNullException.ThrowIfNull(corpusCase);

        analyzer ??= new TextAnalyzer();
        var analyzerEvidence = AnalysisEvidenceCollector.Collect(
            corpusCase.Input,
            sourceLanguage: corpusCase.SourceLanguage,
            enableGermanCompoundDecomposition: false,
            germanLexicon: null,
            analyzer: analyzer);

        return new EnglishGoldCorpusCaseResult(
            corpusCase.Id,
            corpusCase.Description,
            corpusCase.Authority,
            analyzerEvidence);
    }
}
