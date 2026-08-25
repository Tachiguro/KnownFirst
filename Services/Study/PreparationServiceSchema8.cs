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
/// The eight required, stable Schema-8 preparation-accept fault-injection checkpoints (KF-MEANING-001
/// Slice 3; reduced from nine by KF-MEANING-002's independent review — see the removal note on the
/// former <c>DuringAutoExactVariantLinking</c> checkpoint). Every checkpoint fires inside the same
/// <c>RunInTransactionAsync</c> transaction as
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

    // KF-MEANING-002 independent review: the former DuringAutoExactVariantLinking checkpoint was removed
    // here. After the all-exact auto-linking loop it named was deleted, it fired with zero statements
    // between it and AfterResolvedIndexPersist — an identical, redundant rollback boundary under a name
    // that no longer described anything real. Kept as a code comment (not a checkpoint) so a future
    // reader does not wonder where checkpoint 6 of 9 went.

    /// <summary>Right before this call's final candidate-state (ledger) commit.</summary>
    public const string BeforeCandidateCompletion = "BeforeCandidateCompletion";

    /// <summary>
    /// Right before the candidate is marked Prepared and the session/word counters update (KF-MEANING-002:
    /// every successful accept reaches this point — there is no partial-acceptance branch).
    /// </summary>
    public const string BeforeAutomaticCandidateCompletion = "BeforeAutomaticCandidateCompletion";
}

public sealed partial class PreparationService
{
    /// <summary>
    /// Schema-8 AcceptAsync (KF-MEANING-001 Slice 3; completion rule corrected by KF-MEANING-002).
    /// Accepting one explicitly selected provider meaning completes the preparation of the current
    /// candidate: the selected Sense/Meaning is created or reused, cards/answer-variant assignments are
    /// created only for that Sense, and the candidate reaches <see cref="PreparationCandidateStatus.Prepared"/>
    /// immediately. Every other provider meaning is a suggestion the user did not choose — it is never
    /// reviewed, matched, or persisted, and it never gates completion. May write only Senses, Meanings,
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
        var isManualInput = input.ManualInputMode.HasValue || envelope.Result is null;
        // Only the candidate's already-frozen evidence determines the manual answer mode. This is the
        // same persisted document contract used by CreateItemAsync to expose PreparationItem.LookupMode
        // and TargetLanguage; a payload value can request manual handling but can never redirect it.
        var contextData = ResolveContextDataFromFrozenEvidence(connection, word.Id, envelope.FrozenEvidence);
        var candidateContext = ResolveCandidateLookupContext(connection, word, contextData);
        var acceptedInput = isManualInput
            ? NormalizeManualInput(input, candidateContext.LookupMode)
            : input;
        if (isManualInput)
        {
            ValidateManualInput(acceptedInput, candidateContext.LookupMode);
        }

        int? targetIndex = null;
        LexicalMeaning? targetMeaning = null;
        SortedSet<int>? resolved = null;
        if (!isManualInput)
        {
            if (envelope.Result is null)
            {
                throw new InvalidOperationException("The preparation candidate has no lexical result to accept.");
            }

            // §7: SelectedMeaningIndex is read fresh, inside this transaction, from the just-reloaded candidate.
            targetIndex = candidate.SelectedMeaningIndex;
            if (targetIndex < 0 || targetIndex >= envelope.Result.Meanings.Count)
            {
                throw new InvalidOperationException(
                    $"The selected provider meaning index {targetIndex} is out of range for {envelope.Result.Meanings.Count} meaning(s).");
            }

            resolved = new SortedSet<int>(envelope.ResolvedProviderMeaningIndexes);
            if (resolved.Contains(targetIndex.Value))
            {
                throw new InvalidOperationException(
                    $"Provider meaning index {targetIndex} has already been resolved for this candidate (stale selection or duplicate acceptance).");
            }

            targetMeaning = envelope.Result.Meanings[targetIndex.Value];
            if (!string.IsNullOrWhiteSpace(acceptedInput.SelectedMeaningId)
                && !string.Equals(acceptedInput.SelectedMeaningId!.Trim(), targetMeaning.MeaningId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The submitted SelectedMeaningId does not match the currently persisted provider meaning at index {targetIndex} (stale selection).");
            }
        }

        var now = clock.UtcNow;
        var explanationLanguage = candidateContext.ExplanationLanguage;
        var vocabularyIdentityKey = KnownFirst.Core.Text.VocabularyIdentityPolicy
            .Resolve(word.CanonicalTerm, word.TokenKind, word.Language).Identity;
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(word.Language, vocabularyIdentityKey);

        var existingSenses = LoadSenses(connection, word.Id);
        var targetFacts = isManualInput
            ? ResolveManualDiscriminatorFacts(word, normalizedTopicOrDomain, acceptedInput, explanationLanguage)
            : ResolveDiscriminatorFacts(
                word,
                envelope.Result!,
                targetMeaning!,
                normalizedTopicOrDomain,
                acceptedInput,
                explanationLanguage);
        var preparedTokenKind = !string.IsNullOrWhiteSpace(acceptedInput.AcronymExpansion)
            && AcronymExpansionDetector.IsAcronymCandidate(word.CanonicalTerm)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : word.TokenKind;
        var aliases = acceptedInput.AcceptedAliases
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var persistedInput = isManualInput
            ? WithoutProviderProvenance(acceptedInput, word.CanonicalTerm)
            : acceptedInput;
        var candidateItem = BuildCandidateMeaningItem(word, persistedInput, explanationLanguage, preparedTokenKind, aliases, now);
        var semanticIdentity = SemanticMeaningIdentityPolicy.Compute(
            candidateItem,
            vocabularyIdentity,
            targetFacts.TopicOrDomain);

        var exactManualMatch = isManualInput
            ? TryFindExactManualMeaning(
                connection,
                existingSenses,
                candidateItem,
                semanticIdentity,
                vocabularyIdentity)
            : null;

        int senseId;
        int meaningId;
        if (exactManualMatch is not null)
        {
            senseId = exactManualMatch.SenseId;
            meaningId = exactManualMatch.MeaningId;
        }
        else
        {
            var (matchedSense, _) = PreparationSenseClassifier.ClassifyAgainstExisting(
                word.Language, vocabularyIdentityKey, targetFacts, existingSenses);

            // §8 PartOfSpeech precedence: explicit input (already normalized/validated above), else the
            // selected provider Meaning, else empty.
            var partOfSpeech = normalizedExplicitPartOfSpeech.Length > 0
                ? normalizedExplicitPartOfSpeech
                : isManualInput
                    ? string.Empty
                    : PreparationMetadataPolicy.NormalizePartOfSpeech(targetMeaning!.PartOfSpeech);
            senseId = matchedSense?.Id ?? InsertSense(connection, word.Id, targetFacts, partOfSpeech, now);
            meaningId = TryFindExactDuplicateMeaning(connection, senseId, candidateItem, semanticIdentity)
                ?? InsertMeaning(connection, word.Id, senseId, candidateItem, preparedTokenKind, now);
        }

        Trip(PreparationSchema8Checkpoints.AfterSenseInsert);

        BackfillDefaultMeaningIfMissing(connection, senseId, meaningId);

        Trip(PreparationSchema8Checkpoints.AfterMeaningInsert);

        // §4: the candidate's frozen evidence is never recomputed or replaced here — only linked.
        InsertNewContextSnapshots(connection, meaningId, senseId, word.Id, contextData, now);

        Trip(PreparationSchema8Checkpoints.AfterContextLink);

        var newDirections = EnsureCardsForDirections(
            connection, word.Id, senseId, meaningId, cardDirectionPreference, now);

        Trip(PreparationSchema8Checkpoints.AfterCardInsert);

        // KF-MEANING-001 Slice 4: initialize answer variants and direction-specific assignments for exactly the
        // directions this acceptance just created. Runs inside the same transaction and adds no checkpoint, so
        // every existing preparation checkpoint keeps its documented meaning and ordering.
        EnsureAnswerAssignmentsForNewDirections(connection, senseId, newDirections, now);

        if (!isManualInput)
        {
            resolved!.Add(targetIndex!.Value);

            // KF-MEANING-002: candidate completion no longer requires every provider meaning to be resolved.
            // Unselected provider meanings are suggestions only — they are never inspected, matched, auto-
            // linked, or required to reach any resolution state.

            // §4/§5: FrozenEvidence is carried forward byte-identical — only ResolvedProviderMeaningIndexes changes.
            candidate.ResultJson = PreparationCandidatePayloadCodec.Write(envelope with
            {
                ResolvedProviderMeaningIndexes = resolved.ToArray()
            });
        }

        // The manual path intentionally leaves Result and ResolvedProviderMeaningIndexes byte-for-byte as
        // frozen. It neither creates provider data nor claims to resolve an index that did not exist.
        Trip(PreparationSchema8Checkpoints.AfterResolvedIndexPersist);

        // KF-MEANING-002: accepting the one explicitly selected meaning completes the candidate. There is
        // no partial-acceptance state — every successful AcceptSchema8 call ends here.
        Trip(PreparationSchema8Checkpoints.BeforeCandidateCompletion);
        if (!isManualInput)
        {
            Trip(PreparationSchema8Checkpoints.BeforeAutomaticCandidateCompletion);
        }
        word.PreparationState = PreparationState.Prepared;
        word.UpdatedAt = now;
        connection.Update(word);
        connection.Update(candidate);
        CompleteCandidate(connection, session, candidate, PreparationCandidateStatus.Prepared, now);

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

    private static SenseDiscriminatorFacts ResolveManualDiscriminatorFacts(
        WordEntity word,
        string normalizedTopicOrDomain,
        PreparedMeaningInput input,
        string explanationLanguage) => new(
        word.Language,
        explanationLanguage,
        string.Empty,
        normalizedTopicOrDomain,
        input.GrammaticalRelationship?.Trim() ?? string.Empty,
        input.AcronymExpansion?.Trim() ?? string.Empty);

    private static CandidateLookupContext ResolveCandidateLookupContext(
        SQLiteConnection connection,
        WordEntity word,
        IReadOnlyList<ContextData> contextData)
    {
        var firstContext = contextData.FirstOrDefault();
        var document = firstContext is null
            ? null
            : connection.Find<DocumentEntity>(firstContext.DocumentId);
        if (document is null)
        {
            return new CandidateLookupContext(LexicalLookupMode.Definition, null, word.Language);
        }

        var (lookupMode, targetLanguage) = ResolveLookupSettings(document);
        return new CandidateLookupContext(
            lookupMode,
            targetLanguage,
            targetLanguage ?? document.TextLanguage);
    }

    private static PreparedMeaningInput NormalizeManualInput(
        PreparedMeaningInput input,
        LexicalLookupMode lookupMode) => lookupMode switch
        {
            LexicalLookupMode.Definition => input with
            {
                Translation = null,
                ManualInputMode = lookupMode
            },
            LexicalLookupMode.Translation => input with
            {
                Definition = string.Empty,
                ManualInputMode = lookupMode
            },
            LexicalLookupMode.DefinitionAndTranslation => input with
            {
                ManualInputMode = lookupMode
            },
            _ => throw new ArgumentOutOfRangeException(nameof(lookupMode))
        };

    private static PreparedMeaningInput WithoutProviderProvenance(
        PreparedMeaningInput input,
        string canonicalTerm) => input with
    {
        SelectedMeaningId = null,
        DictionaryExample = null,
        ProviderName = string.Empty,
        SourceProject = string.Empty,
        SourcePageTitle = string.Empty,
        SourceRevisionId = null,
        Attribution = string.Empty,
        CanonicalLearningTerm = canonicalTerm
    };

    private static ManualMeaningMatch? TryFindExactManualMeaning(
        SQLiteConnection connection,
        IReadOnlyList<SenseRow> existingSenses,
        BackupPreparedItem candidateItem,
        SemanticMeaningIdentity candidateSemanticIdentity,
        VocabularyIdentity vocabularyIdentity)
    {
        var candidateVariant = ExactMeaningVariantIdentityPolicy.Compute(
            candidateItem,
            candidateSemanticIdentity);
        foreach (var sense in existingSenses)
        {
            var existingRows = connection.Query<LegacyMeaningRow>(
                "SELECT * FROM Meanings WHERE SenseId = ? ORDER BY Id",
                sense.Id);
            foreach (var row in existingRows)
            {
                if (!IsManualMeaning(row))
                {
                    continue;
                }

                var existingItem = Schema8SemanticUpgradePolicy.BuildPreparedItem(row);
                var existingSemanticIdentity = SemanticMeaningIdentityPolicy.Compute(
                    existingItem,
                    vocabularyIdentity,
                    sense.TopicOrDomain);
                if (existingSemanticIdentity != candidateSemanticIdentity)
                {
                    continue;
                }

                var existingVariant = ExactMeaningVariantIdentityPolicy.Compute(
                    existingItem,
                    existingSemanticIdentity);
                if (existingVariant == candidateVariant)
                {
                    return new ManualMeaningMatch(sense.Id, row.Id);
                }
            }
        }

        return null;
    }

    private static bool IsManualMeaning(LegacyMeaningRow meaning) =>
        string.IsNullOrWhiteSpace(meaning.SelectedMeaningId)
        && string.IsNullOrWhiteSpace(meaning.Source)
        && string.IsNullOrWhiteSpace(meaning.SourceProject)
        && string.IsNullOrWhiteSpace(meaning.SourcePageTitle)
        && meaning.SourceRevisionId is null
        && string.IsNullOrWhiteSpace(meaning.Attribution);

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

        // KF-MEANING-002 context-integrity fail-safe: never persist a ContextSnapshot whose target text at
        // its own recorded coordinates is not a surface form already registered for this Word.
        var recognizedSurfaceForms = LoadRecognizedSurfaceForms(connection, wordId);
        var byKey = new Dictionary<KnownFirst.Core.Preparation.ContextEvidenceKey, ContextData>();
        var occurrenceContexts = Schema8EvidenceScanner.EnumerateOccurrenceContexts(connection, wordId);
        foreach (var context in occurrenceContexts)
        {
            if (!IsAttributableToCandidate(wordId, context.Text, context.TargetStart, context.TargetLength, recognizedSurfaceForms))
            {
                continue;
            }

            var key = KnownFirst.Core.Preparation.PreparationContextEvidencePolicy.CreateKey(
                context.DocumentId, context.Text, context.TargetStart, context.TargetLength);
            byKey.TryAdd(key, context);
        }

        // A derived component intentionally has no WordOccurrenceEntity rows: its surviving evidence's FK
        // ownership chain back to this exact WordId is itself the attribution proof, so the surface-form
        // heuristic above (built for occurrence-scanned text) does not apply here.
        if (occurrenceContexts.Count == 0)
        {
            foreach (var context in Schema8EvidenceScanner.EnumerateDerivedEvidenceContexts(connection, wordId))
            {
                var key = KnownFirst.Core.Preparation.PreparationContextEvidencePolicy.CreateKey(
                    context.DocumentId, context.Text, context.TargetStart, context.TargetLength);
                byKey.TryAdd(key, context);
            }
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

    /// <summary>KF-MEANING-002 context-integrity fail-safe: the sync counterpart of the display-path's
    /// surface-form registry lookup, used inside the same transaction as AcceptSchema8.</summary>
    private static HashSet<string> LoadRecognizedSurfaceForms(SQLiteConnection connection, int wordId) =>
        connection.Table<WordFormEntity>()
            .Where(form => form.WordId == wordId)
            .ToList()
            .Select(form => form.SurfaceForm)
            .ToHashSet(StringComparer.Ordinal);

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

    /// <summary>
    /// Inserts a card for every requested direction that does not already have one, and returns exactly the
    /// directions this call newly created. KF-MEANING-001 Slice 4 uses that list so answer-variant and
    /// assignment initialization touches only newly created directions and never alters an existing
    /// direction's assignment graph.
    /// </summary>
    private static List<CardDirection> EnsureCardsForDirections(
        SQLiteConnection connection,
        int wordId,
        int senseId,
        int meaningId,
        CardDirectionPreference cardDirectionPreference,
        DateTime now)
    {
        var created = new List<CardDirection>();
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
            created.Add(direction);
        }

        return created;
    }

    /// <summary>
    /// KF-MEANING-001 Slice 4: initializes answer variants and direction-specific assignments for exactly the
    /// card directions this acceptance created.
    /// <para>
    /// Each such direction receives exactly one deterministic primary assignment with
    /// <see cref="AnswerVariantRequirement.Required"/>, <c>IsPreferred = true</c> and
    /// <c>RequiredSinceUtc = CreatedAtUtc</c> (the existing transaction timestamp). Every remaining
    /// expression — the other Meanings' term/translation text and every accepted alias — becomes an
    /// <see cref="AnswerVariantRequirement.AcceptedOnly"/>, non-preferred assignment with a null boundary
    /// (Decision 12). Nothing is invented when a direction has no valid expression, aliases stay term-side and
    /// therefore <see cref="CardDirection.MeaningToTerm"/>-only, and <c>AnswerLanguage</c> is never used to
    /// infer or reject a direction.
    /// </para>
    /// </summary>
    private static void EnsureAnswerAssignmentsForNewDirections(
        SQLiteConnection connection,
        int senseId,
        IReadOnlyList<CardDirection> newDirections,
        DateTime now)
    {
        if (newDirections.Count == 0)
        {
            return;
        }

        // Ascending Meaning.Id is the single deterministic ordering key, exactly as in the dormant migration.
        var meanings = connection.Query<PreparationMeaningExpressionRow>(
            """
            SELECT Id, SourceLanguage, ExplanationLanguage, DisplayTerm, Translation, AcceptedAliasesJson
            FROM Meanings WHERE SenseId = ? ORDER BY Id
            """,
            senseId);
        if (meanings.Count == 0)
        {
            return;
        }

        foreach (var direction in newDirections)
        {
            var isTermSide = direction == CardDirection.MeaningToTerm;

            var primary = isTermSide
                ? meanings.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.DisplayTerm))
                : meanings.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Translation));
            if (primary is null)
            {
                // No valid primary expression: no variant, no assignment, no invented fallback text.
                continue;
            }

            var primaryVariantId = GetOrCreatePreparationAnswerVariant(
                connection, senseId,
                isTermSide ? primary.SourceLanguage : primary.ExplanationLanguage,
                isTermSide ? primary.DisplayTerm : primary.Translation,
                primary.Id, now);
            EnsurePreparationAssignment(
                connection, senseId, direction, primaryVariantId,
                AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: now, now);

            foreach (var meaning in meanings)
            {
                var alternativeText = isTermSide ? meaning.DisplayTerm : meaning.Translation;
                if (!string.IsNullOrWhiteSpace(alternativeText))
                {
                    var alternativeId = GetOrCreatePreparationAnswerVariant(
                        connection, senseId,
                        isTermSide ? meaning.SourceLanguage : meaning.ExplanationLanguage,
                        alternativeText, meaning.Id, now);
                    EnsurePreparationAssignment(
                        connection, senseId, direction, alternativeId,
                        AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null, now);
                }

                if (!isTermSide)
                {
                    continue;
                }

                foreach (var alias in Schema8SemanticUpgradePolicy
                             .DeserializeAliases(meaning.AcceptedAliasesJson)
                             .Distinct(StringComparer.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    var aliasVariantId = GetOrCreatePreparationAnswerVariant(
                        connection, senseId, meaning.SourceLanguage, alias, meaning.Id, now);
                    EnsurePreparationAssignment(
                        connection, senseId, direction, aliasVariantId,
                        AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null, now);
                }
            }
        }
    }

    /// <summary>
    /// Deduplicates by the exact <c>(SenseId, AnswerLanguage, NormalizedText)</c> triple the table's own unique
    /// index enforces, and never overwrites an already-recorded <c>SourceMeaningId</c>.
    /// </summary>
    private static int GetOrCreatePreparationAnswerVariant(
        SQLiteConnection connection,
        int senseId,
        string answerLanguage,
        string displayText,
        int sourceMeaningId,
        DateTime now)
    {
        var normalized = CanonicalText.NormalizeOptional(displayText);
        var existingId = connection.ExecuteScalar<int?>(
            "SELECT Id FROM AnswerVariants WHERE SenseId = ? AND AnswerLanguage = ? AND NormalizedText = ?",
            senseId, answerLanguage, normalized);
        if (existingId is int found)
        {
            return found;
        }

        connection.Execute(
            """
            INSERT INTO AnswerVariants
                (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            Guid.NewGuid().ToString("N"), senseId, answerLanguage, displayText, normalized, sourceMeaningId, now, now);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Adds an assignment for the exact triple unless one already exists. Never downgrades, never duplicates,
    /// and therefore can never displace the primary Required assignment created first for that direction.
    /// Enforces invariant I1 before writing.
    /// </summary>
    private static void EnsurePreparationAssignment(
        SQLiteConnection connection,
        int senseId,
        CardDirection direction,
        int answerVariantId,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTime? requiredSinceUtc,
        DateTime now)
    {
        var exists = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments
            WHERE SenseId = ? AND CardDirection = ? AND AnswerVariantId = ?
            """,
            senseId, (int)direction, answerVariantId) > 0;
        if (exists)
        {
            return;
        }

        if ((requirement == AnswerVariantRequirement.Required) != requiredSinceUtc.HasValue)
        {
            throw new InvalidOperationException(
                "Assignment violates 'Requirement = Required if and only if RequiredSinceUtc is not null'.");
        }

        connection.Execute(
            """
            INSERT INTO SenseAnswerVariantAssignments
                (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            Guid.NewGuid().ToString("N"), senseId, (int)direction, answerVariantId, (int)requirement, isPreferred,
            requiredSinceUtc, now, now);
    }

    private readonly record struct CandidateLookupContext(
        LexicalLookupMode LookupMode,
        string? TargetLanguage,
        string ExplanationLanguage);

    private sealed record ManualMeaningMatch(int SenseId, int MeaningId);

    private sealed class FingerprintRow
    {
        public string NormalizedFingerprint { get; set; } = string.Empty;
    }

    /// <summary>Meaning expressions used to derive Slice-4 answer variants during preparation.</summary>
    private sealed class PreparationMeaningExpressionRow
    {
        public int Id { get; set; }
        public string SourceLanguage { get; set; } = string.Empty;
        public string ExplanationLanguage { get; set; } = string.Empty;
        public string DisplayTerm { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public string AcceptedAliasesJson { get; set; } = "[]";
    }

}
