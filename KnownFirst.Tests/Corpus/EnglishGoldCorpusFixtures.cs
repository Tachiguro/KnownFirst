using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Tests.AnalysisEvidence;

namespace KnownFirst.Tests.Corpus;

public sealed record EnglishGoldCorpusCase(
    string Id,
    string Description,
    string Input,
    CorpusExpectationAuthority Authority,
    string SourceLanguage = "en",
    bool ExecuteReviewAdmission = false,
    bool IsDuplicateTest = false,
    IReadOnlyList<PreImportSeedWord>? SeedWords = null);

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

    public static readonly EnglishGoldCorpusCase EN006_ReviewAdmissionNewAndExistingUnreviewed = new(
        Id: "EN-006-review-admission-new-and-existing-unreviewed",
        Description: "English review admission for new vocabulary and existing Unreviewed vocabulary in an Accepted import.",
        Input: "Signal Packet.",
        Authority: CorpusExpectationAuthority.BindingContract,
        SourceLanguage: "en",
        ExecuteReviewAdmission: true,
        SeedWords: new[]
        {
            new PreImportSeedWord("Packet", "W:packet", WordStatus.Unreviewed, Occurrences: 2, DocCount: 1, Language: "en")
        });

    public static readonly EnglishGoldCorpusCase EN007_ReviewAdmissionStatusExclusions = new(
        Id: "EN-007-review-admission-status-exclusions",
        Description: "English review admission in Accepted import with UnknownBacklog (excluded from review, occurrence contribution recorded) and Known/Ignored (excluded, no contribution).",
        Input: "Beacon Backlog Known Ignored.",
        Authority: CorpusExpectationAuthority.BindingContract,
        SourceLanguage: "en",
        ExecuteReviewAdmission: true,
        SeedWords: new[]
        {
            new PreImportSeedWord("Backlog", "W:backlog", WordStatus.UnknownBacklog, Occurrences: 2, DocCount: 1, Language: "en"),
            new PreImportSeedWord("Known", "W:known", WordStatus.Known, Occurrences: 3, DocCount: 1, Language: "en"),
            new PreImportSeedWord("Ignored", "W:ignored", WordStatus.Ignored, Occurrences: 4, DocCount: 1, Language: "en")
        });

    public static readonly EnglishGoldCorpusCase EN008_ReviewAdmissionNoNewVocabulary = new(
        Id: "EN-008-review-admission-no-new-vocabulary",
        Description: "English review admission resulting in NoNewVocabulary: early return without session creation or occurrence contribution.",
        Input: "Known Backlog.",
        Authority: CorpusExpectationAuthority.BindingContract,
        SourceLanguage: "en",
        ExecuteReviewAdmission: true,
        SeedWords: new[]
        {
            new PreImportSeedWord("Known", "W:known", WordStatus.Known, Occurrences: 5, DocCount: 2, Language: "en"),
            new PreImportSeedWord("Backlog", "W:backlog", WordStatus.UnknownBacklog, Occurrences: 3, DocCount: 1, Language: "en")
        });

    public static readonly EnglishGoldCorpusCase EN009_ReviewAdmissionExactDuplicate = new(
        Id: "EN-009-review-admission-exact-duplicate",
        Description: "English review admission resulting in ExactDuplicate import outcome without fabricating review candidates.",
        Input: "Unique synthetic English text for duplicate import verification.",
        Authority: CorpusExpectationAuthority.BindingContract,
        SourceLanguage: "en",
        ExecuteReviewAdmission: true,
        IsDuplicateTest: true);

    public static readonly IReadOnlyList<EnglishGoldCorpusCase> AllCases = new[]
    {
        EN001_RepeatedWordsAndContexts,
        EN002_UnicodeOffsetsApostrophesPunctuation,
        EN003_BoundariesLineEndingsAbbreviationsDecimals,
        EN004_TechnicalTokensAndExclusions,
        EN005_DuplicateContextsAndEnglishIdentityRules,
        EN006_ReviewAdmissionNewAndExistingUnreviewed,
        EN007_ReviewAdmissionStatusExclusions,
        EN008_ReviewAdmissionNoNewVocabulary,
        EN009_ReviewAdmissionExactDuplicate
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

    public static async Task<GermanGoldCorpusEvidenceArtifact> ExecuteCaseAsync(
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

        if (!corpusCase.ExecuteReviewAdmission)
        {
            return new GermanGoldCorpusEvidenceArtifact(
                corpusCase.Id,
                corpusCase.Description,
                corpusCase.Authority,
                analyzerEvidence,
                ReviewAdmissionArtifactEvidence.None());
        }

        const string translationLanguage = "de";
        var database = new ReviewAdmissionCorrelationTests.AdmissionTestDatabase();
        await database.InitializeAsync();
        try
        {
            var settings = new DisabledEnhancedRecognitionSettings();
            var lexicon = new ReviewAdmissionCorrelationTests.ThrowingGermanLexicon();
            var service = new TextReviewService(database, analyzer, settings, lexicon);

            if (corpusCase.IsDuplicateTest)
            {
                var initialRequest = new ImportTextRequest(
                    "Initial English corpus doc",
                    corpusCase.Input,
                    corpusCase.SourceLanguage,
                    translationLanguage);
                await service.ImportAsync(initialRequest);
                while (await service.GetCurrentCandidateAsync() is { } current)
                {
                    await service.DecideAsync(current.WordId, WordStatus.UnknownBacklog);
                }

                var duplicateRequest = new ImportTextRequest(
                    "Duplicate English corpus doc",
                    corpusCase.Input,
                    corpusCase.SourceLanguage,
                    translationLanguage);
                var duplicateEvidence = await ReviewAdmissionCorrelator.CorrelateAsync(
                    database,
                    service,
                    duplicateRequest,
                    analyzer,
                    enableGermanCompoundDecomposition: false,
                    germanLexicon: null);

                return new GermanGoldCorpusEvidenceArtifact(
                    corpusCase.Id,
                    corpusCase.Description,
                    corpusCase.Authority,
                    analyzerEvidence,
                    duplicateEvidence.ToArtifactEvidence());
            }

            if (corpusCase.SeedWords is { Count: > 0 } seedWords)
            {
                foreach (var seed in seedWords)
                {
                    await database.RunInTransactionAsync(conn =>
                    {
                        conn.Insert(new WordEntity
                        {
                            Language = seed.Language,
                            CanonicalTerm = seed.CanonicalTerm,
                            NormalizedTerm = seed.NormalizedTerm,
                            TokenKind = TokenKind.Word,
                            Status = seed.Status,
                            TotalOccurrenceCount = seed.Occurrences,
                            DocumentCount = seed.DocCount,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                        return true;
                    });
                }
            }

            var request = new ImportTextRequest(
                "English corpus doc",
                corpusCase.Input,
                corpusCase.SourceLanguage,
                translationLanguage);
            var reviewEvidence = await ReviewAdmissionCorrelator.CorrelateAsync(
                database,
                service,
                request,
                analyzer,
                enableGermanCompoundDecomposition: false,
                germanLexicon: null);

            return new GermanGoldCorpusEvidenceArtifact(
                corpusCase.Id,
                corpusCase.Description,
                corpusCase.Authority,
                analyzerEvidence,
                reviewEvidence.ToArtifactEvidence());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
