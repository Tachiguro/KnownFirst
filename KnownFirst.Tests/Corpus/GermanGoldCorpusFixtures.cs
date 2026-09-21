using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Tests.AnalysisEvidence;

namespace KnownFirst.Tests.Corpus;

public sealed record PreImportSeedWord(
    string CanonicalTerm,
    string NormalizedTerm,
    WordStatus Status,
    int Occurrences = 1,
    int DocCount = 1,
    string Language = "de");

public sealed record GermanGoldCorpusCase(
    string Id,
    string Description,
    string Input,
    CorpusExpectationAuthority Authority,
    string SourceLanguage = "de",
    bool EnableGermanCompoundDecomposition = false,
    IGermanLexicon? GermanLexicon = null,
    bool ExecuteReviewAdmission = false,
    bool IsDuplicateTest = false,
    IReadOnlyList<PreImportSeedWord>? SeedWords = null);

public sealed class AmbiguousFixtureLexicon : IGermanLexicon
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


public static class GermanGoldCorpusFixtures
{
    public static readonly GermanGoldCorpusCase DE001_BasicWords = new(
        Id: "DE-001-basic-words",
        Description: "Basic ordinary German words with sentence-initial capitalization, German nouns, repeated tokens, frequency 1, and context selection.",
        Input: "Das Buch ist gut. Ein Buch hilft oft.",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly GermanGoldCorpusCase DE002_UmlautsAndEszett = new(
        Id: "DE-002-umlauts-and-eszett",
        Description: "German characters (umlauts ä, ö, ü and eszett ß), exact UTF-16 coordinates, and final non-terminated sentence remainder.",
        Input: "Große Füße, schöne Vögel und weiße Mäuse",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly GermanGoldCorpusCase DE003_UnicodeComposedDecomposed = new(
        Id: "DE-003-unicode-composed-decomposed",
        Description: "Composed and decomposed Unicode normalization to Form C, exact UTF-16 coordinates, and case-only/encountered-form grouping.",
        Input: "Schöne Bäume. Scho\u0308ne Ba\u0308ume.",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior);

    public static readonly GermanGoldCorpusCase DE004_PunctuationQuotesApostrophes = new(
        Id: "DE-004-punctuation-quotes-apostrophes",
        Description: "Punctuation adjacency, German typographic quotation marks, straight quotes, and typographic apostrophe.",
        Input: "Er sagte: „Geht’s heute noch?\" Sie antwortete: 'Vielleicht.'",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior);

    public static readonly GermanGoldCorpusCase DE005_HyphenatedWords = new(
        Id: "DE-005-hyphenated-words",
        Description: "Hyphenated forms and technical tokens with internal hyphens.",
        Input: "Der Vor- und Nachteile-Vergleich ist ein Deutsch-Englisch-Wörterbuch.",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior);

    public static readonly GermanGoldCorpusCase DE006_LineEndingsCrlfLfCr = new(
        Id: "DE-006-line-endings-crlf-lf-cr",
        Description: "Preservation of CRLF, LF, and CR line endings without line-ending normalization and exact sentence boundaries.",
        Input: "Erste Zeile.\r\nZweite Zeile.\nDritte Zeile.\rVierte Zeile.",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly GermanGoldCorpusCase DE007_AbbreviationsAndDecimals = new(
        Id: "DE-007-abbreviations-and-decimals",
        Description: "Abbreviations with boundary suppression, decimal punctuation suppression, and standalone number exclusions.",
        Input: "Dr. Müller zahlte 12.50 Euro für ca. 3 Äpfel bzw. Birnen.",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior);

    public static readonly GermanGoldCorpusCase DE008_TechnicalTokensAndExclusions = new(
        Id: "DE-008-technical-tokens-and-exclusions",
        Description: "Technical tokens (OAuth2, IPv6, CVE family, SHA family), URL exclusion, and email address exclusion.",
        Input: "OAuth2 und IPv6 mit CVE-2023-1234 und SHA-256 auf https://example.org von info@example.com.",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly GermanGoldCorpusCase DE009_DuplicateContextSuppression = new(
        Id: "DE-009-duplicate-context-suppression",
        Description: "Duplicate context suppression for identical sentences containing the same candidate.",
        Input: "Das Buch liegt hier. Das Buch liegt hier.",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior);

    public static readonly GermanGoldCorpusCase DE010_GermanCoordinatedCompounds = new(
        Id: "DE-010-german-coordinated-compounds",
        Description: "German coordinated process compounds normalization to full singular process terms.",
        Input: "Arbeits-, Qualitäts-, Sicherheits- und Datenschutzprozesse sind wichtig.",
        Authority: CorpusExpectationAuthority.BindingContract);

    public static readonly GermanGoldCorpusCase DE011_ConservativeCompoundDecomposition = new(
        Id: "DE-011-conservative-compound-decomposition",
        Description: "Conservative German compound decomposition into whole direct candidate and derived components.",
        Input: "Die Schreibmaschine steht im Zimmer.",
        Authority: CorpusExpectationAuthority.BindingContract,
        EnableGermanCompoundDecomposition: true,
        GermanLexicon: new FixtureGermanLexicon());

    public static readonly GermanGoldCorpusCase DE012_CompoundDirectDerivedCollision = new(
        Id: "DE-012-compound-direct-derived-collision",
        Description: "Direct-versus-derived collision where existing direct candidate takes precedence over colliding derived identity.",
        Input: "Die Schreibmaschine und die Maschine stehen hier.",
        Authority: CorpusExpectationAuthority.VerifiedCurrentBehavior,
        EnableGermanCompoundDecomposition: true,
        GermanLexicon: new FixtureGermanLexicon());

    public static readonly GermanGoldCorpusCase DE013_AmbiguousFailClosedCompound = new(
        Id: "DE-013-ambiguous-fail-closed-compound",
        Description: "Ambiguous compound decomposition fails closed without guessing and emits no derived candidates.",
        Input: "Der Begriff Xyzabc wird analysiert.",
        Authority: CorpusExpectationAuthority.BindingContract,
        EnableGermanCompoundDecomposition: true,
        GermanLexicon: new AmbiguousFixtureLexicon());

    public static readonly GermanGoldCorpusCase DE014_ReviewAdmissionNewAndExistingUnreviewed = new(
        Id: "DE-014-review-admission-new-and-existing-unreviewed",
        Description: "Review admission for new vocabulary and existing Unreviewed vocabulary in an Accepted import.",
        Input: "Alpha Beta.",
        Authority: CorpusExpectationAuthority.BindingContract,
        ExecuteReviewAdmission: true,
        SeedWords: new[]
        {
            new PreImportSeedWord("Beta", "W:beta", WordStatus.Unreviewed, Occurrences: 1, DocCount: 1)
        });

    public static readonly GermanGoldCorpusCase DE015_ReviewAdmissionStatusExclusions = new(
        Id: "DE-015-review-admission-status-exclusions",
        Description: "Review admission in Accepted import with UnknownBacklog (excluded from review, occurrence contribution recorded), and Known/Ignored (excluded, no contribution).",
        Input: "Alpha Backlog Known Ignored.",
        Authority: CorpusExpectationAuthority.BindingContract,
        ExecuteReviewAdmission: true,
        SeedWords: new[]
        {
            new PreImportSeedWord("Backlog", "W:backlog", WordStatus.UnknownBacklog, Occurrences: 2, DocCount: 1),
            new PreImportSeedWord("Known", "W:known", WordStatus.Known, Occurrences: 3, DocCount: 1),
            new PreImportSeedWord("Ignored", "W:ignored", WordStatus.Ignored, Occurrences: 4, DocCount: 1)
        });

    public static readonly GermanGoldCorpusCase DE016_ReviewAdmissionNoNewVocabulary = new(
        Id: "DE-016-review-admission-no-new-vocabulary",
        Description: "Review admission resulting in NoNewVocabulary: early return without session creation or occurrence contribution.",
        Input: "Known Backlog.",
        Authority: CorpusExpectationAuthority.BindingContract,
        ExecuteReviewAdmission: true,
        SeedWords: new[]
        {
            new PreImportSeedWord("Known", "W:known", WordStatus.Known, Occurrences: 5, DocCount: 2),
            new PreImportSeedWord("Backlog", "W:backlog", WordStatus.UnknownBacklog, Occurrences: 3, DocCount: 1)
        });

    public static readonly GermanGoldCorpusCase DE017_ReviewAdmissionExactDuplicate = new(
        Id: "DE-017-review-admission-exact-duplicate",
        Description: "Review admission resulting in ExactDuplicate import outcome.",
        Input: "Einzigartiger Text fuer Duplikat.",
        Authority: CorpusExpectationAuthority.BindingContract,
        ExecuteReviewAdmission: true,
        IsDuplicateTest: true);

    public static readonly IReadOnlyList<GermanGoldCorpusCase> AllCases = new[]
    {
        DE001_BasicWords,
        DE002_UmlautsAndEszett,
        DE003_UnicodeComposedDecomposed,
        DE004_PunctuationQuotesApostrophes,
        DE005_HyphenatedWords,
        DE006_LineEndingsCrlfLfCr,
        DE007_AbbreviationsAndDecimals,
        DE008_TechnicalTokensAndExclusions,
        DE009_DuplicateContextSuppression,
        DE010_GermanCoordinatedCompounds,
        DE011_ConservativeCompoundDecomposition,
        DE012_CompoundDirectDerivedCollision,
        DE013_AmbiguousFailClosedCompound,
        DE014_ReviewAdmissionNewAndExistingUnreviewed,
        DE015_ReviewAdmissionStatusExclusions,
        DE016_ReviewAdmissionNoNewVocabulary,
        DE017_ReviewAdmissionExactDuplicate
    };

    public static async Task<GermanGoldCorpusEvidenceArtifact> ExecuteCaseAsync(
        GermanGoldCorpusCase corpusCase,
        TextAnalyzer? analyzer = null)
    {
        ArgumentNullException.ThrowIfNull(corpusCase);

        analyzer ??= new TextAnalyzer();
        var analyzerEvidence = AnalysisEvidenceCollector.Collect(
            corpusCase.Input,
            sourceLanguage: corpusCase.SourceLanguage,
            enableGermanCompoundDecomposition: corpusCase.EnableGermanCompoundDecomposition,
            germanLexicon: corpusCase.GermanLexicon,
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

        var database = new ReviewAdmissionCorrelationTests.AdmissionTestDatabase();
        await database.InitializeAsync();
        try
        {
            var settings = new DisabledEnhancedRecognitionSettings();

            var lexicon = corpusCase.GermanLexicon ?? new ReviewAdmissionCorrelationTests.ThrowingGermanLexicon();
            var service = new TextReviewService(database, analyzer, settings, lexicon);

            if (corpusCase.IsDuplicateTest)
            {
                var initialRequest = new ImportTextRequest("Initial doc", corpusCase.Input, corpusCase.SourceLanguage, "en");
                await service.ImportAsync(initialRequest);
                while (await service.GetCurrentCandidateAsync() is { } current)
                {
                    await service.DecideAsync(current.WordId, WordStatus.UnknownBacklog);
                }

                var duplicateRequest = new ImportTextRequest("Duplicate doc", corpusCase.Input, corpusCase.SourceLanguage, "en");
                var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(
                    database,
                    service,
                    duplicateRequest,
                    analyzer,
                    corpusCase.EnableGermanCompoundDecomposition,
                    corpusCase.GermanLexicon);

                return new GermanGoldCorpusEvidenceArtifact(
                    corpusCase.Id,
                    corpusCase.Description,
                    corpusCase.Authority,
                    analyzerEvidence,
                    evidence.ToArtifactEvidence());
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

            var request = new ImportTextRequest("Corpus doc", corpusCase.Input, corpusCase.SourceLanguage, "en");
            var reviewEvidence = await ReviewAdmissionCorrelator.CorrelateAsync(
                database,
                service,
                request,
                analyzer,
                corpusCase.EnableGermanCompoundDecomposition,
                corpusCase.GermanLexicon);

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
