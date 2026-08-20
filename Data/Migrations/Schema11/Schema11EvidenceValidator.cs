using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Data.Migrations.Schema11;

internal static class Schema11EvidenceValidator
{
    public static bool Validate(SQLiteConnection connection, out string? failureDetail)
    {
        var duplicateCount = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM (
                SELECT ReviewCandidateId, SourceIdentity, SourceStartPosition, SourceLength, ComponentForm
                FROM DerivedTermEvidenceEntries
                GROUP BY ReviewCandidateId, SourceIdentity, SourceStartPosition, SourceLength, ComponentForm
                HAVING COUNT(*) > 1
            )
            """);
        if (duplicateCount > 0)
        {
            failureDetail = "Duplicate semantic evidence entry exists in DerivedTermEvidenceEntries.";
            return false;
        }

        var entries = connection.Table<DerivedTermEvidenceEntity>().ToList();
        if (entries.Count == 0)
        {
            failureDetail = null;
            return true;
        }

        var candidates = connection.Table<ReviewCandidateEntity>().ToList().ToDictionary(c => c.Id);
        var sessions = connection.Table<ReviewSessionEntity>().ToList().ToDictionary(s => s.Id);
        var documents = connection.Table<DocumentEntity>().ToList().ToDictionary(d => d.Id);
        var sentenceSpans = connection.Table<SentenceSpanEntity>().ToList();
        var words = connection.Table<WordEntity>().ToList();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceIdentity))
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} has a blank SourceIdentity.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.SourceSurfaceForm))
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} has a blank SourceSurfaceForm.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.ComponentForm))
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} has a blank ComponentForm.";
                return false;
            }

            if (entry.SourceStartPosition < 0)
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} has negative SourceStartPosition {entry.SourceStartPosition}.";
                return false;
            }

            if (entry.SourceLength <= 0)
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} has non-positive SourceLength {entry.SourceLength}.";
                return false;
            }

            if (!candidates.TryGetValue(entry.ReviewCandidateId, out var candidate))
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} references non-existent ReviewCandidate {entry.ReviewCandidateId}.";
                return false;
            }

            if (!sessions.TryGetValue(candidate.SessionId, out var session))
            {
                failureDetail = $"ReviewCandidate {candidate.Id} references non-existent ReviewSession {candidate.SessionId}.";
                return false;
            }

            if (!documents.TryGetValue(session.DocumentId, out var document))
            {
                failureDetail = $"ReviewSession {session.Id} references non-existent Document {session.DocumentId}.";
                return false;
            }

            if (document.Content is null)
            {
                failureDetail = $"Document {document.Id} has null Content.";
                return false;
            }

            var widenedEnd = (long)entry.SourceStartPosition + entry.SourceLength;
            if (widenedEnd < 0 || widenedEnd > document.Content.Length)
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} source range [{entry.SourceStartPosition}..{widenedEnd}) is out of bounds for Document {document.Id} content length {document.Content.Length}.";
                return false;
            }

            var actualSubstr = document.Content.Substring(entry.SourceStartPosition, entry.SourceLength);
            if (!string.Equals(actualSubstr, entry.SourceSurfaceForm, StringComparison.Ordinal))
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} SourceSurfaceForm '{entry.SourceSurfaceForm}' does not match document text '{actualSubstr}'.";
                return false;
            }

            var matchingSentences = sentenceSpans
                .Where(s => s.DocumentId == document.Id && s.Order == entry.SourceSentenceOrder)
                .ToList();
            if (matchingSentences.Count != 1)
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} expected exactly one SentenceSpan for Document {document.Id} Order {entry.SourceSentenceOrder}, but found {matchingSentences.Count}.";
                return false;
            }

            var sentence = matchingSentences[0];
            var widenedSentenceEnd = (long)sentence.StartPosition + sentence.Length;
            if (entry.SourceStartPosition < sentence.StartPosition || widenedEnd > widenedSentenceEnd)
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} range [{entry.SourceStartPosition}..{widenedEnd}) does not lie fully inside SentenceSpan [{sentence.StartPosition}..{widenedSentenceEnd}).";
                return false;
            }

            var matchingSourceWord = words.FirstOrDefault(w =>
                string.Equals(w.Language, document.TextLanguage, StringComparison.Ordinal)
                && string.Equals(w.NormalizedTerm, entry.SourceIdentity, StringComparison.Ordinal));
            if (matchingSourceWord is null)
            {
                failureDetail = $"DerivedTermEvidence entry {entry.Id} references SourceIdentity '{entry.SourceIdentity}' which has no Word row for language '{document.TextLanguage}'.";
                return false;
            }
        }

        failureDetail = null;
        return true;
    }
}
