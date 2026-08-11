using System.Globalization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Tentative per-row facts about one archive <see cref="BackupPreparedItem"/>, fixed once computed and
/// reused unmodified by the later AnswerVariant pass. Unlike the previous revision, no later pass ever
/// overrides <see cref="Classification"/> — a row's classification is now fully determined by this pass
/// alone (see "Correct SemanticMeaningIdentity" in the architecture document for why the earlier
/// card-driven override no longer exists).
/// </summary>
internal sealed record MeaningRowState(MergeEntityClassification Classification, string ReasonCode, bool WordIsNew, bool SemanticIsNew);

/// <summary>
/// Pure, database-independent read-only merge preflight planner (KF-BACKUP-002 Slice 3, corrected for the
/// approved Word -> SemanticMeaning -> AnswerVariant model — see
/// <c>docs/architecture/backup-merge-v1-design.md</c> §16/§17 and KF-MEANING-001 in <c>docs/BACKLOG.md</c>).
/// Given a target and an archive <see cref="BackupPayload"/> (both already produced by the same
/// <see cref="BackupModelMapper"/>) plus the archive's own <see cref="BackupManifest"/>, computes the
/// complete, deterministic <see cref="MergePreflightPlan"/>. Has no SQLite, filesystem, network,
/// environment, current-time, random, Preferences, or MAUI dependency, and opens no write transaction.
///
/// <para><b>LearningCard matching is now semantic</b>: two cards match when they share a
/// <see cref="FutureCardIdentity"/> (<see cref="SemanticMeaningIdentity"/> + Direction), not merely a
/// physical (VocabularyIdentity, Direction) slot. The physical identity (<see cref="LearningCardIdentityPolicy"/>)
/// is retained only to detect when two distinct FutureCardIdentity values collide on the one physical
/// slot the live schema currently allows — a blocking prerequisite, never a silent non-blocking
/// downgrade.</para>
///
/// <para>Every entity kind has an explicit, stable, parent-resolved identity; no entity is ever
/// classified in lockstep with its parent's own classification. Workflow children
/// (VocabularyReviewItem, PreparationItem, LearningQueueItem) and the VocabularyReviewWorkflow session
/// itself now compare complete historical content, not identity alone, before concluding "exact
/// duplicate".</para>
/// </summary>
public static class MergePreflightPlanner
{
    public static MergePreflightPlan CreatePlan(BackupPayload target, BackupPayload archive, BackupManifest archiveManifest)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(archiveManifest);

        var counts = CreateEmptyCounts();
        var actions = new List<MergePlanAction>();
        var derivedAnswerVariantPlans = new List<DerivedAnswerVariantPlan>();
        var warningCodes = new SortedSet<string>(StringComparer.Ordinal);
        var blockingPrerequisites = new SortedSet<string>(StringComparer.Ordinal);
        var knowledgeStateDecisions = new List<KnowledgeStateConflictDecision>();
        var workflowStatusDecisions = new List<WorkflowStatusConflictDecision>();
        var semanticMeaningGroupingDecisions = new List<SemanticMeaningGroupingDecision>();

        void Record(MergeEntityKind kind, string stableIdentity, string archiveLocalId, MergeEntityClassification classification, string reason, DecisionId? decisionId = null)
        {
            counts[kind] = counts[kind].Increment(classification);
            actions.Add(new MergePlanAction(kind, stableIdentity, archiveLocalId, classification, reason, decisionId));
        }

        static DecisionId MakeDecisionId(string domain, string keyValue) =>
            new(new CanonicalFingerprintBuilder(domain).WriteString(keyValue).ComputeSha256Hex());

        // ---- Base identity maps (archive-local id -> stable identity), fail-closed on duplicate ids ----
        var targetVocabByLocalId = BuildIdentityMap(target.Vocabulary, v => v.Id, VocabularyMergeIdentityPolicy.Compute, "target vocabulary");
        var archiveVocabByLocalId = BuildIdentityMap(archive.Vocabulary, v => v.Id, VocabularyMergeIdentityPolicy.Compute, "archive vocabulary");
        var targetVocabIdentitySet = new HashSet<VocabularyIdentity>(targetVocabByLocalId.Values);
        var targetVocabularyByIdentity = ToUniqueDictionary(target.Vocabulary, v => targetVocabByLocalId[v.Id], "target vocabulary identity");

        var targetDocByLocalId = BuildIdentityMap(target.SourceMaterials, d => d.Id, SourceMaterialIdentityPolicy.Compute, "target source material");
        var archiveDocByLocalId = BuildIdentityMap(archive.SourceMaterials, d => d.Id, SourceMaterialIdentityPolicy.Compute, "archive source material");
        var targetDocIdentitySet = new HashSet<SourceMaterialIdentity>(targetDocByLocalId.Values);

        // SemanticMeaning / ExactMeaningVariant maps. Topic/domain is always empty: the current schema
        // and archive format never persist it (MergePreflightSchemaGapCodes.TopicPersistenceRequired).
        var targetSemanticByLocalId = BuildIdentityMap(target.PreparedLearning, m => m.Id, m => SemanticMeaningIdentityPolicy.Compute(m, targetVocabByLocalId), "target prepared item");
        var archiveSemanticByLocalId = BuildIdentityMap(archive.PreparedLearning, m => m.Id, m => SemanticMeaningIdentityPolicy.Compute(m, archiveVocabByLocalId), "archive prepared item");
        var targetSemanticIdentitySet = new HashSet<SemanticMeaningIdentity>(targetSemanticByLocalId.Values);

        var targetExactVariantByLocalId = BuildIdentityMap(target.PreparedLearning, m => m.Id, m => ExactMeaningVariantIdentityPolicy.Compute(m, targetSemanticByLocalId[m.Id]), "target prepared item exact variant");
        var archiveExactVariantByLocalId = BuildIdentityMap(archive.PreparedLearning, m => m.Id, m => ExactMeaningVariantIdentityPolicy.Compute(m, archiveSemanticByLocalId[m.Id]), "archive prepared item exact variant");
        var targetExactVariantIdentitySet = new HashSet<ExactMeaningVariantIdentity>(targetExactVariantByLocalId.Values);

        var targetPreparedByLocalId = ToUniqueDictionary(target.PreparedLearning, m => m.Id, "target prepared item id");
        var archivePreparedByLocalId = ToUniqueDictionary(archive.PreparedLearning, m => m.Id, "archive prepared item id");

        var targetRepresentativeBySemanticIdentity = new Dictionary<SemanticMeaningIdentity, BackupPreparedItem>();
        foreach (var meaning in target.PreparedLearning.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            targetRepresentativeBySemanticIdentity.TryAdd(targetSemanticByLocalId[meaning.Id], meaning);
        }

        var targetAnswerVariantIdentitySet = new HashSet<AnswerVariantIdentity>();
        foreach (var meaning in target.PreparedLearning)
        {
            var semanticIdentity = targetSemanticByLocalId[meaning.Id];
            AddAnswerVariantIdentities(targetAnswerVariantIdentitySet, semanticIdentity, meaning);
        }

        // Physical (schema-enforced) identity: (VocabularyIdentity, Direction). Used only to detect a
        // FutureCardIdentity collision on today's one-card-per-slot schema, never to match cards.
        var targetPhysicalCardIdentityByLocalId = BuildIdentityMap(target.Learning.Cards, c => c.Id, c => LearningCardIdentityPolicy.ComputeMatchIdentity(c, targetVocabByLocalId), "target learning card physical identity");
        var archivePhysicalCardIdentityByLocalId = BuildIdentityMap(archive.Learning.Cards, c => c.Id, c => LearningCardIdentityPolicy.ComputeMatchIdentity(c, archiveVocabByLocalId), "archive learning card physical identity");
        var targetPhysicalCardIdentitySet = new HashSet<LearningCardMatchIdentity>(targetPhysicalCardIdentityByLocalId.Values);

        // Semantic matching identity: (SemanticMeaningIdentity, Direction). This is what the planner
        // actually matches LearningCards by.
        var targetFutureCardIdByLocalId = new Dictionary<string, FutureCardIdentity>();
        foreach (var card in target.Learning.Cards)
        {
            var semanticIdentity = Resolve(targetSemanticByLocalId, card.PreparedItemId, "target learning card semantic meaning");
            targetFutureCardIdByLocalId[card.Id] = FutureCardIdentityPolicy.Compute(semanticIdentity, card.Direction);
        }

        var archiveFutureCardIdByLocalId = new Dictionary<string, FutureCardIdentity>();
        foreach (var card in archive.Learning.Cards)
        {
            var semanticIdentity = Resolve(archiveSemanticByLocalId, card.PreparedItemId, "archive learning card semantic meaning");
            archiveFutureCardIdByLocalId[card.Id] = FutureCardIdentityPolicy.Compute(semanticIdentity, card.Direction);
        }

        var targetCardsByFutureCardIdentity = ToUniqueDictionary(target.Learning.Cards, c => targetFutureCardIdByLocalId[c.Id], "target learning card future-card identity");

        // Structural warning: topic/domain disambiguation cannot be verified because it is never
        // persisted today. Independent of any scenario below; never inferred from Definition/Translation text.
        if (archive.PreparedLearning.Count > 0)
        {
            warningCodes.Add(MergePreflightSchemaGapCodes.TopicPersistenceRequired);
        }

        // ================= Vocabulary (design §5.1/§5.2) =================
        var targetFormOccurrenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var targetLegacySummariesByWord = new Dictionary<string, HashSet<BackupLegacyReviewSummary>>(StringComparer.Ordinal);
        foreach (var word in target.Vocabulary)
        {
            var vocabIdentity = targetVocabByLocalId[word.Id];
            foreach (var form in word.EncounteredForms)
            {
                targetFormOccurrenceCounts[ComputeEncounteredFormIdentity(vocabIdentity, form.SurfaceForm)] = form.OccurrenceCount;
            }

            if (word.LegacyReviewSummaries.Count > 0)
            {
                targetLegacySummariesByWord[vocabIdentity.Value] = [.. word.LegacyReviewSummaries];
            }
        }

        foreach (var archiveWord in archive.Vocabulary)
        {
            var identity = archiveVocabByLocalId[archiveWord.Id];
            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;

            if (!targetVocabularyByIdentity.TryGetValue(identity, out var targetWord))
            {
                classification = MergeEntityClassification.New;
                reason = "vocabulary-new";
            }
            else
            {
                var knowledgeResult = KnowledgeStateConflictPolicy.Resolve(targetWord.KnowledgeState, archiveWord.KnowledgeState);
                var preparationResult = PreparationStateConflictPolicy.Resolve(targetWord.PreparationState, archiveWord.PreparationState);

                if (knowledgeResult.Classification == MergeConflictClassification.UnresolvedKeepTargetWithWarning)
                {
                    classification = MergeEntityClassification.UnresolvedConflict;
                    reason = knowledgeResult.ReasonCode;
                    warningCodes.Add(knowledgeResult.ReasonCode);
                    decisionId = MakeDecisionId("KnownFirst.Merge.Decision.KnowledgeState.v1", identity.Value);
                    knowledgeStateDecisions.Add(new KnowledgeStateConflictDecision(
                        decisionId.Value, identity, archiveWord.Id, targetWord.KnowledgeState, archiveWord.KnowledgeState, knowledgeResult.ReasonCode));
                }
                else
                {
                    var advanced = knowledgeResult.ResolvedState != targetWord.KnowledgeState
                        || preparationResult.ResolvedState != targetWord.PreparationState;
                    classification = advanced ? MergeEntityClassification.Enriched : MergeEntityClassification.ExactDuplicateSkipped;
                    reason = advanced ? "vocabulary-progress-advanced" : "vocabulary-progress-equal";
                }
            }

            var wordIsNewForChildren = classification == MergeEntityClassification.New;
            Record(MergeEntityKind.Vocabulary, identity.Value, archiveWord.Id, classification, reason, decisionId);

            foreach (var form in archiveWord.EncounteredForms)
            {
                var formIdentity = ComputeEncounteredFormIdentity(identity, form.SurfaceForm);
                MergeEntityClassification formClassification;
                string formReason;
                if (wordIsNewForChildren || !targetFormOccurrenceCounts.TryGetValue(formIdentity, out var targetCount))
                {
                    formClassification = MergeEntityClassification.New;
                    formReason = "encountered-form-new";
                }
                else
                {
                    formClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    formReason = targetCount == form.OccurrenceCount
                        ? "encountered-form-exact-duplicate"
                        : "encountered-form-count-recompute-required";
                }

                Record(MergeEntityKind.EncounteredForm, formIdentity, archiveWord.Id + ":" + form.SurfaceForm, formClassification, formReason);
            }

            if (archiveWord.LegacyReviewSummaries.Count > 0)
            {
                var summaryIdentity = ComputeLegacyReviewSummaryIdentity(identity);
                var hasTargetSummaries = targetLegacySummariesByWord.TryGetValue(identity.Value, out var targetSummarySet);
                for (var i = 0; i < archiveWord.LegacyReviewSummaries.Count; i++)
                {
                    var summary = archiveWord.LegacyReviewSummaries[i];
                    MergeEntityClassification summaryClassification;
                    string summaryReason;
                    if (wordIsNewForChildren)
                    {
                        summaryClassification = MergeEntityClassification.New;
                        summaryReason = "legacy-review-summary-new";
                    }
                    else if (hasTargetSummaries && targetSummarySet!.Contains(summary))
                    {
                        summaryClassification = MergeEntityClassification.ExactDuplicateSkipped;
                        summaryReason = "legacy-review-summary-exact-duplicate";
                    }
                    else
                    {
                        summaryClassification = MergeEntityClassification.Enriched;
                        summaryReason = "legacy-review-summary-recompute-required";
                    }

                    Record(
                        MergeEntityKind.LegacyReviewSummary,
                        summaryIdentity,
                        archiveWord.Id + ":" + i.ToString(CultureInfo.InvariantCulture),
                        summaryClassification,
                        summaryReason);
                }
            }
        }

        // ================= SourceMaterial + independent SentenceRange/Occurrence identities =================
        var targetSentenceIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        var targetSentenceIdentityByDocAndLocalId = new Dictionary<(string DocLocalId, string SentenceLocalId), string>();
        foreach (var doc in target.SourceMaterials)
        {
            var docIdentity = targetDocByLocalId[doc.Id];
            foreach (var sentence in doc.Sentences)
            {
                var sentenceIdentity = ComputeSentenceRangeIdentity(docIdentity, sentence);
                targetSentenceIdentitySet.Add(sentenceIdentity);
                targetSentenceIdentityByDocAndLocalId[(doc.Id, sentence.Id)] = sentenceIdentity;
            }
        }

        var targetOccurrenceIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in target.SourceMaterials)
        {
            var docIdentity = targetDocByLocalId[doc.Id];
            foreach (var occurrence in doc.Occurrences)
            {
                var vocabIdentity = Resolve(targetVocabByLocalId, occurrence.VocabularyId, "target occurrence vocabulary");
                var sentenceIdentity = Resolve(targetSentenceIdentityByDocAndLocalId, (doc.Id, occurrence.SentenceId), "target occurrence sentence");
                targetOccurrenceIdentitySet.Add(ComputeOccurrenceIdentity(docIdentity, sentenceIdentity, vocabIdentity, occurrence));
            }
        }

        var targetReviewSessionByIdentity = ToUniqueDictionary(
            target.Workflows.VocabularyReviews,
            s => ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(s, targetDocByLocalId),
            "target review session identity");

        foreach (var archiveDoc in archive.SourceMaterials)
        {
            var docIdentity = archiveDocByLocalId[archiveDoc.Id];
            var docClassification = targetDocIdentitySet.Contains(docIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
            var docReason = docClassification == MergeEntityClassification.New ? "source-material-new" : "source-material-exact-duplicate";
            Record(MergeEntityKind.SourceMaterial, docIdentity.Value, archiveDoc.Id, docClassification, docReason);

            var archiveDocSentenceIdentityByLocalId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var sentence in archiveDoc.Sentences)
            {
                var sentenceIdentity = ComputeSentenceRangeIdentity(docIdentity, sentence);
                archiveDocSentenceIdentityByLocalId[sentence.Id] = sentenceIdentity;
                var sentenceClassification = targetSentenceIdentitySet.Contains(sentenceIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
                var sentenceReason = sentenceClassification == MergeEntityClassification.New ? "sentence-range-new" : "sentence-range-exact-duplicate";
                Record(MergeEntityKind.SentenceRange, sentenceIdentity, sentence.Id, sentenceClassification, sentenceReason);
            }

            foreach (var occurrence in archiveDoc.Occurrences)
            {
                var vocabIdentity = Resolve(archiveVocabByLocalId, occurrence.VocabularyId, "archive occurrence vocabulary");
                var sentenceIdentity = Resolve(archiveDocSentenceIdentityByLocalId, occurrence.SentenceId, "archive occurrence sentence");
                var occurrenceIdentity = ComputeOccurrenceIdentity(docIdentity, sentenceIdentity, vocabIdentity, occurrence);
                var occurrenceClassification = targetOccurrenceIdentitySet.Contains(occurrenceIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
                var occurrenceReason = occurrenceClassification == MergeEntityClassification.New ? "occurrence-new" : "occurrence-exact-duplicate";
                Record(
                    MergeEntityKind.Occurrence,
                    occurrenceIdentity,
                    ComputeOccurrenceArchiveActionKey(archiveDoc.Id, occurrence.Order),
                    occurrenceClassification,
                    occurrenceReason);
            }
        }

        // ================= PreparedMeaning (ExactMeaningVariant row) =================
        // Classification is now final on this single pass — no later pass ever overrides it.
        var meaningRowState = new Dictionary<string, MeaningRowState>(StringComparer.Ordinal);
        foreach (var archiveMeaning in archive.PreparedLearning)
        {
            var vocabIdentity = Resolve(archiveVocabByLocalId, archiveMeaning.VocabularyId, "archive prepared item vocabulary");
            var semanticIdentity = archiveSemanticByLocalId[archiveMeaning.Id];
            var exactVariantIdentity = archiveExactVariantByLocalId[archiveMeaning.Id];
            var wordIsNew = !targetVocabIdentitySet.Contains(vocabIdentity);
            var semanticIsNew = !targetSemanticIdentitySet.Contains(semanticIdentity);

            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;

            if (wordIsNew)
            {
                classification = MergeEntityClassification.New;
                reason = "meaning-new-with-new-word";
            }
            else if (semanticIsNew)
            {
                // "Different SemanticMeaning: preserve both and plan separate future cards" — the word
                // already exists, but this is a genuinely new learnable sense for it. Whether a physical
                // future card can actually be scheduled is decided independently in the LearningCard pass.
                classification = MergeEntityClassification.Enriched;
                reason = "meaning-new-semantic-sense-existing-word";
            }
            else if (targetExactVariantIdentitySet.Contains(exactVariantIdentity))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "meaning-exact-duplicate";
            }
            else if (!SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(archiveMeaning))
            {
                // Same Word/languages, no reliable sense discriminator on either side (identical hashed
                // fields, by construction, since semanticIsNew is false), yet not byte-identical either —
                // the divergence can only be in fields SemanticMeaningIdentity deliberately excludes
                // (Translation/answer text, notes, aliases, provenance). Do not guess whether this is the
                // same sense or a different one.
                classification = MergeEntityClassification.UnresolvedConflict;
                reason = "semantic-meaning-grouping-ambiguous";
                decisionId = MakeDecisionId("KnownFirst.Merge.Decision.SemanticMeaningGrouping.v1", vocabIdentity.Value + "|" + exactVariantIdentity.Value);

                var targetRepresentative = targetRepresentativeBySemanticIdentity[semanticIdentity];
                semanticMeaningGroupingDecisions.Add(new SemanticMeaningGroupingDecision(
                    decisionId.Value,
                    vocabIdentity,
                    ToGroupingSummary(targetRepresentative),
                    ToGroupingSummary(archiveMeaning),
                    SemanticMeaningGroupingDecision.StandardChoices));
            }
            else
            {
                // A reliable discriminator confirms both sides really are the same sense; the divergence
                // (note/example/provenance/aliases) is a legitimate preserved content variant.
                classification = MergeEntityClassification.PreservedVariant;
                reason = "meaning-preserved-content-variant-same-sense";
            }

            meaningRowState[archiveMeaning.Id] = new MeaningRowState(classification, reason, wordIsNew, semanticIsNew);
            Record(MergeEntityKind.PreparedMeaning, exactVariantIdentity.Value, archiveMeaning.Id, classification, reason, decisionId);
        }

        // ================= LearningReview — meaning-aware fingerprint (FutureCardIdentity-keyed) =================
        // Uses a planner-specific fingerprint, distinct from the persisted Slice-1 LearningReviewFingerprint
        // (which is keyed by the physical LearningCardMatchIdentity and remains unchanged for any other
        // caller): two reviews for different SemanticMeanings must stay distinct events even when every
        // other field (Word, Direction, timestamp, rating, outcome) is equal.
        var targetReviewFingerprints = new HashSet<string>(
            target.Learning.ReviewEvents.Select(r => ComputeMeaningAwareReviewFingerprint(
                Resolve(targetFutureCardIdByLocalId, r.CardId, "target learning review card"), r)));
        var cardsWithNewEvents = new HashSet<FutureCardIdentity>();

        foreach (var review in archive.Learning.ReviewEvents)
        {
            var futureCardIdentity = Resolve(archiveFutureCardIdByLocalId, review.CardId, "archive learning review card");
            var fingerprint = ComputeMeaningAwareReviewFingerprint(futureCardIdentity, review);

            MergeEntityClassification classification;
            string reason;
            if (targetReviewFingerprints.Contains(fingerprint))
            {
                classification = MergeEntityClassification.DeduplicatedEvent;
                reason = "learning-review-exact-duplicate-event";
            }
            else
            {
                classification = MergeEntityClassification.New;
                reason = "learning-review-new-distinct-event";
                cardsWithNewEvents.Add(futureCardIdentity);
            }

            var label = review.CardId + "@" + review.ReviewedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            Record(MergeEntityKind.LearningReview, fingerprint, label, classification, reason);
        }

        // ================= LearningCard — matched by FutureCardIdentity =================
        foreach (var archiveCard in archive.Learning.Cards)
        {
            var futureCardIdentity = archiveFutureCardIdByLocalId[archiveCard.Id];
            MergeEntityClassification classification;
            string reason;

            if (targetCardsByFutureCardIdentity.ContainsKey(futureCardIdentity))
            {
                // Same FutureCardIdentity: matched card (same SemanticMeaning AND Direction). Per Rule R1
                // the card's own scheduling fields are never matrix-resolved here — only whether new
                // review history exists is determined.
                classification = cardsWithNewEvents.Contains(futureCardIdentity)
                    ? MergeEntityClassification.Enriched
                    : MergeEntityClassification.ExactDuplicateSkipped;
                reason = classification == MergeEntityClassification.Enriched
                    ? "learning-card-enriched-new-review-events"
                    : "learning-card-exact-duplicate";
            }
            else
            {
                // Distinct SemanticMeaning => a separate planned future card ("different SemanticMeaning:
                // preserve both and plan separate future cards") — never a non-blocking preserved variant.
                classification = MergeEntityClassification.New;
                var archivePhysicalIdentity = archivePhysicalCardIdentityByLocalId[archiveCard.Id];
                if (targetPhysicalCardIdentitySet.Contains(archivePhysicalIdentity))
                {
                    // Physical-slot collision: this word/direction already has a target card, but for a
                    // different SemanticMeaning. Both senses are preserved as planned content; neither the
                    // live schema (one card per (WordId, Direction)) nor the v1 archive format (verified:
                    // BackupArchiveWriter.ValidatePayloadGraph rejects two cards sharing one
                    // (VocabularyId, Direction) pair) can represent both today.
                    reason = "learning-card-new-future-card-physical-slot-collision";
                    blockingPrerequisites.Add(MergePreflightSchemaGapCodes.MeaningCardSchemaMigrationRequired);
                    blockingPrerequisites.Add(MergePreflightSchemaGapCodes.ArchiveFormatMigrationRequired);
                }
                else
                {
                    reason = "learning-card-new";
                }
            }

            Record(MergeEntityKind.LearningCard, futureCardIdentity.Value, archiveCard.Id, classification, reason);
        }

        var requiresSchedulerReplay = cardsWithNewEvents.Overlaps(targetCardsByFutureCardIdentity.Keys);

        // ================= PreferredVariantSelectionDecision (blocking) =================
        // For every matched card (same FutureCardIdentity on both sides — the same SemanticMeaning AND
        // Direction, already confirmed by identity), resolve each side's referenced PreparedItem and
        // compare their ExactMeaningVariantIdentity values. Correction (final focused review): the
        // comparison key is the referenced exact variant identity, never DisplayTerm text — DisplayTerm is
        // presentation content, not stable variant identity, and two cards can reference genuinely
        // different exact variants while happening to share the same DisplayTerm (a conflict the old
        // DisplayTerm-only comparison would silently miss). Never created for genuinely distinct
        // SemanticMeaning values — those never share a FutureCardIdentity, so no matched-card pair exists
        // to compare in the first place.
        var preferredVariantSelectionDecisions = new List<PreferredVariantSelectionDecision>();
        foreach (var archiveCard in archive.Learning.Cards.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var futureCardIdentity = archiveFutureCardIdByLocalId[archiveCard.Id];
            if (!targetCardsByFutureCardIdentity.TryGetValue(futureCardIdentity, out var targetCard))
            {
                continue;
            }

            var targetExactVariantIdentity = Resolve(targetExactVariantByLocalId, targetCard.PreparedItemId, "target preferred-variant card prepared item");
            var archiveExactVariantIdentity = Resolve(archiveExactVariantByLocalId, archiveCard.PreparedItemId, "archive preferred-variant card prepared item");
            if (targetExactVariantIdentity.Equals(archiveExactVariantIdentity))
            {
                continue;
            }

            var semanticIdentity = Resolve(archiveSemanticByLocalId, archiveCard.PreparedItemId, "archive preferred-variant card semantic meaning");
            var targetPreparedItem = Resolve(targetPreparedByLocalId, targetCard.PreparedItemId, "target preferred-variant card prepared item content");
            var archivePreparedItem = Resolve(archivePreparedByLocalId, archiveCard.PreparedItemId, "archive preferred-variant card prepared item content");

            var decisionId = MakeDecisionId(
                "KnownFirst.Merge.Decision.PreferredVariant.v2",
                futureCardIdentity.Value + "|" + targetExactVariantIdentity.Value + "|" + archiveExactVariantIdentity.Value);
            preferredVariantSelectionDecisions.Add(new PreferredVariantSelectionDecision(
                decisionId,
                futureCardIdentity,
                semanticIdentity,
                targetExactVariantIdentity,
                archiveExactVariantIdentity,
                targetPreparedItem.DisplayTerm,
                archivePreparedItem.DisplayTerm,
                PreferredVariantSelectionDecision.StandardChoices));
        }

        // ================= ContextSnapshot + derived AnswerVariant plans =================
        var targetContextIdentitySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var meaning in target.PreparedLearning)
        {
            var semanticIdentity = targetSemanticByLocalId[meaning.Id];
            foreach (var context in meaning.Contexts)
            {
                var docIdentity = Resolve(targetDocByLocalId, context.SourceMaterialId, "target context snapshot source material");
                targetContextIdentitySet.Add(ComputeContextSnapshotIdentity(semanticIdentity, docIdentity, context));
            }
        }

        foreach (var archiveMeaning in archive.PreparedLearning)
        {
            var rowState = meaningRowState[archiveMeaning.Id];
            var semanticIdentity = archiveSemanticByLocalId[archiveMeaning.Id];

            // ContextSnapshot: independent identity (SemanticMeaning + SourceMaterial + fingerprint +
            // position) — never inherits the parent row's classification.
            foreach (var context in archiveMeaning.Contexts)
            {
                var docIdentity = Resolve(archiveDocByLocalId, context.SourceMaterialId, "archive context snapshot source material");
                var contextIdentity = ComputeContextSnapshotIdentity(semanticIdentity, docIdentity, context);
                var contextClassification = targetContextIdentitySet.Contains(contextIdentity) ? MergeEntityClassification.ExactDuplicateSkipped : MergeEntityClassification.New;
                var contextReason = contextClassification == MergeEntityClassification.New ? "context-snapshot-new" : "context-snapshot-exact-duplicate";
                Record(MergeEntityKind.ContextSnapshot, contextIdentity, archiveMeaning.Id + ":" + context.NormalizedFingerprint, contextClassification, contextReason);
            }

            // Derived answer-variant plans: not a physical entity, never counted as a primary action.
            var seenInThisRow = new HashSet<AnswerVariantIdentity>();
            void RecordAnswerVariant(string? text, AnswerVariantRole role)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var variantIdentity = AnswerVariantIdentityPolicy.Compute(semanticIdentity, text, archiveMeaning.ExplanationLanguage);
                if (!seenInThisRow.Add(variantIdentity))
                {
                    return;
                }

                var roleLabel = role == AnswerVariantRole.PrimaryAnswer ? "primary" : "alias";
                MergeEntityClassification classification;
                string reason;
                if (rowState.WordIsNew || rowState.SemanticIsNew)
                {
                    classification = MergeEntityClassification.New;
                    reason = $"answer-variant-{roleLabel}-new";
                }
                else if (targetAnswerVariantIdentitySet.Contains(variantIdentity))
                {
                    classification = MergeEntityClassification.ExactDuplicateSkipped;
                    reason = $"answer-variant-{roleLabel}-exact-duplicate";
                }
                else
                {
                    classification = MergeEntityClassification.Enriched;
                    reason = $"answer-variant-{roleLabel}-new-synonym";
                    warningCodes.Add(MergePreflightSchemaGapCodes.AnswerVariantProgressMigrationRequired);
                }

                derivedAnswerVariantPlans.Add(new DerivedAnswerVariantPlan(variantIdentity, archiveMeaning.Id, role, classification, reason));
            }

            RecordAnswerVariant(archiveMeaning.DisplayTerm, AnswerVariantRole.PrimaryAnswer);
            foreach (var alias in archiveMeaning.AcceptedAliases)
            {
                RecordAnswerVariant(alias, AnswerVariantRole.AcceptedAlias);
            }
        }

        // ================= VocabularyReviewWorkflow + VocabularyReviewItem — full historical content =================
        var targetReviewItemContentByIdentity = new Dictionary<ReviewCandidateIdentity, BackupVocabularyReviewItem>();
        foreach (var session in target.Workflows.VocabularyReviews)
        {
            var docIdentity = Resolve(targetDocByLocalId, session.SourceMaterialId, "target review session source material");
            foreach (var item in session.Items)
            {
                var itemIdentity = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(item, docIdentity, targetVocabByLocalId);
                targetReviewItemContentByIdentity.TryAdd(itemIdentity, item);
            }
        }

        foreach (var session in archive.Workflows.VocabularyReviews)
        {
            var identity = ReviewWorkflowIdentityPolicy.ComputeSessionIdentity(session, archiveDocByLocalId);
            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;

            if (!targetReviewSessionByIdentity.TryGetValue(identity, out var targetSession))
            {
                classification = MergeEntityClassification.New;
                reason = "review-workflow-new";
            }
            else if (ReviewWorkflowContentEquals(targetSession, session))
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "review-workflow-exact-duplicate";
            }
            else
            {
                // ReviewSessionEntity.DocumentId is uniquely indexed live-schema-side: only one physical
                // session row can ever exist per document, so two independently completed sessions for
                // the same document cannot both be preserved as separate rows.
                classification = MergeEntityClassification.UnresolvedConflict;
                reason = "review-workflow-history-divergence";
                decisionId = MakeDecisionId("KnownFirst.Merge.Decision.WorkflowHistory.ReviewSession.v1", identity.Value);
                workflowStatusDecisions.Add(new WorkflowStatusConflictDecision(decisionId.Value, MergeEntityKind.VocabularyReviewWorkflow, session.Id, reason));
                blockingPrerequisites.Add(MergePreflightSchemaGapCodes.WorkflowHistorySchemaMigrationRequired);
            }

            Record(MergeEntityKind.VocabularyReviewWorkflow, identity.Value, session.Id, classification, reason, decisionId);

            var docIdentityForItems = Resolve(archiveDocByLocalId, session.SourceMaterialId, "archive review session source material");
            foreach (var item in session.Items)
            {
                var itemIdentity = ReviewWorkflowIdentityPolicy.ComputeCandidateIdentity(item, docIdentityForItems, archiveVocabByLocalId);
                MergeEntityClassification itemClassification;
                string itemReason;
                if (!targetReviewItemContentByIdentity.TryGetValue(itemIdentity, out var targetItem))
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "review-item-new";
                }
                else if (ReviewItemContentEquals(targetItem, item))
                {
                    itemClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    itemReason = "review-item-exact-duplicate";
                }
                else
                {
                    // Order is not part of ReviewCandidateIdentity and is not DB-unique beyond
                    // (SessionId, Order) — a second, content-divergent row is representable and preserved.
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "review-item-preserved-divergent-history";
                }

                Record(MergeEntityKind.VocabularyReviewItem, itemIdentity.Value, item.Id, itemClassification, itemReason);
            }
        }

        // ================= PreparationWorkflow + PreparationItem =================
        var targetPrepSessionIdByLocalId = BuildIdentityMap(target.Workflows.PreparationBatches, s => s.Id, s => PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(s, targetVocabByLocalId), "target preparation session");
        var archivePrepSessionIdByLocalId = BuildIdentityMap(archive.Workflows.PreparationBatches, s => s.Id, s => PreparationWorkflowIdentityPolicy.ComputeSessionIdentity(s, archiveVocabByLocalId), "archive preparation session");
        var targetPrepSessionsByIdentity = ToUniqueDictionary(target.Workflows.PreparationBatches, s => targetPrepSessionIdByLocalId[s.Id], "target preparation session identity");

        var targetPrepItemContentByIdentity = new Dictionary<PreparationCandidateIdentity, BackupPreparationItem>();
        foreach (var session in target.Workflows.PreparationBatches)
        {
            var sessionIdentity = targetPrepSessionIdByLocalId[session.Id];
            foreach (var item in session.Items)
            {
                targetPrepItemContentByIdentity.TryAdd(PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(item, sessionIdentity, targetVocabByLocalId), item);
            }
        }

        foreach (var session in archive.Workflows.PreparationBatches)
        {
            var identity = archivePrepSessionIdByLocalId[session.Id];
            MergeEntityClassification classification;
            string reason;
            DecisionId? decisionId = null;

            if (!targetPrepSessionsByIdentity.TryGetValue(identity, out var targetSession))
            {
                classification = MergeEntityClassification.New;
                reason = "preparation-workflow-new";
            }
            else if (targetSession.Status == session.Status)
            {
                classification = MergeEntityClassification.ExactDuplicateSkipped;
                reason = "preparation-workflow-exact-duplicate";
            }
            else
            {
                var statusResult = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(targetSession.Status, session.Status);
                if (statusResult.Classification == MergeConflictClassification.UnresolvedKeepTargetWithWarning)
                {
                    classification = MergeEntityClassification.UnresolvedConflict;
                    reason = statusResult.ReasonCode;
                    warningCodes.Add(reason);
                    decisionId = MakeDecisionId("KnownFirst.Merge.Decision.WorkflowStatus.v1", identity.Value);
                    workflowStatusDecisions.Add(new WorkflowStatusConflictDecision(decisionId.Value, MergeEntityKind.PreparationWorkflow, session.Id, reason));
                }
                else
                {
                    // Defensive only: both portable statuses (Completed, Cancelled) are same-tier by
                    // design, so this branch is unreachable for valid archives.
                    classification = MergeEntityClassification.Enriched;
                    reason = "preparation-workflow-status-monotonic-advance";
                }
            }

            Record(MergeEntityKind.PreparationWorkflow, identity.Value, session.Id, classification, reason, decisionId);

            var sessionIdentity = identity;
            foreach (var item in session.Items)
            {
                var itemIdentity = PreparationWorkflowIdentityPolicy.ComputeCandidateIdentity(item, sessionIdentity, archiveVocabByLocalId);
                MergeEntityClassification itemClassification;
                string itemReason;
                if (!targetPrepItemContentByIdentity.TryGetValue(itemIdentity, out var targetItem))
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "preparation-item-new";
                }
                else if (PreparationItemContentEquals(targetItem, item))
                {
                    itemClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    itemReason = "preparation-item-exact-duplicate";
                }
                else
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "preparation-item-preserved-divergent-history";
                }

                Record(MergeEntityKind.PreparationItem, itemIdentity.Value, item.Id, itemClassification, itemReason);
            }
        }

        // ================= LearningWorkflow + LearningQueueItem =================
        var targetLearningSessionIdByLocalId = BuildIdentityMap(target.Workflows.LearningSessions, s => s.Id, s => LearningWorkflowIdentityPolicy.ComputeSessionIdentity(s, targetPhysicalCardIdentityByLocalId), "target learning session");
        var archiveLearningSessionIdByLocalId = BuildIdentityMap(archive.Workflows.LearningSessions, s => s.Id, s => LearningWorkflowIdentityPolicy.ComputeSessionIdentity(s, archivePhysicalCardIdentityByLocalId), "archive learning session");
        var targetLearningSessionIdentitySet = new HashSet<LearningSessionIdentity>(targetLearningSessionIdByLocalId.Values);

        var targetLearningQueueItemContentByIdentity = new Dictionary<LearningSessionCardIdentity, BackupLearningQueueItem>();
        foreach (var session in target.Workflows.LearningSessions)
        {
            var sessionIdentity = targetLearningSessionIdByLocalId[session.Id];
            foreach (var item in session.QueueItems)
            {
                targetLearningQueueItemContentByIdentity.TryAdd(
                    LearningWorkflowIdentityPolicy.ComputeSessionCardIdentity(item, sessionIdentity, targetPhysicalCardIdentityByLocalId), item);
            }
        }

        foreach (var session in archive.Workflows.LearningSessions)
        {
            var identity = archiveLearningSessionIdByLocalId[session.Id];
            var classification = targetLearningSessionIdentitySet.Contains(identity)
                ? MergeEntityClassification.ExactDuplicateSkipped
                : MergeEntityClassification.New;
            var reason = classification == MergeEntityClassification.New ? "learning-workflow-new" : "learning-workflow-exact-duplicate";

            Record(MergeEntityKind.LearningWorkflow, identity.Value, session.Id, classification, reason);

            var sessionIdentity = identity;
            foreach (var item in session.QueueItems)
            {
                var itemIdentity = LearningWorkflowIdentityPolicy.ComputeSessionCardIdentity(item, sessionIdentity, archivePhysicalCardIdentityByLocalId);
                MergeEntityClassification itemClassification;
                string itemReason;
                if (!targetLearningQueueItemContentByIdentity.TryGetValue(itemIdentity, out var targetItem))
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "learning-queue-item-new";
                }
                else if (LearningQueueItemContentEquals(targetItem, item))
                {
                    itemClassification = MergeEntityClassification.ExactDuplicateSkipped;
                    itemReason = "learning-queue-item-exact-duplicate";
                }
                else
                {
                    itemClassification = MergeEntityClassification.New;
                    itemReason = "learning-queue-item-preserved-divergent-history";
                }

                Record(MergeEntityKind.LearningQueueItem, itemIdentity.Value, item.Id, itemClassification, itemReason);
            }
        }

        // ================= Finalize: deterministic ordering, status, sample details =================
        var sortedActions = actions
            .OrderBy(a => (int)a.EntityKind)
            .ThenBy(a => a.StableIdentity, StringComparer.Ordinal)
            .ThenBy(a => a.ArchiveLocalId, StringComparer.Ordinal)
            .ToList();

        var sortedDerivedAnswerVariantPlans = derivedAnswerVariantPlans
            .OrderBy(p => p.StableIdentity.Value, StringComparer.Ordinal)
            .ThenBy(p => p.SourcePreparedItemArchiveLocalId, StringComparer.Ordinal)
            .ThenBy(p => p.Role)
            .ToList();

        var sampleDetails = new Dictionary<MergeEntityClassification, List<string>>();
        foreach (var action in sortedActions)
        {
            if (!sampleDetails.TryGetValue(action.Classification, out var list))
            {
                list = [];
                sampleDetails[action.Classification] = list;
            }

            if (list.Count < MergePreflightPlan.MaxSampleDetailsPerCategory)
            {
                list.Add($"{action.EntityKind}:{action.ArchiveLocalId}:{action.ReasonCode}");
            }
        }

        var sampleDetailsReadOnly = sampleDetails.ToDictionary(
            entry => entry.Key,
            IReadOnlyList<string> (entry) => entry.Value);

        var sortedKnowledgeStateDecisions = knowledgeStateDecisions
            .OrderBy(d => d.DecisionId.Value, StringComparer.Ordinal)
            .ToList();
        var sortedWorkflowStatusDecisions = workflowStatusDecisions
            .OrderBy(d => d.DecisionId.Value, StringComparer.Ordinal)
            .ToList();
        var sortedSemanticMeaningGroupingDecisions = semanticMeaningGroupingDecisions
            .OrderBy(d => d.DecisionId.Value, StringComparer.Ordinal)
            .ToList();
        var sortedPreferredVariantSelectionDecisions = preferredVariantSelectionDecisions
            .OrderBy(d => d.DecisionId.Value, StringComparer.Ordinal)
            .ToList();

        // Invariant: every UnresolvedConflict action carries a DecisionId, and the total number of such
        // actions equals the total number of decisions that map onto a single action (KnowledgeState,
        // WorkflowStatus, SemanticMeaningGrouping). PreferredVariantSelectionDecision spans a pair of
        // otherwise-fine rows and deliberately does not tag any action UnresolvedConflict, but still
        // contributes to RequiresUserDecision below.
        var actionMappedDecisionCount = sortedKnowledgeStateDecisions.Count + sortedWorkflowStatusDecisions.Count + sortedSemanticMeaningGroupingDecisions.Count;
        var unresolvedActionCount = sortedActions.Count(a => a.Classification == MergeEntityClassification.UnresolvedConflict);
        if (unresolvedActionCount != actionMappedDecisionCount)
        {
            throw new InvalidOperationException(
                "Internal invariant violated: every UnresolvedConflict action must reference exactly one decision, and every action-mapped decision must correspond to exactly one UnresolvedConflict action.");
        }

        var totalBlockingDecisions = actionMappedDecisionCount + sortedPreferredVariantSelectionDecisions.Count;
        var sortedBlockingPrerequisites = blockingPrerequisites.ToList();
        var hasInsertableChange = counts.Values.Any(c => c.TotalInsertableCount > 0);

        MergePreflightStatus status;
        if (totalBlockingDecisions > 0)
        {
            status = MergePreflightStatus.RequiresUserDecision;
        }
        else if (sortedBlockingPrerequisites.Count > 0)
        {
            status = MergePreflightStatus.BlockedByPrerequisite;
        }
        else if (hasInsertableChange)
        {
            status = MergePreflightStatus.Ready;
        }
        else
        {
            status = MergePreflightStatus.NoChanges;
        }

        var isExecutable = status is MergePreflightStatus.Ready or MergePreflightStatus.NoChanges;

        var manifestInfo = new MergeManifestInfo(
            archiveManifest.FormatVersion,
            archiveManifest.SourceAppVersion,
            archiveManifest.SourceDatabaseSchemaVersion,
            archiveManifest.CreatedAtUtc,
            archiveManifest.SourcePlatform);

        return new MergePreflightPlan(
            status,
            isExecutable,
            manifestInfo,
            true,
            counts,
            sortedActions,
            sortedDerivedAnswerVariantPlans,
            sortedKnowledgeStateDecisions,
            sortedWorkflowStatusDecisions,
            sortedSemanticMeaningGroupingDecisions,
            sortedPreferredVariantSelectionDecisions,
            sortedBlockingPrerequisites,
            sampleDetailsReadOnly,
            [.. warningCodes],
            requiresSchedulerReplay,
            null);
    }

    private static SemanticMeaningVariantSummary ToGroupingSummary(BackupPreparedItem item) => new(
        string.IsNullOrWhiteSpace(item.Definition) ? null : item.Definition,
        string.IsNullOrWhiteSpace(item.Translation) ? null : item.Translation,
        string.IsNullOrWhiteSpace(item.ProviderMeaningId) ? null : item.ProviderMeaningId,
        string.IsNullOrWhiteSpace(item.GrammaticalRelationship) ? null : item.GrammaticalRelationship,
        null, // topic/domain is never persisted today — never guessed.
        item.ExplanationLanguage);

    private static void AddAnswerVariantIdentities(HashSet<AnswerVariantIdentity> set, SemanticMeaningIdentity semanticIdentity, BackupPreparedItem meaning)
    {
        if (!string.IsNullOrWhiteSpace(meaning.DisplayTerm))
        {
            set.Add(AnswerVariantIdentityPolicy.Compute(semanticIdentity, meaning.DisplayTerm, meaning.ExplanationLanguage));
        }

        foreach (var alias in meaning.AcceptedAliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                set.Add(AnswerVariantIdentityPolicy.Compute(semanticIdentity, alias, meaning.ExplanationLanguage));
            }
        }
    }

    /// <summary>
    /// Planner-specific review-event fingerprint, distinct from the persisted Slice-1
    /// <see cref="LearningReviewFingerprintPolicy"/> (which remains unchanged for its own callers/tests):
    /// keyed by <see cref="FutureCardIdentity"/> instead of the physical <see cref="LearningCardMatchIdentity"/>,
    /// so two reviews for different SemanticMeanings never collapse into one event merely because they
    /// share a Word, Direction, timestamp, rating, and outcome.
    /// </summary>
    private static string ComputeMeaningAwareReviewFingerprint(FutureCardIdentity futureCardIdentity, BackupLearningReview review)
    {
        var builder = new CanonicalFingerprintBuilder("KnownFirst.Merge.Preflight.MeaningAwareLearningReview.v1")
            .WriteString(futureCardIdentity.Value)
            .WriteUtcTimestamp(review.ReviewedAtUtc)
            .WriteEnum(review.Rating)
            .WriteBoolean(review.WasTypedAnswer)
            .WriteBoolean(review.WasCorrect)
            .WriteUtcTimestamp(review.DueAtUtc)
            .WriteInt32(review.IntervalDays)
            .WriteDouble(review.EaseFactor);

        return builder.ComputeSha256Hex();
    }

    internal static string ComputeSentenceRangeIdentity(SourceMaterialIdentity documentIdentity, BackupSentenceRange sentence)
    {
        var builder = new CanonicalFingerprintBuilder("KnownFirst.Merge.SentenceRange.v1")
            .WriteString(documentIdentity.Value)
            .WriteInt32(sentence.Order)
            .WriteInt32(sentence.Start)
            .WriteInt32(sentence.Length);

        return builder.ComputeSha256Hex();
    }

    internal static string ComputeOccurrenceIdentity(
        SourceMaterialIdentity documentIdentity, string sentenceIdentity, VocabularyIdentity vocabularyIdentity, BackupOccurrence occurrence)
    {
        var builder = new CanonicalFingerprintBuilder("KnownFirst.Merge.Occurrence.v1")
            .WriteString(documentIdentity.Value)
            .WriteString(sentenceIdentity)
            .WriteString(vocabularyIdentity.Value)
            .WriteInt32(occurrence.Start)
            .WriteInt32(occurrence.Length)
            .WriteString(occurrence.SurfaceForm)
            .WriteInt32(occurrence.Order)
            .WriteEnum(occurrence.TechnicalFamily)
            .WriteNullableInt32(occurrence.TechnicalInstanceYear)
            .WriteNullableString(occurrence.TechnicalInstanceIdentifier)
            .WriteNullableString(occurrence.TechnicalVariant);

        return builder.ComputeSha256Hex();
    }

    internal static string ComputeOccurrenceArchiveActionKey(
        string sourceMaterialArchiveId, int occurrenceOrder) =>
        sourceMaterialArchiveId + ":" + occurrenceOrder.ToString(CultureInfo.InvariantCulture);

    internal static string ComputeEncounteredFormIdentity(VocabularyIdentity vocabularyIdentity, string surfaceForm)
    {
        var builder = new CanonicalFingerprintBuilder("KnownFirst.Merge.EncounteredForm.v1")
            .WriteString(vocabularyIdentity.Value)
            .WriteString(CanonicalText.NormalizeOptional(surfaceForm));

        return builder.ComputeSha256Hex();
    }

    internal static string ComputeLegacyReviewSummaryIdentity(VocabularyIdentity vocabularyIdentity)
    {
        var builder = new CanonicalFingerprintBuilder("KnownFirst.Merge.LegacyReviewSummary.v1")
            .WriteString(vocabularyIdentity.Value);

        return builder.ComputeSha256Hex();
    }

    internal static string ComputeContextSnapshotIdentity(
        SemanticMeaningIdentity semanticMeaningIdentity, SourceMaterialIdentity sourceMaterialIdentity, BackupContextSnapshot context)
    {
        var builder = new CanonicalFingerprintBuilder("KnownFirst.Merge.ContextSnapshot.v1")
            .WriteString(semanticMeaningIdentity.Value)
            .WriteString(sourceMaterialIdentity.Value)
            .WriteString(context.NormalizedFingerprint)
            .WriteInt32(context.TargetStart)
            .WriteInt32(context.TargetLength);

        return builder.ComputeSha256Hex();
    }

    internal static bool ReviewWorkflowContentEquals(BackupVocabularyReviewWorkflow a, BackupVocabularyReviewWorkflow b) =>
        a.Status == b.Status
        && a.TotalCandidates == b.TotalCandidates
        && a.ReviewedCount == b.ReviewedCount
        && a.KnownCount == b.KnownCount
        && a.UnknownCount == b.UnknownCount
        && a.IgnoredCount == b.IgnoredCount
        && a.DecisionSequence == b.DecisionSequence
        && a.StartedAtUtc == b.StartedAtUtc
        && a.CompletedAtUtc == b.CompletedAtUtc;

    /// <summary>Order is intentionally excluded — positional, not semantically meaningful (proven safe to renumber).</summary>
    internal static bool ReviewItemContentEquals(BackupVocabularyReviewItem a, BackupVocabularyReviewItem b) =>
        a.Status == b.Status
        && a.PreviousKnowledgeState == b.PreviousKnowledgeState
        && a.PreviousTotalOccurrenceCount == b.PreviousTotalOccurrenceCount
        && a.PreviousDocumentCount == b.PreviousDocumentCount
        && a.PreviousUpdatedAtUtc == b.PreviousUpdatedAtUtc
        && a.DecisionSequence == b.DecisionSequence
        && a.WasVocabularyCreatedForSession == b.WasVocabularyCreatedForSession
        && a.DecidedAtUtc == b.DecidedAtUtc;

    /// <summary>
    /// Compares the directly retrievable candidate-decision fields. The nested <c>LookupDraft</c> (a
    /// snapshot of raw provider lookup results) is intentionally not deep-compared here: its collection
    /// fields do not have structural equality, and it is not itself the "historical decision" content this
    /// comparison protects — the fields compared are.
    /// </summary>
    internal static bool PreparationItemContentEquals(BackupPreparationItem a, BackupPreparationItem b) =>
        a.Status == b.Status
        && a.SelectedMeaningIndex == b.SelectedMeaningIndex
        && a.LastErrorCode == b.LastErrorCode
        && a.LookupAttemptCount == b.LookupAttemptCount
        && a.UpdatedAtUtc == b.UpdatedAtUtc;

    private static bool LearningQueueItemContentEquals(BackupLearningQueueItem a, BackupLearningQueueItem b) =>
        a.IsDueCard == b.IsDueCard
        && a.IsAgainRepeat == b.IsAgainRepeat
        && a.AnswerRevealed == b.AnswerRevealed
        && a.SpellingChecked == b.SpellingChecked
        && a.SpellingCorrect == b.SpellingCorrect
        && a.IsCompleted == b.IsCompleted
        && a.Rating == b.Rating
        && a.CompletedAtUtc == b.CompletedAtUtc;

    internal static Dictionary<MergeEntityKind, MergeEntityPlanCounts> CreateEmptyCounts() =>
        Enum.GetValues<MergeEntityKind>().ToDictionary(kind => kind, _ => MergeEntityPlanCounts.Zero);

    internal static Dictionary<string, TIdentity> BuildIdentityMap<TItem, TIdentity>(
        IEnumerable<TItem> items,
        Func<TItem, string> idSelector,
        Func<TItem, TIdentity> identityFn,
        string context)
        where TIdentity : notnull
    {
        var map = new Dictionary<string, TIdentity>();
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (!map.TryAdd(id, identityFn(item)))
            {
                throw new MergePlanningException(
                    BackupErrorCodes.DuplicateId,
                    $"Duplicate {context} archive-local id '{id}' produces an ambiguous merge identity.");
            }
        }

        return map;
    }

    internal static Dictionary<TKey, TValue> ToUniqueDictionary<TValue, TKey>(
        IEnumerable<TValue> items, Func<TValue, TKey> keySelector, string context)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (var item in items)
        {
            var key = keySelector(item);
            if (!result.TryAdd(key, item))
            {
                throw new MergePlanningException(
                    BackupErrorCodes.DuplicateId,
                    $"Ambiguous {context}: more than one item resolved to the same stable identity.");
            }
        }

        return result;
    }

    internal static TValue Resolve<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> map, TKey localId, string context)
        where TKey : notnull
    {
        if (!map.TryGetValue(localId, out var value))
        {
            throw new KeyNotFoundException($"No stable {context} identity supplied for archive-local id '{localId}'.");
        }

        return value;
    }
}
