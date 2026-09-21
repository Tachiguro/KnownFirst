using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Text;
using KnownFirst.Tests.AnalysisEvidence;

namespace KnownFirst.Tests;

[TestClass]
public sealed class AnalysisEvidenceHarnessTests
{
    [TestMethod]
    public void Collect_PreservesExactInputTextAndLength()
    {
        const string input = "  Häuser und Straße!\r\n\tNeue Zeile.  ";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        Assert.AreEqual(input, doc.Input.Text);
        Assert.AreEqual(input.Length, doc.Input.Length);
        Assert.AreEqual("de", doc.Input.SourceLanguage);

        var expectedSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        Assert.AreEqual(expectedSha256, doc.Input.ContentSha256);
    }

    [TestMethod]
    public void Collect_SentenceCoordinatesAndSubstringsMatchExactly()
    {
        const string input = "Erster Satz.  Zweiter Satz!\r\nDritter Satz?";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        Assert.AreEqual(3, doc.Sentences.Count);
        foreach (var sentence in doc.Sentences)
        {
            Assert.IsTrue(sentence.ExactSubstringMatch);
            Assert.AreEqual(input.Substring(sentence.StartPosition, sentence.Length), sentence.Text);
            Assert.IsFalse(string.IsNullOrWhiteSpace(sentence.BoundaryReasonCode));
            Assert.IsFalse(string.IsNullOrWhiteSpace(sentence.BoundaryExplanation));
        }
    }

    [TestMethod]
    public void Collect_CapturesIncludedAndExcludedTokenDecisions()
    {
        const string input = "Das Haus kostet 500 Euro. Siehe https://example.com.";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        Assert.IsTrue(doc.TokenDecisions.Count > 0);
        var included = doc.TokenDecisions.Where(t => t.IsIncluded).ToArray();
        var excluded = doc.TokenDecisions.Where(t => !t.IsIncluded).ToArray();

        Assert.IsTrue(included.Any(t => t.RawValue == "Haus"));
        Assert.IsTrue(excluded.Any(t => t.RawValue == "500"));
        Assert.IsTrue(excluded.Any(t => t.RawValue.Contains("example.com", StringComparison.Ordinal)));

        foreach (var decision in doc.TokenDecisions)
        {
            if (decision.StartPosition >= 0 && decision.StartPosition + decision.Length <= input.Length)
            {
                Assert.IsTrue(decision.ExactSubstringMatch);
                Assert.AreEqual(input.Substring(decision.StartPosition, decision.Length), decision.RawValue);
            }
            Assert.IsFalse(string.IsNullOrWhiteSpace(decision.ReasonCode));
            Assert.IsFalse(string.IsNullOrWhiteSpace(decision.Explanation));
        }
    }

    [TestMethod]
    public void Collect_CapturesCandidatesAndOccurrencesWithExactSubstrings()
    {
        const string input = "Ein Haus ist alt. Das Haus ist schön. Ein anderes Haus steht dort.";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        Assert.IsTrue(doc.Candidates.Count > 0);
        var hausCandidate = doc.Candidates.FirstOrDefault(c => c.CanonicalTerm == "Haus");
        Assert.IsNotNull(hausCandidate);
        Assert.AreEqual(3, hausCandidate.OccurrenceCount);
        Assert.AreEqual(3, hausCandidate.Occurrences.Count);

        foreach (var candidate in doc.Candidates)
        {
            foreach (var occ in candidate.Occurrences)
            {
                Assert.IsTrue(occ.ExactSubstringMatch);
                Assert.AreEqual(input.Substring(occ.StartPosition, occ.Length), occ.SurfaceForm);
            }

            // Surface forms are sorted deterministically
            var forms = candidate.SurfaceForms.Select(f => f.Form).ToArray();
            var sortedForms = forms.OrderBy(f => f, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(sortedForms, forms);
        }
    }

    [TestMethod]
    public void Collect_CapturesDiagnosticsAndInvariantValidationPass()
    {
        const string input = "Ein klarer Satz. Noch ein Satz.";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        Assert.IsTrue(doc.CandidateGroups.Count > 0);
        Assert.IsTrue(doc.ContextDecisions.Count > 0);
        Assert.AreEqual(0, doc.InvariantFailures.Count);
        Assert.IsTrue(doc.InvariantsPassed);

        foreach (var context in doc.ContextDecisions)
        {
            Assert.AreEqual(context.OccurrenceStartPosition - context.SentenceStartPosition, context.TargetStartInSentence);
            Assert.IsFalse(string.IsNullOrWhiteSpace(context.Fingerprint));
        }
    }

    [TestMethod]
    public void Collect_ProducesDifferentFingerprintsForCrlfVsLf()
    {
        const string lfText = "Erste Zeile.\nZweite Zeile.";
        const string crlfText = "Erste Zeile.\r\nZweite Zeile.";

        var docLf = AnalysisEvidenceCollector.Collect(lfText, sourceLanguage: "de");
        var docCrlf = AnalysisEvidenceCollector.Collect(crlfText, sourceLanguage: "de");

        Assert.AreNotEqual(docLf.Input.ContentSha256, docCrlf.Input.ContentSha256);
        Assert.AreNotEqual(docLf.Input.Length, docCrlf.Input.Length);
    }

    [TestMethod]
    public void TextFormatter_ProducesDeterministicLosslessEscapedReport()
    {
        const string input = "Häuser & Straße!\r\nZweite Zeile: \"Hallo\".";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        var text1 = AnalysisEvidenceTextFormatter.Format(doc);
        var text2 = AnalysisEvidenceTextFormatter.Format(doc);

        Assert.AreEqual(text1, text2);
        Assert.IsTrue(text1.Contains(@"\r\n", StringComparison.Ordinal));
        Assert.IsTrue(text1.Contains("=== ANALYSIS EVIDENCE REPORT ===", StringComparison.Ordinal));
        Assert.IsTrue(text1.Contains("--- SENTENCES (2) ---", StringComparison.Ordinal));
        Assert.IsTrue(text1.Contains("--- CANDIDATES", StringComparison.Ordinal));
        Assert.IsTrue(text1.Contains("INVARIANT VALIDATION", StringComparison.Ordinal));
    }

    [TestMethod]
    public void JsonFormatter_ProducesDeterministicOutput()
    {
        const string input = "Erster Satz. Zweiter Satz.";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        var json1 = AnalysisEvidenceJsonFormatter.Format(doc);
        var json2 = AnalysisEvidenceJsonFormatter.Format(doc);

        Assert.AreEqual(json1, json2);
        Assert.IsFalse(string.IsNullOrWhiteSpace(json1));
    }

    [TestMethod]
    public void JsonFormatter_RoundTripsExactTextCrlfLfUnicodeAndCoordinates()
    {
        const string input = "Häuser & Straße!\r\nEine neue Zeile mit \n gemischten Umbrüchen: \"Test\", Привет, κόσμος.";
        var doc = AnalysisEvidenceCollector.Collect(input, sourceLanguage: "de");

        var json = AnalysisEvidenceJsonFormatter.Format(doc);
        var roundTrip = AnalysisEvidenceJsonFormatter.Deserialize(json);

        Assert.AreEqual(doc.Input.Text, roundTrip.Input.Text);
        Assert.AreEqual(doc.Input.Length, roundTrip.Input.Length);
        Assert.AreEqual(doc.Input.SourceLanguage, roundTrip.Input.SourceLanguage);
        Assert.AreEqual(doc.Input.ContentSha256, roundTrip.Input.ContentSha256);

        Assert.AreEqual(doc.Sentences.Count, roundTrip.Sentences.Count);
        for (var i = 0; i < doc.Sentences.Count; i++)
        {
            Assert.AreEqual(doc.Sentences[i].StartPosition, roundTrip.Sentences[i].StartPosition);
            Assert.AreEqual(doc.Sentences[i].Length, roundTrip.Sentences[i].Length);
            Assert.AreEqual(doc.Sentences[i].Text, roundTrip.Sentences[i].Text);
            Assert.AreEqual(doc.Sentences[i].ExactSubstringMatch, roundTrip.Sentences[i].ExactSubstringMatch);
        }

        Assert.AreEqual(doc.Candidates.Count, roundTrip.Candidates.Count);
        for (var i = 0; i < doc.Candidates.Count; i++)
        {
            Assert.AreEqual(doc.Candidates[i].Identity, roundTrip.Candidates[i].Identity);
            Assert.AreEqual(doc.Candidates[i].CanonicalTerm, roundTrip.Candidates[i].CanonicalTerm);
            Assert.AreEqual(doc.Candidates[i].Kind, roundTrip.Candidates[i].Kind);
            Assert.AreEqual(doc.Candidates[i].Provenance, roundTrip.Candidates[i].Provenance);
            Assert.AreEqual(doc.Candidates[i].Occurrences.Count, roundTrip.Candidates[i].Occurrences.Count);
        }

        Assert.AreEqual(doc.InvariantsPassed, roundTrip.InvariantsPassed);
    }
}
