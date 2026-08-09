using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Resolves every stable merge identity the recomputed <see cref="MergePreflightPlan"/> can reference back
/// to the current target database's own real (SQLite row) integer id (KF-MEANING-001 Slice 8). Built once
/// per write, directly from the raw <see cref="Schema8BackupSnapshot"/> — never from a
/// <see cref="BackupModelMapperV2"/>-produced payload, whose ids are synthetic/positional and therefore
/// cannot be traced back to a real row.
///
/// <para>Every identity here is computed by calling the exact same identity-policy classes the planner
/// uses (<see cref="VocabularyMergeIdentityPolicy"/>, <see cref="SourceMaterialIdentityPolicy"/>,
/// <see cref="SemanticMeaningIdentityPolicy"/>, <see cref="ExactMeaningVariantIdentityPolicy"/>,
/// <see cref="AnswerVariantIdentityPolicy"/>, <see cref="FutureCardIdentityPolicy"/>,
/// <see cref="ReviewWorkflowIdentityPolicy"/>, <see cref="PreparationWorkflowIdentityPolicy"/>, and
/// <see cref="LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(DateTime, DateTime?, IReadOnlyList{ValueTuple{FutureCardIdentity, BackupReviewRating?}})"/>) — no identity computation is
/// duplicated here, only the raw-row field plumbing needed to call those policies against entities that
/// carry a real int id instead of an archive-local string id.</para>
/// </summary>
internal sealed class MergeWriterTargetIndex
{
    public IReadOnlyDictionary<VocabularyIdentity, int> WordIdByIdentity { get; }
    public IReadOnlyDictionary<int, VocabularyIdentity> WordIdentityByWordId { get; }
    public IReadOnlyDictionary<SourceMaterialIdentity, int> DocumentIdByIdentity { get; }
    public IReadOnlyDictionary<int, SourceMaterialIdentity> DocumentIdentityByDocumentId { get; }
    public IReadOnlyDictionary<string, int> SentenceIdByIdentity { get; }
    public IReadOnlyDictionary<SemanticMeaningIdentity, int> SenseIdByIdentity { get; }
    public IReadOnlyDictionary<int, SemanticMeaningIdentity> SenseIdentityBySenseId { get; }
    public IReadOnlyDictionary<ExactMeaningVariantIdentity, int> MeaningIdByIdentity { get; }
    public IReadOnlyDictionary<AnswerVariantIdentity, int> AnswerVariantIdByIdentity { get; }
    public IReadOnlyDictionary<FutureCardIdentity, int> CardIdByIdentity { get; }
    public IReadOnlyDictionary<ReviewSessionIdentity, int> ReviewSessionIdByIdentity { get; }
    public IReadOnlyDictionary<PreparationSessionIdentity, int> PreparationSessionIdByIdentity { get; }
    public IReadOnlyDictionary<string, int> LearningSessionIdByIdentity { get; }

    private MergeWriterTargetIndex(
        IReadOnlyDictionary<VocabularyIdentity, int> wordIdByIdentity,
        IReadOnlyDictionary<int, VocabularyIdentity> wordIdentityByWordId,
        IReadOnlyDictionary<SourceMaterialIdentity, int> documentIdByIdentity,
        IReadOnlyDictionary<int, SourceMaterialIdentity> documentIdentityByDocumentId,
        IReadOnlyDictionary<string, int> sentenceIdByIdentity,
        IReadOnlyDictionary<SemanticMeaningIdentity, int> senseIdByIdentity,
        IReadOnlyDictionary<int, SemanticMeaningIdentity> senseIdentityBySenseId,
        IReadOnlyDictionary<ExactMeaningVariantIdentity, int> meaningIdByIdentity,
        IReadOnlyDictionary<AnswerVariantIdentity, int> answerVariantIdByIdentity,
        IReadOnlyDictionary<FutureCardIdentity, int> cardIdByIdentity,
        IReadOnlyDictionary<ReviewSessionIdentity, int> reviewSessionIdByIdentity,
        IReadOnlyDictionary<PreparationSessionIdentity, int> preparationSessionIdByIdentity,
        IReadOnlyDictionary<string, int> learningSessionIdByIdentity)
    {
        WordIdByIdentity = wordIdByIdentity;
        WordIdentityByWordId = wordIdentityByWordId;
        DocumentIdByIdentity = documentIdByIdentity;
        DocumentIdentityByDocumentId = documentIdentityByDocumentId;
        SentenceIdByIdentity = sentenceIdByIdentity;
        SenseIdByIdentity = senseIdByIdentity;
        SenseIdentityBySenseId = senseIdentityBySenseId;
        MeaningIdByIdentity = meaningIdByIdentity;
        AnswerVariantIdByIdentity = answerVariantIdByIdentity;
        CardIdByIdentity = cardIdByIdentity;
        ReviewSessionIdByIdentity = reviewSessionIdByIdentity;
        PreparationSessionIdByIdentity = preparationSessionIdByIdentity;
        LearningSessionIdByIdentity = learningSessionIdByIdentity;
    }

    public static MergeWriterTargetIndex Build(Schema8BackupSnapshot snapshot)
    {
        var wordIdByIdentity = new Dictionary<VocabularyIdentity, int>();
        var wordIdentityByWordId = new Dictionary<int, VocabularyIdentity>();
        foreach (var word in snapshot.Words)
        {
            var identity = VocabularyMergeIdentityPolicy.Compute(word.Language, word.NormalizedTerm);
            wordIdByIdentity[identity] = word.Id;
            wordIdentityByWordId[word.Id] = identity;
        }

        var documentIdByIdentity = new Dictionary<SourceMaterialIdentity, int>();
        var documentIdentityByDocumentId = new Dictionary<int, SourceMaterialIdentity>();
        foreach (var document in snapshot.Documents)
        {
            var identity = Schema9ReviewSessionRowIdentities.ComputeDocumentIdentity(document);
            documentIdByIdentity[identity] = document.Id;
            documentIdentityByDocumentId[document.Id] = identity;
        }

        var sentenceIdByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sentence in snapshot.SentenceSpans)
        {
            var docIdentity = documentIdentityByDocumentId[sentence.DocumentId];
            var identity = MergePreflightPlanner.ComputeSentenceRangeIdentity(docIdentity, ToBackupSentenceRange(sentence));
            sentenceIdByIdentity[identity] = sentence.Id;
        }

        var senseIdByIdentity = new Dictionary<SemanticMeaningIdentity, int>();
        var senseIdentityBySenseId = new Dictionary<int, SemanticMeaningIdentity>();
        foreach (var sense in snapshot.Senses)
        {
            var vocabIdentity = wordIdentityByWordId[sense.WordId];
            var identity = SemanticMeaningIdentityPolicy.Compute(ToBackupSense(sense), vocabIdentity);
            senseIdByIdentity[identity] = sense.Id;
            senseIdentityBySenseId[sense.Id] = identity;
        }

        var meaningIdByIdentity = new Dictionary<ExactMeaningVariantIdentity, int>();
        foreach (var meaning in snapshot.Meanings)
        {
            if (meaning.SenseId is not { } senseId || !senseIdentityBySenseId.TryGetValue(senseId, out var senseIdentity))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var identity = ExactMeaningVariantIdentityPolicy.Compute(ToBackupPreparedItemV2(meaning), senseIdentity);
            meaningIdByIdentity[identity] = meaning.Id;
        }

        var answerVariantIdByIdentity = new Dictionary<AnswerVariantIdentity, int>();
        foreach (var variant in snapshot.AnswerVariants)
        {
            var senseIdentity = senseIdentityBySenseId[variant.SenseId];
            var identity = AnswerVariantIdentityPolicy.Compute(senseIdentity, variant.NormalizedText, variant.AnswerLanguage);
            answerVariantIdByIdentity[identity] = variant.Id;
        }

        var cardIdByIdentity = new Dictionary<FutureCardIdentity, int>();
        var cardIdentityByCardId = new Dictionary<int, FutureCardIdentity>();
        foreach (var card in snapshot.LearningCards)
        {
            if (card.SenseId is not { } cardSenseId || !senseIdentityBySenseId.TryGetValue(cardSenseId, out var senseIdentity))
            {
                throw new BackupFormatException(BackupErrorCodes.MissingReference);
            }

            var identity = FutureCardIdentityPolicy.Compute(senseIdentity, BackupEnumMappings.ToBackup(card.Direction));
            cardIdByIdentity[identity] = card.Id;
            cardIdentityByCardId[card.Id] = identity;
        }

        // Same full-history v2 identity the Schema-9 planner records, computed from raw rows through the
        // shared, caller-neutral Schema9ReviewSessionRowIdentities plumbing that BackupModelMapperV2's
        // canonical ReviewSessions ordering also uses — one definition of the completed-review identity,
        // including the five retained outcome counters, which after ordinary completion are the session's
        // only surviving outcome summary. Reference resolution stays here, so a dangling document or
        // vocabulary reference still surfaces exactly as it did before.
        var reviewSessionIdByIdentity = new Dictionary<ReviewSessionIdentity, int>();
        foreach (var session in snapshot.ReviewSessions)
        {
            var docIdentity = documentIdentityByDocumentId[session.DocumentId];
            var candidates = snapshot.ReviewCandidates
                .Where(candidate => candidate.SessionId == session.Id)
                .Select(candidate => Schema9ReviewSessionRowIdentities.BuildCandidateContent(
                    candidate, wordIdentityByWordId[candidate.WordId]))
                .ToList();

            var result = Schema9ReviewSessionRowIdentities.ComputeSessionIdentity(session, docIdentity, candidates);
            if (result.HasDuplicateCandidateVocabularyIdentity)
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }

            if (!reviewSessionIdByIdentity.TryAdd(result.Identity, session.Id))
            {
                throw new BackupFormatException(BackupErrorCodes.DuplicateId);
            }
        }

        var preparationSessionIdByIdentity = new Dictionary<PreparationSessionIdentity, int>();
        foreach (var session in snapshot.PreparationSessions)
        {
            var orderedVocabIdentities = snapshot.PreparationCandidates
                .Where(item => item.SessionId == session.Id)
                .OrderBy(item => item.Order)
                .Select(item => wordIdentityByWordId[item.WordId])
                .ToList();
            var identity = PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(
                session.StartedAtUtc, session.CompletedAtUtc, orderedVocabIdentities);
            preparationSessionIdByIdentity[identity] = session.Id;
        }

        // Mirrors the archive-side rule in LearningWorkflowIdentityPolicy: a Schema-10 target row's own
        // persistent StableId is its logical identity, and only a target still without one falls back to
        // the Schema-8 content fingerprint. Both sides must apply the same rule, or an archive carrying
        // identities could never match the very rows it came from.
        var learningSessionIdByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var session in snapshot.LearningSessions)
        {
            var persistedStableId = snapshot.LearningSessionStableIds?.GetValueOrDefault(session.Id);
            string identity;
            if (Data.Schema10.LearningWorkflowStableId.IsValid(persistedStableId))
            {
                identity = persistedStableId!;
            }
            else
            {
                var orderedQueueItems = snapshot.LearningSessionCards
                    .Where(item => item.SessionId == session.Id)
                    .OrderBy(item => item.QueueOrder)
                    .Select(item => (
                        cardIdentityByCardId[item.CardId],
                        item.Rating is null ? (BackupReviewRating?)null : BackupEnumMappings.ToBackup(item.Rating.Value)))
                    .ToList();
                identity = LearningWorkflowIdentityPolicy.ComputeSchema8SessionIdentity(
                    session.StartedAtUtc, session.CompletedAtUtc, orderedQueueItems);
            }

            learningSessionIdByIdentity[identity] = session.Id;
        }

        return new MergeWriterTargetIndex(
            wordIdByIdentity, wordIdentityByWordId, documentIdByIdentity, documentIdentityByDocumentId,
            sentenceIdByIdentity, senseIdByIdentity, senseIdentityBySenseId, meaningIdByIdentity,
            answerVariantIdByIdentity, cardIdByIdentity, reviewSessionIdByIdentity,
            preparationSessionIdByIdentity, learningSessionIdByIdentity);
    }

    private static BackupSentenceRange ToBackupSentenceRange(SentenceSpanEntity sentence) => new(
        sentence.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        sentence.Order,
        sentence.StartPosition,
        sentence.Length);

    /// <summary>Single definition, shared with the Schema-10 identity bootstrap — see
    /// <see cref="Schema8RowSemanticIdentities.ToBackupSense"/> for why it must not be duplicated.</summary>
    private static BackupSense ToBackupSense(SenseRow sense) => Schema8RowSemanticIdentities.ToBackupSense(sense);

    private static BackupPreparedItemV2 ToBackupPreparedItemV2(Schema8MeaningRow meaning) => new(
        meaning.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string.Empty,
        meaning.StableId,
        string.Empty,
        meaning.SourceLanguage,
        meaning.ExplanationLanguage,
        meaning.DisplayTerm,
        EmptyToNull(meaning.EncounteredSurfaceForm),
        EmptyToNull(meaning.GrammaticalRelationship),
        BackupEnumMappings.ToBackup(meaning.TokenKind),
        EmptyToNull(meaning.SelectedMeaningId),
        EmptyToNull(meaning.AcronymExpansion),
        EmptyToNull(meaning.Translation),
        EmptyToNull(meaning.Definition),
        EmptyToNull(meaning.DictionaryExample),
        EmptyToNull(meaning.AdditionalNote),
        EmptyToNull(meaning.TranslationOrDefinition),
        DeserializeAliases(meaning.AcceptedAliasesJson),
        meaning.ConfirmedByUser,
        new BackupSourceReference(meaning.Source, meaning.SourceProject, meaning.SourcePageTitle, meaning.SourceRevisionId, meaning.Attribution),
        EnsureUtc(meaning.CreatedAt),
        EnsureUtc(meaning.UpdatedAt),
        EnsureUtc(meaning.PreparedAt),
        []);

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? EnsureUtc(DateTime? value) => value.HasValue ? EnsureUtc(value.Value) : null;

    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static IReadOnlyList<string> DeserializeAliases(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var result = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.GetString() is { } text)
                {
                    result.Add(text);
                }
            }

            return result;
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
