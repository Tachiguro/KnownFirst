using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Deterministic, pure archive-format v1→v2 in-memory upgrade (KF-MEANING-001 Slice 2). Consumes a
/// validated v1 <see cref="BackupPayload"/> and produces the canonical upgraded <see cref="BackupPayloadV2"/>
/// graph, using the exact same Sense-grouping/preferred-candidate-selection rules as the live
/// <see cref="Schema8DormantMigration"/> (via the shared, connection-free <see cref="Schema8SemanticUpgradePolicy"/>),
/// parameterized by <em>this</em> upgrader's own source-order key: ordinal v1 Meaning archive id — never
/// local SQLite <c>Meaning.Id</c>, which a v1 archive never preserved. No I/O, no
/// <see cref="Guid.NewGuid"/>, no current-time dependency: every synthesized timestamp is derived from the
/// source archive's own content, so calling <see cref="Upgrade"/> twice on the same valid v1 archive
/// produces a byte-identical <see cref="BackupPayloadV2"/>.
/// </summary>
public static class BackupArchiveV1UpgradePolicy
{
    public static BackupPayloadV2 Upgrade(BackupPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var meaningsByVocab = payload.PreparedLearning
            .GroupBy(m => m.VocabularyId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var cardsByVocab = payload.Learning.Cards
            .GroupBy(c => c.VocabularyId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var vocabularyOut = new List<BackupVocabularyItem>();
        var sensesOut = new List<BackupSense>();
        var preparedOut = new List<BackupPreparedItemV2>();
        var answerVariantsOut = new List<BackupAnswerVariant>();
        var assignmentsOut = new List<BackupSenseAnswerVariantAssignment>();
        var cardsOut = new List<BackupLearningCardV2>();
        var progressOut = new List<BackupAnswerVariantProgress>();

        // (CardArchiveId) -> (SenseArchiveId, Direction), populated as cards are built below; consumed by
        // review/queue/progress mapping which all need to know a card's Sense+direction.
        var cardSenseDirection = new Dictionary<string, (string SenseId, BackupCardDirection Direction)>(StringComparer.Ordinal);
        // (SenseArchiveId, Direction) -> the direction's preferred AnswerVariant archive/StableId (the
        // upgrader's counterpart of the migration's MigrationContext.DirectionVariantId).
        var directionVariantId = new Dictionary<(string SenseId, BackupCardDirection Direction), string>();

        foreach (var vocab in payload.Vocabulary)
        {
            if (!meaningsByVocab.TryGetValue(vocab.Id, out var meaningsForVocab) || meaningsForVocab.Count == 0)
            {
                vocabularyOut.Add(vocab);
                continue;
            }

            vocabularyOut.Add(DowngradeKnowledgeStateIfNeeded(vocab));

            // Source-order key for this upgrader: ordinal v1 Meaning archive id ascending — never local
            // Meaning.Id (unavailable from a v1 archive).
            var sortedMeanings = meaningsForVocab.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();

            var rows = new List<LegacyMeaningRow>(sortedMeanings.Count);
            var rowToV1 = new Dictionary<int, BackupPreparedItem>();
            for (var i = 0; i < sortedMeanings.Count; i++)
            {
                var m = sortedMeanings[i];
                rows.Add(new LegacyMeaningRow
                {
                    Id = i, // synthetic, position-based — encodes only this upgrade run's source order
                    DisplayTerm = m.DisplayTerm,
                    Translation = m.Translation ?? string.Empty,
                    SourceLanguage = m.SourceLanguage,
                    ExplanationLanguage = m.ExplanationLanguage,
                    // These three must be populated exactly — GroupMeaningsIntoSenses's discriminator/
                    // identity computation (via Schema8SemanticUpgradePolicy.BuildPreparedItem) reads them;
                    // leaving them at LegacyMeaningRow's empty-string defaults would silently force every
                    // group to look discriminator-less, splitting every Sense into singletons.
                    SelectedMeaningId = m.ProviderMeaningId ?? string.Empty,
                    GrammaticalRelationship = m.GrammaticalRelationship ?? string.Empty,
                    AcronymExpansion = m.AcronymExpansion ?? string.Empty
                });
                rowToV1[i] = m;
            }

            var vocabularyIdentity = ComputeVocabularyIdentity(vocab);
            var groups = Schema8SemanticUpgradePolicy.GroupMeaningsIntoSenses(rows, vocabularyIdentity);
            var senseStatus = TranslateInitialSenseStatus(vocab.KnowledgeState);

            foreach (var group in groups)
            {
                var representativeRow = group[0];
                var representativeV1 = rowToV1[representativeRow.Id];
                var hasDiscriminator = SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(representativeV1);
                var semanticIdentity = SemanticMeaningIdentityPolicy.Compute(representativeV1, vocabularyIdentity);

                var memberFingerprints = group
                    .Select(row => rowToV1[row.Id])
                    .Select(v1 => v1.Id + ":" + ExactMeaningVariantIdentityPolicy.Compute(v1, semanticIdentity).Value)
                    .OrderBy(fingerprint => fingerprint, StringComparer.Ordinal)
                    .ToList();

                var senseSeed = "sense-v1upgrade|" + vocab.Id + "|"
                    + (hasDiscriminator ? semanticIdentity.Value : "no-reliable-discriminator") + "|"
                    + string.Join(",", memberFingerprints);
                var senseStableId = DeterministicStableId.Generate(senseSeed);
                var senseArchiveId = senseStableId;

                // Meanings first (archive id preserved verbatim from v1 — this is what makes a card's
                // PreferredMeaningId a direct copy of the v1 card's PreparedItemId, with no re-derivation).
                foreach (var row in group)
                {
                    var v1 = rowToV1[row.Id];
                    var exactIdentity = ExactMeaningVariantIdentityPolicy.Compute(v1, semanticIdentity);
                    var meaningSeed = "meaning-v1upgrade|" + vocab.Id + "|" + v1.Id + "|" + exactIdentity.Value;
                    var meaningStableId = DeterministicStableId.Generate(meaningSeed);

                    preparedOut.Add(new BackupPreparedItemV2(
                        v1.Id, senseArchiveId, meaningStableId, v1.VocabularyId, v1.SourceLanguage, v1.ExplanationLanguage,
                        v1.DisplayTerm, v1.EncounteredSurfaceForm, v1.GrammaticalRelationship, v1.TokenKind,
                        v1.ProviderMeaningId, v1.AcronymExpansion, v1.Translation, v1.Definition, v1.DictionaryExample,
                        v1.AdditionalNote, v1.LegacyAnswerText, v1.AcceptedAliases, v1.ConfirmedByUser, v1.Source,
                        v1.CreatedAtUtc, v1.UpdatedAtUtc, v1.PreparedAtUtc,
                        v1.Contexts.Select(c => new BackupContextSnapshotV2(
                            c.SourceMaterialId, c.SourceTitle, c.Text, c.TargetStart, c.TargetLength,
                            c.NormalizedFingerprint, c.CreatedAtUtc, senseArchiveId)).ToList()));
                }

                sensesOut.Add(new BackupSense(
                    senseArchiveId, senseStableId, vocab.Id, representativeV1.SourceLanguage, representativeV1.ExplanationLanguage,
                    representativeV1.ProviderMeaningId ?? string.Empty, string.Empty, string.Empty,
                    representativeV1.GrammaticalRelationship ?? string.Empty, representativeV1.AcronymExpansion ?? string.Empty,
                    representativeV1.Id, senseStatus, representativeV1.CreatedAtUtc, representativeV1.UpdatedAtUtc));

                var groupMemberIds = group.Select(row => rowToV1[row.Id].Id).ToHashSet(StringComparer.Ordinal);
                var cardsForGroup = (cardsByVocab.TryGetValue(vocab.Id, out var vocabCards) ? vocabCards : [])
                    .Where(c => groupMemberIds.Contains(c.PreparedItemId))
                    .ToList();
                var directionsPresent = cardsForGroup.Select(c => c.Direction).Distinct().ToHashSet();

                foreach (var card in cardsForGroup)
                {
                    cardSenseDirection[card.Id] = (senseArchiveId, card.Direction);
                }

                var variantIdByKey = new Dictionary<(string Language, string Normalized), string>();
                var assignmentKeys = new HashSet<(string VariantId, BackupCardDirection Direction)>();

                string GetOrCreateVariant(string language, string text, BackupPreparedItem sourceMeaning)
                {
                    var normalized = CanonicalText.NormalizeOptional(text);
                    var key = (language, normalized);
                    if (variantIdByKey.TryGetValue(key, out var existingId))
                    {
                        return existingId;
                    }

                    var variantSeed = "answervariant-v1upgrade|" + senseStableId + "|" + language + "|" + normalized;
                    var variantStableId = DeterministicStableId.Generate(variantSeed);
                    answerVariantsOut.Add(new BackupAnswerVariant(
                        variantStableId, variantStableId, senseArchiveId, language, text, normalized, sourceMeaning.Id,
                        sourceMeaning.CreatedAtUtc, sourceMeaning.UpdatedAtUtc));
                    variantIdByKey[key] = variantStableId;
                    return variantStableId;
                }

                // KF-MEANING-001 Slice 4: the deterministic primary assignment of a direction becomes
                // Required with RequiredSinceUtc = that direction's card CreatedAtUtc, exactly as the live
                // migration does, so the upgraded graph keeps the card's full legacy history inside its first
                // Required epoch. Every other assignment stays AcceptedOnly with a null boundary.
                DateTime? DirectionBoundary(BackupCardDirection direction) =>
                    cardsForGroup.Where(c => c.Direction == direction)
                        .Select(c => (DateTime?)c.CreatedAtUtc)
                        .FirstOrDefault();

                void EnsureAssignmentLocal(
                    string variantId,
                    BackupCardDirection direction,
                    bool isPreferred,
                    BackupPreparedItem sourceMeaning,
                    DateTime? requiredSinceUtc = null)
                {
                    if (!assignmentKeys.Add((variantId, direction)))
                    {
                        return; // never downgrade or duplicate an existing assignment
                    }

                    var assignmentSeed = "assignment-v1upgrade|" + senseStableId + "|" + direction + "|" + variantId;
                    var assignmentStableId = DeterministicStableId.Generate(assignmentSeed);
                    assignmentsOut.Add(new BackupSenseAnswerVariantAssignment(
                        assignmentStableId, assignmentStableId, senseArchiveId, direction, variantId,
                        requiredSinceUtc.HasValue
                            ? BackupAnswerVariantRequirement.Required
                            : BackupAnswerVariantRequirement.AcceptedOnly,
                        isPreferred,
                        sourceMeaning.CreatedAtUtc, sourceMeaning.UpdatedAtUtc, requiredSinceUtc));
                    if (isPreferred)
                    {
                        directionVariantId[(senseArchiveId, direction)] = variantId;
                    }
                }

                // Preferred assignments (must be created before the loop below so first-add wins).
                if (directionsPresent.Contains(BackupCardDirection.MeaningToTerm))
                {
                    var chosen = Schema8SemanticUpgradePolicy.SelectPreferredCandidate(group, r => r.DisplayTerm);
                    if (chosen is not null)
                    {
                        var chosenV1 = rowToV1[chosen.Id];
                        var variantId = GetOrCreateVariant(chosen.SourceLanguage, chosen.DisplayTerm, chosenV1);
                        EnsureAssignmentLocal(
                            variantId, BackupCardDirection.MeaningToTerm, isPreferred: true, chosenV1,
                            DirectionBoundary(BackupCardDirection.MeaningToTerm));
                    }
                }

                if (directionsPresent.Contains(BackupCardDirection.TermToMeaning))
                {
                    var chosen = Schema8SemanticUpgradePolicy.SelectPreferredCandidate(group, r => r.Translation);
                    if (chosen is not null)
                    {
                        var chosenV1 = rowToV1[chosen.Id];
                        var variantId = GetOrCreateVariant(chosen.ExplanationLanguage, chosen.Translation, chosenV1);
                        EnsureAssignmentLocal(
                            variantId, BackupCardDirection.TermToMeaning, isPreferred: true, chosenV1,
                            DirectionBoundary(BackupCardDirection.TermToMeaning));
                    }
                }

                // Losslessness: every remaining distinct answer expression (every Meaning's DisplayTerm,
                // Translation, and every accepted alias — aliases are always MeaningToTerm-only,
                // AcceptedOnly) becomes a real, usable AnswerVariant. Ascending source-order iteration
                // guarantees first-writer-wins provenance.
                foreach (var row in group)
                {
                    var v1 = rowToV1[row.Id];
                    if (directionsPresent.Contains(BackupCardDirection.MeaningToTerm) && !string.IsNullOrEmpty(row.DisplayTerm))
                    {
                        var variantId = GetOrCreateVariant(row.SourceLanguage, row.DisplayTerm, v1);
                        EnsureAssignmentLocal(variantId, BackupCardDirection.MeaningToTerm, isPreferred: false, v1);
                    }

                    if (directionsPresent.Contains(BackupCardDirection.TermToMeaning) && !string.IsNullOrEmpty(row.Translation))
                    {
                        var variantId = GetOrCreateVariant(row.ExplanationLanguage, row.Translation, v1);
                        EnsureAssignmentLocal(variantId, BackupCardDirection.TermToMeaning, isPreferred: false, v1);
                    }

                    if (directionsPresent.Contains(BackupCardDirection.MeaningToTerm))
                    {
                        foreach (var alias in v1.AcceptedAliases.Distinct(StringComparer.Ordinal))
                        {
                            if (string.IsNullOrWhiteSpace(alias))
                            {
                                continue;
                            }

                            var variantId = GetOrCreateVariant(row.SourceLanguage, alias, v1);
                            EnsureAssignmentLocal(variantId, BackupCardDirection.MeaningToTerm, isPreferred: false, v1);
                        }
                    }
                }
            }
        }

        foreach (var card in payload.Learning.Cards)
        {
            if (!cardSenseDirection.TryGetValue(card.Id, out var senseDirection))
            {
                // Unreachable for a validated v1 payload (every card's PreparedItemId resolves to a
                // Meaning, which is always assigned to exactly one Sense above) — defensive only.
                continue;
            }

            cardsOut.Add(new BackupLearningCardV2(
                card.Id, card.VocabularyId, senseDirection.SenseId, card.PreparedItemId, card.Direction, card.State,
                card.DueAtUtc, card.IntervalDays, card.EaseFactor, card.SuccessfulReviewCount, card.LapseCount,
                card.LastReviewedAtUtc, card.LastRating, card.CreatedAtUtc, card.UpdatedAtUtc));
        }

        var reviewsOut = payload.Learning.ReviewEvents.Select(review =>
        {
            string? targetVariantId = null;
            if (cardSenseDirection.TryGetValue(review.CardId, out var senseDirection))
            {
                directionVariantId.TryGetValue((senseDirection.SenseId, senseDirection.Direction), out targetVariantId);
            }

            var matched = review.WasCorrect && review.WasTypedAnswer ? targetVariantId : null;
            return new BackupLearningReviewV2(
                review.CardId, review.LearningSessionId, review.Rating, review.WasTypedAnswer, review.WasCorrect,
                review.ReviewedAtUtc, review.DueAtUtc, review.IntervalDays, review.EaseFactor, targetVariantId, matched);
        }).ToList();

        var learningWorkflowsOut = payload.Workflows.LearningSessions.Select(session =>
        {
            var queueItems = session.QueueItems.Select(queueItem =>
            {
                string? targetVariantId = null;
                if (cardSenseDirection.TryGetValue(queueItem.CardId, out var senseDirection))
                {
                    directionVariantId.TryGetValue((senseDirection.SenseId, senseDirection.Direction), out targetVariantId);
                }

                return new BackupLearningQueueItemV2(
                    queueItem.Id, queueItem.CardId, queueItem.QueueOrder, queueItem.IsDueCard, queueItem.IsAgainRepeat,
                    queueItem.AnswerRevealed, queueItem.SpellingChecked, queueItem.SpellingCorrect, queueItem.IsCompleted,
                    queueItem.Rating, queueItem.CompletedAtUtc, targetVariantId);
            }).ToList();

            return new BackupLearningWorkflowV2(
                session.Id, session.Status, session.TotalCards, session.CompletedCards, session.AgainCount,
                session.HardCount, session.GoodCount, session.EasyCount, session.StartedAtUtc, session.UpdatedAtUtc,
                session.CompletedAtUtc, queueItems);
        }).ToList();

        foreach (var vocab in payload.Vocabulary)
        {
            var automatic = vocab.AutomaticLearning;
            var hasNonDefaultState =
                automatic.ConsecutiveRecallSuccessCount != 0
                || automatic.ConsecutiveTypingSuccessCount != 0
                || automatic.ConsecutiveTypingFailureCount != 0
                || automatic.MasteryReviewExtensionScheduled;
            if (!hasNonDefaultState || !cardsByVocab.TryGetValue(vocab.Id, out var vocabCards))
            {
                continue;
            }

            foreach (var card in vocabCards)
            {
                if (!cardSenseDirection.TryGetValue(card.Id, out var senseDirection)
                    || !directionVariantId.TryGetValue((senseDirection.SenseId, senseDirection.Direction), out var variantId))
                {
                    continue;
                }

                progressOut.Add(new BackupAnswerVariantProgress(
                    card.Id, variantId, automatic.InteractionMode, automatic.ConsecutiveRecallSuccessCount,
                    automatic.ConsecutiveTypingSuccessCount, automatic.ConsecutiveTypingFailureCount,
                    vocab.UpdatedAtUtc, automatic.MasteryReviewExtensionScheduled, IsMastered: false, ReplayVersion: 1,
                    vocab.CreatedAtUtc, vocab.UpdatedAtUtc));
            }
        }

        return new BackupPayloadV2(
            payload.SourceMaterials,
            vocabularyOut,
            sensesOut,
            preparedOut,
            answerVariantsOut,
            assignmentsOut,
            progressOut,
            new BackupLearningDataV2(cardsOut, reviewsOut),
            new BackupWorkflowDataV2(payload.Workflows.VocabularyReviews, payload.Workflows.PreparationBatches, learningWorkflowsOut),
            [],
            payload.Extensions);
    }

    private static BackupVocabularyItem DowngradeKnowledgeStateIfNeeded(BackupVocabularyItem vocab) =>
        vocab.KnowledgeState is BackupKnowledgeState.Prepared or BackupKnowledgeState.Learning or BackupKnowledgeState.Mastered
            ? vocab with { KnowledgeState = BackupKnowledgeState.UnknownBacklog }
            : vocab;

    private static BackupSenseStatus TranslateInitialSenseStatus(BackupKnowledgeState status) => status switch
    {
        BackupKnowledgeState.Prepared => BackupSenseStatus.Prepared,
        BackupKnowledgeState.Learning => BackupSenseStatus.Learning,
        BackupKnowledgeState.Mastered => BackupSenseStatus.Mastered,
        _ => BackupSenseStatus.Prepared
    };

    private static VocabularyIdentity ComputeVocabularyIdentity(BackupVocabularyItem vocab)
    {
        var tokenKind = BackupEnumMappings.ToPersistence(vocab.TokenKind);
        var resolution = VocabularyIdentityPolicy.Resolve(vocab.CanonicalTerm, tokenKind, vocab.Language);
        return VocabularyMergeIdentityPolicy.Compute(vocab.Language, resolution.Identity);
    }
}
