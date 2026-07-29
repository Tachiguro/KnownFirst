using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>
/// Test-only fault-injection hook for the Schema-8 preparation-accept path (KF-MEANING-001 Slice 3),
/// mirroring <see cref="IBackupImportFailureInjector.AtCheckpoint"/>. Never referenced by production
/// callers — the default (no injector supplied to <see cref="PreparationService"/>) is exactly ordinary
/// production behavior. A test can throw from <see cref="AtCheckpoint"/> to prove that
/// <see cref="PreparationService.AcceptAsync"/> rolls back completely (including <c>PRAGMA user_version</c>
/// staying untouched) at that exact boundary.
/// </summary>
public interface IPreparationFaultInjector
{
    void AtCheckpoint(string checkpointName);
}

/// <summary>
/// The nine required, stable Schema-8 preparation-accept fault-injection checkpoints (KF-MEANING-001
/// Slice 3). Every checkpoint fires inside the same <c>RunInTransactionAsync</c> transaction as
/// <see cref="PreparationService.AcceptAsync"/>'s Schema-8 branch, so an injected exception at any one of
/// them rolls back every mutation made so far in that call, leaves <c>PRAGMA user_version</c> unchanged,
/// and leaves the candidate retryable.
/// </summary>
public static class PreparationSchema8Checkpoints
{
    /// <summary>Right after the candidate's envelope is guaranteed valid (loaded as-is, or lazily upgraded and persisted).</summary>
    public const string AfterEnvelopePersist = "AfterEnvelopePersist";

    /// <summary>Right after the target Sense is resolved (matched or newly inserted).</summary>
    public const string AfterSenseInsert = "AfterSenseInsert";

    /// <summary>Right after the target Meaning is resolved (exact-duplicate match or newly inserted) and the Sense's default Meaning is backfilled.</summary>
    public const string AfterMeaningInsert = "AfterMeaningInsert";

    /// <summary>Right after the frozen evidence is linked into new <c>ContextSnapshots</c> rows.</summary>
    public const string AfterContextLink = "AfterContextLink";

    /// <summary>Right after missing direction cards are inserted for the resolved Sense/Meaning.</summary>
    public const string AfterCardInsert = "AfterCardInsert";

    /// <summary>Right after the explicitly accepted provider index is added to the in-memory resolved set.</summary>
    public const string AfterResolvedIndexPersist = "AfterResolvedIndexPersist";

    /// <summary>Fires once, at the start of the opportunistic all-exact auto-linking pass over every other unresolved index.</summary>
    public const string DuringAutoExactVariantLinking = "DuringAutoExactVariantLinking";

    /// <summary>Right before this call's final candidate-state (ledger) commit, regardless of completion outcome.</summary>
    public const string BeforeCandidateCompletion = "BeforeCandidateCompletion";

    /// <summary>Right before a candidate that has just become fully resolved is marked Prepared and the session/word counters update.</summary>
    public const string BeforeAutomaticCandidateCompletion = "BeforeAutomaticCandidateCompletion";
}

public sealed partial class PreparationService
{
    /// <summary>
    /// Schema-8 multi-Sense AcceptAsync (KF-MEANING-001 Slice 3). May write only Senses, Meanings,
    /// ContextSnapshots, LearningCards, PreparationCandidate/PreparationSession workflow rows, and
    /// Word.PreparationState — never WordStatus.Prepared/Learning/Mastered and never the frozen
    /// automatic-progress columns. Existing (SenseId, Direction) cards are never repointed.
    /// </summary>
    private bool AcceptSchema8(
        SQLiteConnection connection,
        int candidateId,
        PreparedMeaningInput input,
        CardDirectionPreference cardDirectionPreference,
        ValidatedPreparationSchema8Capability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        // KF-MEANING-001 Slice 3 (§8): TopicOrDomain/PartOfSpeech bounds are validated before any mutation,
        // including before the candidate/envelope is even looked up.
        var normalizedTopicOrDomain = PreparationMetadataPolicy.NormalizeTopicOrDomain(input.TopicOrDomain);
        var normalizedExplicitPartOfSpeech = PreparationMetadataPolicy.NormalizePartOfSpeech(input.PartOfSpeech);

        var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
            ?? throw new InvalidOperationException("The preparation candidate does not exist.");
        EnsureCurrentCandidate(connection, candidate);
        var session = connection.Find<PreparationSessionEntity>(candidate.SessionId)
            ?? throw new InvalidOperationException("The preparation session does not exist.");
        var word = connection.Find<WordEntity>(candidate.WordId)
            ?? throw new InvalidOperationException("The preparation word does not exist.");

        // Lazy upgrade (§6): genuine EnvelopeV1 stays byte-identical; Empty/LegacyLexicalResult become a
        // valid envelope (with newly frozen evidence for Empty) before any further use.
        EnsureCandidateEnvelopeAndSelection(connection, candidate);
        Trip(PreparationSchema8Checkpoints.AfterEnvelopePersist);

        var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Envelope!;
        if (envelope.Result is null)
        {
            throw new InvalidOperationException("The preparation candidate has no lexical result to accept.");
        }

        // §7: SelectedMeaningIndex is read fresh, inside this transaction, from the just-reloaded candidate.
        var targetIndex = candidate.SelectedMeaningIndex;
        if (targetIndex < 0 || targetIndex >= envelope.Result.Meanings.Count)
        {
            throw new InvalidOperationException(
                $"The selected provider meaning index {targetIndex} is out of range for {envelope.Result.Meanings.Count} meaning(s).");
        }

        var resolved = new SortedSet<int>(envelope.ResolvedProviderMeaningIndexes);
        if (resolved.Contains(targetIndex))
        {
            throw new InvalidOperationException(
                $"Provider meaning index {targetIndex} has already been resolved for this candidate (stale selection or duplicate acceptance).");
        }

        var targetMeaningForIndexCheck = envelope.Result.Meanings[targetIndex];
        if (!string.IsNullOrWhiteSpace(input.SelectedMeaningId)
            && !string.Equals(input.SelectedMeaningId!.Trim(), targetMeaningForIndexCheck.MeaningId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The submitted SelectedMeaningId does not match the currently persisted provider meaning at index {targetIndex} (stale selection).");
        }

        var now = clock.UtcNow;
        // §4: only the candidate's own frozen evidence is ever linked — never recomputed from a fresh scan.
        var contextData = ResolveContextDataFromFrozenEvidence(connection, word.Id, envelope.FrozenEvidence);
        var explanationLanguage = contextData.FirstOrDefault()?.ExplanationLanguage ?? word.Language;
        var vocabularyIdentityKey = KnownFirst.Core.Text.VocabularyIdentityPolicy
            .Resolve(word.CanonicalTerm, word.TokenKind, word.Language).Identity;

        // The explicitly accepted index: full content review, Sense match/create, exact-variant dedup,
        // card creation. Every other still-unresolved index is only auto-linked when it independently
        // classifies Equal against an existing Sense (never given content it was never reviewed for).
        var targetMeaning = envelope.Result.Meanings[targetIndex];
        var existingSenses = LoadSenses(connection, word.Id);
        var targetFacts = ResolveDiscriminatorFacts(word, envelope.Result, targetMeaning, normalizedTopicOrDomain, input, explanationLanguage);
        var (matchedSense, _) = PreparationSenseClassifier.ClassifyAgainstExisting(
            word.Language, vocabularyIdentityKey, targetFacts, existingSenses);

        // §8 PartOfSpeech precedence: explicit input (already normalized/validated above), else the
        // selected provider Meaning, else empty.
        var partOfSpeech = normalizedExplicitPartOfSpeech.Length > 0
            ? normalizedExplicitPartOfSpeech
            : PreparationMetadataPolicy.NormalizePartOfSpeech(targetMeaning.PartOfSpeech);
        var senseId = matchedSense?.Id ?? InsertSense(connection, word.Id, targetFacts, partOfSpeech, now);
        if (matchedSense is null)
        {
            existingSenses = [.. existingSenses, LoadSense(connection, senseId)];
        }

        Trip(PreparationSchema8Checkpoints.AfterSenseInsert);

        var preparedTokenKind = !string.IsNullOrWhiteSpace(input.AcronymExpansion)
            && AcronymExpansionDetector.IsAcronymCandidate(word.CanonicalTerm)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : word.TokenKind;
        var aliases = input.AcceptedAliases
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var candidateItem = BuildCandidateMeaningItem(word, input, explanationLanguage, preparedTokenKind, aliases, now);
        var semanticIdentity = SemanticMeaningIdentityPolicy.Compute(
            candidateItem,
            VocabularyMergeIdentityPolicy.Compute(word.Language, vocabularyIdentityKey),
            targetFacts.TopicOrDomain);

        var meaningId = TryFindExactDuplicateMeaning(connection, senseId, candidateItem, semanticIdentity)
            ?? InsertMeaning(connection, word.Id, senseId, candidateItem, preparedTokenKind, now);

        BackfillDefaultMeaningIfMissing(connection, senseId, meaningId);

        Trip(PreparationSchema8Checkpoints.AfterMeaningInsert);

        // §4: the candidate's frozen evidence is never recomputed or replaced here — only linked.
        InsertNewContextSnapshots(connection, meaningId, senseId, word.Id, contextData, now);

        Trip(PreparationSchema8Checkpoints.AfterContextLink);

        EnsureCardsForDirections(connection, word.Id, senseId, meaningId, cardDirectionPreference, now);

        Trip(PreparationSchema8Checkpoints.AfterCardInsert);

        resolved.Add(targetIndex);

        Trip(PreparationSchema8Checkpoints.AfterResolvedIndexPersist);

        // All-exact automatic completion (§9): opportunistically auto-link every other still-unresolved
        // index that independently classifies Equal against an existing Sense for this Word — recognized
        // as more evidence for a Sense the word already has, never a silent loss (the ledger still records
        // it as resolved) and never a new Sense/Meaning/Card for content nobody reviewed.
        Trip(PreparationSchema8Checkpoints.DuringAutoExactVariantLinking);
        for (var index = 0; index < envelope.Result.Meanings.Count; index++)
        {
            if (resolved.Contains(index))
            {
                continue;
            }

            var meaning = envelope.Result.Meanings[index];
            var facts = ProviderOnlyDiscriminatorFacts(word, envelope.Result, meaning, explanationLanguage);
            var (_, outcome) = PreparationSenseClassifier.ClassifyAgainstExisting(
                word.Language, vocabularyIdentityKey, facts, existingSenses);
            if (outcome == SenseMatchOutcome.Equal)
            {
                resolved.Add(index);
            }
        }

        // §4/§5: FrozenEvidence is carried forward byte-identical — only ResolvedProviderMeaningIndexes changes.
        var updatedEnvelope = envelope with
        {
            ResolvedProviderMeaningIndexes = resolved.ToArray()
        };
        candidate.ResultJson = PreparationCandidatePayloadCodec.Write(updatedEnvelope);
        candidate.UpdatedAtUtc = now;

        var isFullyResolved = resolved.Count >= envelope.Result.Meanings.Count;
        Trip(PreparationSchema8Checkpoints.BeforeCandidateCompletion);
        if (isFullyResolved)
        {
            Trip(PreparationSchema8Checkpoints.BeforeAutomaticCandidateCompletion);
            word.PreparationState = PreparationState.Prepared;
            word.UpdatedAt = now;
            connection.Update(word);
            connection.Update(candidate);
            CompleteCandidate(connection, session, candidate, PreparationCandidateStatus.Prepared, now);
        }
        else
        {
            connection.Update(candidate);
        }

        return true;
    }

    private void Trip(string checkpoint) => faultInjector?.AtCheckpoint(checkpoint);

    private static List<SenseRow> LoadSenses(SQLiteConnection connection, int wordId) =>
        connection.Query<SenseRow>("SELECT * FROM Senses WHERE WordId = ? ORDER BY Id", wordId);

    private static SenseRow LoadSense(SQLiteConnection connection, int senseId) =>
        connection.Query<SenseRow>("SELECT * FROM Senses WHERE Id = ?", senseId).Single();

    /// <summary>
    /// Post-lookup all-exact auto-resolution (KF-MEANING-001 Slice 3 §9): every provider index that
    /// independently classifies Equal against one of the Word's EXISTING Senses (present before this
    /// lookup — never a Sense this same call could create, since no Sense/Meaning is ever inserted here)
    /// is auto-resolved without creating any Sense/Meaning/Card. Existing-Sense-New-Exact-Variant, NewSense,
    /// and Ambiguous provider indexes are left unresolved. Returns the possibly-updated envelope, the
    /// lowest still-unresolved index (0 when none remain or the envelope has no meanings), and whether
    /// every index is now resolved.
    /// </summary>
    private static (PreparationCandidatePayloadV1 Envelope, int NextSelectedIndex, bool IsFullyResolved) AutoResolveExactVariantsAfterLookup(
        SQLiteConnection connection, WordEntity word, PreparationCandidatePayloadV1 envelope)
    {
        if (envelope.Result is null || envelope.Result.Meanings.Count == 0)
        {
            return (envelope, 0, false);
        }

        var existingSenses = LoadSenses(connection, word.Id);
        var resolved = new SortedSet<int>(envelope.ResolvedProviderMeaningIndexes);
        if (existingSenses.Count > 0)
        {
            var vocabularyIdentityKey = KnownFirst.Core.Text.VocabularyIdentityPolicy
                .Resolve(word.CanonicalTerm, word.TokenKind, word.Language).Identity;
            for (var index = 0; index < envelope.Result.Meanings.Count; index++)
            {
                if (resolved.Contains(index))
                {
                    continue;
                }

                var meaning = envelope.Result.Meanings[index];
                var facts = ProviderOnlyDiscriminatorFacts(word, envelope.Result, meaning, envelope.Result.ExplanationLanguage);
                var (_, outcome) = PreparationSenseClassifier.ClassifyAgainstExisting(
                    word.Language, vocabularyIdentityKey, facts, existingSenses);
                if (outcome == SenseMatchOutcome.Equal)
                {
                    resolved.Add(index);
                }
            }
        }

        var updatedEnvelope = resolved.Count == envelope.ResolvedProviderMeaningIndexes.Count
            ? envelope
            : envelope with { ResolvedProviderMeaningIndexes = resolved.ToArray() };
        var isFullyResolved = resolved.Count >= envelope.Result.Meanings.Count;
        var nextIndex = 0;
        for (var index = 0; index < envelope.Result.Meanings.Count; index++)
        {
            if (!resolved.Contains(index))
            {
                nextIndex = index;
                break;
            }
        }

        return (updatedEnvelope, nextIndex, isFullyResolved);
    }

    private static SenseDiscriminatorFacts ResolveDiscriminatorFacts(
        WordEntity word,
        LexicalResult result,
        LexicalMeaning meaning,
        string normalizedTopicOrDomain,
        PreparedMeaningInput input,
        string explanationLanguage) => new(
        word.Language,
        explanationLanguage,
        !string.IsNullOrWhiteSpace(input.SelectedMeaningId) ? input.SelectedMeaningId!.Trim() : meaning.MeaningId ?? string.Empty,
        normalizedTopicOrDomain,
        !string.IsNullOrWhiteSpace(input.GrammaticalRelationship)
            ? input.GrammaticalRelationship!.Trim()
            : result.GrammaticalRelationship ?? string.Empty,
        !string.IsNullOrWhiteSpace(input.AcronymExpansion)
            ? input.AcronymExpansion!.Trim()
            : result.AcronymExpansion ?? string.Empty);

    private static SenseDiscriminatorFacts ProviderOnlyDiscriminatorFacts(
        WordEntity word, LexicalResult result, LexicalMeaning meaning, string explanationLanguage) => new(
        word.Language,
        explanationLanguage,
        meaning.MeaningId ?? string.Empty,
        string.Empty,
        result.GrammaticalRelationship ?? string.Empty,
        result.AcronymExpansion ?? string.Empty);

    private static int InsertSense(
        SQLiteConnection connection, int wordId, SenseDiscriminatorFacts facts, string partOfSpeech, DateTime now)
    {
        connection.Execute(
            """
            INSERT INTO Senses
                (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                 PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, ?)
            """,
            Guid.NewGuid().ToString("N"), wordId, facts.SourceLanguage, facts.ExplanationLanguage,
            facts.ProviderSenseId, facts.TopicOrDomain, partOfSpeech, facts.GrammaticalRelationship,
            facts.AcronymExpansion, (int)SenseStatus.Prepared, now, now);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    private static BackupPreparedItem BuildCandidateMeaningItem(
        WordEntity word,
        PreparedMeaningInput input,
        string explanationLanguage,
        KnownFirst.Core.Text.TokenKind preparedTokenKind,
        string[] aliases,
        DateTime now) => new(
        Id: string.Empty,
        VocabularyId: string.Empty,
        SourceLanguage: word.Language,
        ExplanationLanguage: explanationLanguage,
        DisplayTerm: string.IsNullOrWhiteSpace(input.CanonicalLearningTerm)
            ? word.CanonicalTerm
            : input.CanonicalLearningTerm.Trim(),
        EncounteredSurfaceForm: input.EncounteredSurfaceForm?.Trim() ?? string.Empty,
        GrammaticalRelationship: input.GrammaticalRelationship?.Trim() ?? string.Empty,
        TokenKind: (BackupTokenKind)(int)preparedTokenKind,
        ProviderMeaningId: input.SelectedMeaningId ?? string.Empty,
        AcronymExpansion: input.AcronymExpansion?.Trim() ?? string.Empty,
        Translation: input.Translation?.Trim() ?? string.Empty,
        Definition: input.Definition.Trim(),
        DictionaryExample: input.DictionaryExample?.Trim() ?? string.Empty,
        AdditionalNote: input.AdditionalNote?.Trim() ?? string.Empty,
        LegacyAnswerText: null,
        AcceptedAliases: aliases,
        ConfirmedByUser: true,
        Source: new BackupSourceReference(
            input.ProviderName, input.SourceProject, input.SourcePageTitle, input.SourceRevisionId, input.Attribution),
        CreatedAtUtc: now,
        UpdatedAtUtc: now,
        PreparedAtUtc: now,
        Contexts: []);

    private static int? TryFindExactDuplicateMeaning(
        SQLiteConnection connection, int senseId, BackupPreparedItem candidateItem, SemanticMeaningIdentity semanticIdentity)
    {
        var candidateVariant = ExactMeaningVariantIdentityPolicy.Compute(candidateItem, semanticIdentity);
        var existingRows = connection.Query<LegacyMeaningRow>(
            "SELECT * FROM Meanings WHERE SenseId = ? ORDER BY Id", senseId);
        foreach (var row in existingRows)
        {
            var existingItem = Schema8SemanticUpgradePolicy.BuildPreparedItem(row);
            var existingVariant = ExactMeaningVariantIdentityPolicy.Compute(existingItem, semanticIdentity);
            if (existingVariant == candidateVariant)
            {
                return row.Id;
            }
        }

        return null;
    }

    private static int InsertMeaning(
        SQLiteConnection connection,
        int wordId,
        int senseId,
        BackupPreparedItem item,
        KnownFirst.Core.Text.TokenKind tokenKind,
        DateTime now)
    {
        connection.Execute(
            """
            INSERT INTO Meanings
                (WordId, SenseId, StableId, ExplanationLanguage, SourceLanguage, DisplayTerm,
                 EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId,
                 AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote,
                 AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                 SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            wordId, senseId, Guid.NewGuid().ToString("N"), item.ExplanationLanguage, item.SourceLanguage,
            item.DisplayTerm, item.EncounteredSurfaceForm ?? string.Empty, item.GrammaticalRelationship ?? string.Empty,
            (int)tokenKind, item.ProviderMeaningId ?? string.Empty, item.AcronymExpansion ?? string.Empty,
            item.Translation ?? string.Empty, item.Definition ?? string.Empty, item.DictionaryExample ?? string.Empty,
            item.AdditionalNote ?? string.Empty,
            System.Text.Json.JsonSerializer.Serialize(item.AcceptedAliases.ToArray(), LexicalJsonSerializerContext.Default.StringArray),
            !string.IsNullOrWhiteSpace(item.Translation) ? item.Translation : item.Definition,
            item.Source.ProviderName, item.Source.SourceProject, item.Source.PageTitle, item.Source.RevisionId,
            item.Source.Attribution, item.ConfirmedByUser, now, now, now);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    private static void BackfillDefaultMeaningIfMissing(SQLiteConnection connection, int senseId, int meaningId)
    {
        var hasDefault = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Senses WHERE Id = ? AND DefaultMeaningId IS NOT NULL", senseId) > 0;
        if (!hasDefault)
        {
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);
        }
    }

    /// <summary>
    /// Reconstructs the exact <see cref="ContextData"/> rows for a candidate's already-frozen evidence
    /// (KF-MEANING-001 Slice 3, §4) by re-scanning the Word's occurrences and matching each frozen
    /// four-field key — never a fresh, ledger-driven scan. Documents/sentences/occurrences are immutable
    /// once imported, so the same frozen key always resolves to the same text/position; a key that no
    /// longer resolves (should not happen in practice) is silently skipped rather than failing Accept.
    /// </summary>
    private static List<ContextData> ResolveContextDataFromFrozenEvidence(
        SQLiteConnection connection, int wordId, IReadOnlyList<PreparationCandidateEvidence> frozenEvidence)
    {
        if (frozenEvidence.Count == 0)
        {
            return [];
        }

        var byKey = new Dictionary<KnownFirst.Core.Preparation.ContextEvidenceKey, ContextData>();
        foreach (var context in Schema8EvidenceScanner.EnumerateAllValidContexts(connection, wordId))
        {
            var key = KnownFirst.Core.Preparation.PreparationContextEvidencePolicy.CreateKey(
                context.DocumentId, context.Text, context.TargetStart, context.TargetLength);
            byKey.TryAdd(key, context);
        }

        var result = new List<ContextData>();
        foreach (var evidence in frozenEvidence)
        {
            var key = new KnownFirst.Core.Preparation.ContextEvidenceKey(
                evidence.SourceDocumentId, evidence.NormalizedFingerprint, evidence.TargetStart, evidence.TargetLength);
            if (byKey.TryGetValue(key, out var context))
            {
                result.Add(context);
            }
        }

        return result;
    }

    private void InsertNewContextSnapshots(
        SQLiteConnection connection,
        int meaningId,
        int senseId,
        int wordId,
        List<ContextData> contextData,
        DateTime now)
    {
        var alreadyPersisted = new HashSet<string>(
            connection.Query<FingerprintRow>(
                "SELECT NormalizedFingerprint FROM ContextSnapshots WHERE MeaningId = ?", meaningId)
                .Select(r => r.NormalizedFingerprint),
            StringComparer.Ordinal);

        foreach (var context in contextData.Take(MaximumContextSnapshots))
        {
            var fingerprint = CreateFingerprint(NormalizeContext(context.Text));
            if (!alreadyPersisted.Add(fingerprint))
            {
                continue;
            }

            connection.Execute(
                """
                INSERT INTO ContextSnapshots
                    (MeaningId, SenseId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart,
                     TargetLength, NormalizedFingerprint, CreatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                meaningId, senseId, wordId, context.DocumentId, context.DocumentTitle, context.Text,
                context.TargetStart, context.TargetLength, fingerprint, now);
        }
    }

    private static void EnsureCardsForDirections(
        SQLiteConnection connection,
        int wordId,
        int senseId,
        int meaningId,
        CardDirectionPreference cardDirectionPreference,
        DateTime now)
    {
        foreach (var direction in CardDirectionPreferencePolicy.GetDirections(cardDirectionPreference))
        {
            var exists = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM LearningCards WHERE SenseId = ? AND Direction = ?", senseId, (int)direction) > 0;
            if (exists)
            {
                // Existing (SenseId, Direction) cards are never repointed when adding a variant.
                continue;
            }

            connection.Execute(
                """
                INSERT INTO LearningCards
                    (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                     SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, 0, ?, 0, 0, NULL, NULL, ?, ?)
                """,
                wordId, senseId, meaningId, (int)direction, (int)CardState.New, now,
                SimpleSpacedRepetitionScheduler.DefaultEaseFactor, now, now);
        }
    }

    private sealed class FingerprintRow
    {
        public string NormalizedFingerprint { get; set; } = string.Empty;
    }
}
