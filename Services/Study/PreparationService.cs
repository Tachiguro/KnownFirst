using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.Lexical;
using SQLite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KnownFirst.Services.Study;

public sealed partial class PreparationService(
    IKnownFirstDatabase database,
    ILexicalEnrichmentService lexicalEnrichment,
    IClock clock,
    ILexicalDiagnosticLog? diagnosticLog = null,
    IPreparationFaultInjector? faultInjector = null) : IPreparationService
{
    private const int MaximumContextSnapshots = 3;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _prefetchSync = new();
    private readonly ILexicalDiagnosticLog _diagnosticLog =
        diagnosticLog ?? NullLexicalDiagnosticLog.Instance;
    private CancellationTokenSource? _prefetchCancellation;
    private Task<PrefetchedLookup?>? _prefetchTask;
    private int? _prefetchOriginCandidateId;

    /// <summary>
    /// Widens every Schema-8-specific dispatch below to also accept Schema 9 (index-only activation; the
    /// preparation-relevant data model is unchanged). Returns a Schema-8 capability proof object usable by
    /// the unchanged Schema8-named helpers in either case — a freshly resolved one for an actual Schema-8
    /// database, or a fresh equivalent proof object for a Schema-9 database, since both share the exact
    /// same physical shape for every table preparation touches.
    /// </summary>
    private static ValidatedPreparationSchema8Capability? AsSchema8CompatibleCapability(
        PreparationSchemaCapabilityResult capability) =>
        capability switch
        {
            PreparationSchema8CapabilityResult schema8 => schema8.Capability,
            PreparationSchema9CapabilityResult => new ValidatedPreparationSchema8Capability(),
            PreparationSchema10CapabilityResult => new ValidatedPreparationSchema8Capability(),
            PreparationSchema11CapabilityResult => new ValidatedPreparationSchema8Capability(),
            _ => null
        };
#if DEBUG
    private const int MaximumTimingMeasurements = 200;
    private readonly object _timingSync = new();
    private readonly List<PreparationTimingMeasurement> _timingMeasurements = [];
    private long _timingSequence;
#endif

    public Task<PreparationOverview> GetOverviewAsync() => database.ReadAsync(async connection =>
    {
        var now = clock.UtcNow;
        var words = await connection.Table<WordEntity>().ToListAsync();
        var active = await connection.Table<PreparationSessionEntity>()
            .Where(session => session.Status == PreparationSessionStatus.Active)
            .FirstOrDefaultAsync();
        var latestCompleted = await connection.Table<PreparationSessionEntity>()
            .Where(session => session.Status == PreparationSessionStatus.Completed)
            .OrderByDescending(session => session.Id)
            .FirstOrDefaultAsync();
        var lastCompletedPreparedItems = latestCompleted is null
            ? 0
            : await connection.Table<PreparationCandidateEntity>()
                .Where(candidate => candidate.SessionId == latestCompleted.Id
                    && candidate.Status == PreparationCandidateStatus.Prepared)
                .CountAsync();
        var dueCardCount = await connection.Table<LearningCardEntity>()
            .Where(card => card.State != CardState.New
                && card.State != CardState.Suspended
                && card.State != CardState.Retired
                && card.DueAtUtc <= now)
            .CountAsync();
        var preparedNewWordIds = (await connection.Table<LearningCardEntity>()
                .Where(card => card.State == CardState.New)
                .ToListAsync())
            .Select(card => card.WordId)
            .Distinct()
            .Count();
        var unprepared = words.Count(word => word.Status == WordStatus.UnknownBacklog
            && word.PreparationState != PreparationState.Prepared);
        return new PreparationOverview(
            unprepared,
            preparedNewWordIds,
            dueCardCount,
            active?.Id,
            active?.CompletedItems ?? 0,
            active?.TotalItems ?? 0,
            active?.Method,
            lastCompletedPreparedItems);
    });

    public async Task<int> StartAsync(PreparationMethod method, int requestedLimit)
    {
        await _operationGate.WaitAsync();
        try
        {
            return await database.RunInTransactionAsync(connection =>
            {
                if (connection.Table<ReviewSessionEntity>()
                    .Any(session => session.Status == ReviewSessionStatus.Active))
                {
                    throw new ActiveReviewExistsException();
                }

                var active = connection.Table<PreparationSessionEntity>()
                    .FirstOrDefault(session => session.Status == PreparationSessionStatus.Active);
                if (active is not null)
                {
                    return active.Id;
                }

                // KF-MEANING-001 Slice 3: Schema 7 keeps the unchanged legacy selection path below
                // byte-for-byte; Schema 8 dispatches to StartSchema8 (PreparationServiceSchema8Start.cs),
                // which never queries Schema-8-only structures until this branch is actually taken.
                var capability = PreparationSchemaCapability.Resolve(connection);
                if (AsSchema8CompatibleCapability(capability) is { } schema8StartCapability)
                {
                    return StartSchema8(connection, method, requestedLimit, schema8StartCapability);
                }

                var preparedWordIds = connection.Table<MeaningEntity>()
                    .Where(meaning => meaning.ConfirmedByUser)
                    .ToList()
                    .Select(meaning => meaning.WordId)
                    .ToHashSet();
                var selected = PreparationSelectionPolicy.Select(
                    connection.Table<WordEntity>()
                        .ToList()
                        .Select(word => new PreparationSelectionCandidate(
                            word.Id,
                            word.CanonicalTerm,
                            word.TotalOccurrenceCount,
                            word.CreatedAt,
                            word.Status == WordStatus.UnknownBacklog,
                            word.PreparationState,
                            ReviewIsResolved(connection, word.Id),
                            preparedWordIds.Contains(word.Id))),
                    requestedLimit);
                if (selected.Count == 0)
                {
                    return 0;
                }

                var now = clock.UtcNow;
                var session = new PreparationSessionEntity
                {
                    Status = PreparationSessionStatus.Active,
                    Method = method,
                    TotalItems = selected.Count,
                    StartedAtUtc = now,
                    UpdatedAtUtc = now
                };
                connection.Insert(session);
                for (var index = 0; index < selected.Count; index++)
                {
                    var selectedWord = selected[index];
                    connection.Insert(new PreparationCandidateEntity
                    {
                        SessionId = session.Id,
                        WordId = selectedWord.WordId,
                        Order = index,
                        Status = PreparationCandidateStatus.Pending,
                        UpdatedAtUtc = now
                    });
                    var word = connection.Find<WordEntity>(selectedWord.WordId)
                        ?? throw new InvalidOperationException("A selected preparation word is missing.");
                    word.PreparationState = PreparationState.Preparing;
                    word.UpdatedAt = now;
                    connection.Update(word);
                }

                return session.Id;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PreparationItem?> GetCurrentAsync()
    {
        // KF-MEANING-001 Slice 3 (§6): a genuine EnvelopeV1 is left byte-identical; an active
        // Empty/LegacyLexicalResult/malformed/unsupported Schema-8 candidate is upgraded (or fails)
        // transactionally before it is exposed to the caller. Schema-7 databases are never touched here.
        var pendingCandidateId = await FindActiveCandidateIdAsync();
        if (pendingCandidateId is int candidateIdToUpgrade)
        {
            await EnsureSchema8CandidateUpgradedAsync(candidateIdToUpgrade);
        }

        return await database.ReadAsync(async connection =>
        {
            var session = await connection.Table<PreparationSessionEntity>()
                .Where(item => item.Status == PreparationSessionStatus.Active)
                .FirstOrDefaultAsync();
            if (session is null)
            {
                return null;
            }

            var queryStarted = Stopwatch.GetTimestamp();
            var candidate = await FindCurrentCandidateAsync(connection, session.Id);
            RecordTiming(candidate?.Id, "Get current", PreparationTimingPhase.NextCandidateQuery, queryStarted);
            if (candidate is null)
            {
                return null;
            }

            var contextStarted = Stopwatch.GetTimestamp();
            var item = await CreateItemAsync(connection, session, candidate);
            RecordTiming(candidate.Id, "Get current", PreparationTimingPhase.ContextLoading, contextStarted);
            return item;
        });
    }

    private Task<int?> FindActiveCandidateIdAsync() => database.ReadAsync(async connection =>
    {
        var session = await connection.Table<PreparationSessionEntity>()
            .Where(item => item.Status == PreparationSessionStatus.Active)
            .FirstOrDefaultAsync();
        if (session is null)
        {
            return (int?)null;
        }

        var candidate = await FindCurrentCandidateAsync(connection, session.Id);
        return candidate?.Id;
    });

    /// <summary>
    /// Lazy Schema-8 envelope upgrade (KF-MEANING-001 Slice 3 §6), safe to call from a read-only code path
    /// (<see cref="GetCurrentAsync"/>/<see cref="LookupCurrentAsync"/>): a cheap read-only peek decides
    /// whether anything needs to change, and only opens a transaction when it does. A genuine EnvelopeV1 is
    /// never touched. A Schema-7 database is never touched (the transaction re-checks capability itself
    /// before doing anything, closing the TOCTOU gap between the peek and the transaction).
    /// </summary>
    private async Task EnsureSchema8CandidateUpgradedAsync(int candidateId)
    {
        var needsUpgrade = await database.ReadAsync(async connection =>
        {
            var candidate = await connection.FindAsync<PreparationCandidateEntity>(candidateId);
            return candidate is not null
                && PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Kind != PreparationCandidatePayloadKind.EnvelopeV1;
        });
        if (!needsUpgrade)
        {
            return;
        }

        await database.RunInTransactionAsync(connection =>
        {
            var capability = PreparationSchemaCapability.Resolve(connection);
            if (AsSchema8CompatibleCapability(capability) is null)
            {
                return true;
            }

            var candidate = connection.Find<PreparationCandidateEntity>(candidateId);
            if (candidate is null)
            {
                return true;
            }

            EnsureCandidateEnvelopeAndSelection(connection, candidate);
            return true;
        });
    }

    /// <summary>
    /// Ensures <paramref name="candidate"/>'s <c>ResultJson</c> is a valid EnvelopeV1, mutating and
    /// persisting it if necessary (KF-MEANING-001 Slice 3 §6): a genuine EnvelopeV1 is returned byte-
    /// identical and never rewritten; Empty becomes an envelope with a null Result and newly frozen
    /// evidence; a raw LegacyLexicalResult becomes an envelope wrapping that exact result plus newly frozen
    /// evidence, with <see cref="PreparationCandidateEntity.SelectedMeaningIndex"/> preserved if still valid
    /// or deterministically corrected (clamped into range, or 0 when the result has no meanings) otherwise.
    /// Unsupported/malformed data throws <see cref="PreparationCandidateStateException"/> before any write.
    /// Never performs a network lookup — only ever touches the ResultJson/SelectedMeaningIndex columns.
    /// </summary>
    private static void EnsureCandidateEnvelopeAndSelection(SQLiteConnection connection, PreparationCandidateEntity candidate)
    {
        var read = PreparationCandidatePayloadCodec.Read(candidate.ResultJson);
        switch (read.Kind)
        {
            case PreparationCandidatePayloadKind.EnvelopeV1:
                return;

            case PreparationCandidatePayloadKind.Empty:
            {
                var frozen = Schema8EvidenceScanner.SelectFrozenEvidence(connection, candidate.WordId, MaximumContextSnapshots);
                candidate.ResultJson = PreparationCandidatePayloadCodec.Write(PreparationCandidatePayloadV1.CreatePending(frozen));
                candidate.SelectedMeaningIndex = 0;
                connection.Update(candidate);
                return;
            }

            case PreparationCandidatePayloadKind.LegacyLexicalResult:
            {
                var frozen = Schema8EvidenceScanner.SelectFrozenEvidence(connection, candidate.WordId, MaximumContextSnapshots);
                var legacyResult = read.LegacyResult!;
                var correctedIndex = legacyResult.Meanings.Count == 0
                    ? 0
                    : Math.Clamp(candidate.SelectedMeaningIndex, 0, legacyResult.Meanings.Count - 1);
                var upgraded = PreparationCandidatePayloadV1.Create(legacyResult, frozenEvidence: frozen);
                candidate.ResultJson = PreparationCandidatePayloadCodec.Write(upgraded);
                candidate.SelectedMeaningIndex = correctedIndex;
                connection.Update(candidate);
                return;
            }

            case PreparationCandidatePayloadKind.UnsupportedEnvelopeVersion:
                throw new PreparationCandidateStateException(
                    "preparation-candidate-unsupported-envelope-version",
                    $"Preparation candidate {candidate.Id}'s ResultJson envelope version {read.UnsupportedVersion} is not supported.");

            default:
                throw new PreparationCandidateStateException(
                    "preparation-candidate-malformed",
                    $"Preparation candidate {candidate.Id}'s ResultJson is malformed: {read.FailureDetail}");
        }
    }

    public async Task<PreparationItem?> LookupCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var item = await GetLookupItemAsync();
            if (item is null)
            {
                return null;
            }

            if (item.Status == PreparationCandidateStatus.ResultReady
                && item.Result?.HasUsableData == true)
            {
                return item;
            }

            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(item.CandidateId)
                    ?? throw new InvalidOperationException("The preparation candidate no longer exists.");
                EnsureCurrentCandidate(connection, candidate);
                candidate.Status = PreparationCandidateStatus.Pending;
                candidate.LookupAttemptCount++;
                candidate.LastErrorCode = string.Empty;
                candidate.UpdatedAtUtc = clock.UtcNow;
                connection.Update(candidate);
                var word = connection.Find<WordEntity>(candidate.WordId)!;
                word.PreparationState = PreparationState.Preparing;
                connection.Update(word);
                return true;
            });

            var result = await TryConsumePrefetchAsync(item.CandidateId, cancellationToken);
            if (result is null)
            {
                var documentContent = await GetDocumentContentAsync(item.WordId);
                var networkStarted = Stopwatch.GetTimestamp();
                _diagnosticLog.Write(DiagnosticEvent(item, "preparation.request.start"));
                var request = CreateLookupRequest(item);
                _diagnosticLog.Write(DiagnosticEvent(item, "preparation.request.complete"));
                result = await lexicalEnrichment.EnrichAsync(
                    request,
                    documentContent,
                    item.Contexts.FirstOrDefault()?.Text,
                    cancellationToken);
                RecordTiming(
                    item.CandidateId,
                    "Lookup",
                    PreparationTimingPhase.NetworkWork,
                    networkStarted);
            }
            var persisted = await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(item.CandidateId)
                    ?? throw new InvalidOperationException("The preparation candidate no longer exists.");
                EnsureCurrentCandidate(connection, candidate);
                _diagnosticLog.Write(DiagnosticEvent(item, "preparation.result-serialize.start"));
                // KF-MEANING-001 Slice 3: a Schema-8 database merges the provider result into the
                // already-frozen envelope (evidence recorded at StartAsync/lazy-upgrade time is never
                // replaced here); Schema-7 output is byte-for-byte unchanged.
                var capability = PreparationSchemaCapability.Resolve(connection);
                var isSchema8Compatible = AsSchema8CompatibleCapability(capability) is not null;
                var now = clock.UtcNow;
                var word = connection.Find<WordEntity>(candidate.WordId)!;
                if (isSchema8Compatible && result.HasUsableData)
                {
                    // §9: every provider index that already matches an existing Sense is auto-resolved
                    // right away — never requiring an explicit accept first — and the candidate
                    // auto-completes immediately if that resolves every index.
                    var merged = MergeResultIntoEnvelope(candidate.ResultJson, result);
                    var (autoResolved, nextIndex, isFullyResolved) = AutoResolveExactVariantsAfterLookup(connection, word, merged);
                    candidate.ResultJson = PreparationCandidatePayloadCodec.Write(autoResolved);
                    candidate.SelectedMeaningIndex = nextIndex;
                    candidate.LastErrorCode = string.Empty;
                    candidate.UpdatedAtUtc = now;
                    if (isFullyResolved)
                    {
                        var session = connection.Find<PreparationSessionEntity>(candidate.SessionId)!;
                        word.PreparationState = PreparationState.Prepared;
                        word.UpdatedAt = now;
                        connection.Update(word);
                        connection.Update(candidate);
                        CompleteCandidate(connection, session, candidate, PreparationCandidateStatus.Prepared, now);
                        _diagnosticLog.Write(DiagnosticEvent(item, "preparation.result-serialize.complete"));
                        return (candidate.Status, candidate.SelectedMeaningIndex);
                    }

                    candidate.Status = PreparationCandidateStatus.ResultReady;
                    word.PreparationState = PreparationState.Preparing;
                    word.UpdatedAt = now;
                    connection.Update(word);
                }
                else
                {
                    candidate.ResultJson = isSchema8Compatible
                        ? PreparationCandidatePayloadCodec.Write(MergeResultIntoEnvelope(candidate.ResultJson, result))
                        : JsonSerializer.Serialize(result, LexicalJsonSerializerContext.Default.LexicalResult);
                    candidate.SelectedMeaningIndex = 0;
                    candidate.Status = result.HasUsableData
                        ? PreparationCandidateStatus.ResultReady
                        : PreparationCandidateStatus.Failed;
                    candidate.LastErrorCode = result.ErrorCode ?? string.Empty;
                    word.PreparationState = result.HasUsableData
                        ? PreparationState.Preparing
                        : PreparationState.PreparationFailed;
                    word.UpdatedAt = now;
                    connection.Update(word);
                }

                _diagnosticLog.Write(DiagnosticEvent(item, "preparation.result-serialize.complete"));
                candidate.UpdatedAtUtc = now;
                connection.Update(candidate);
                return (candidate.Status, candidate.SelectedMeaningIndex);
            });

            var updated = item with
            {
                Status = persisted.Status,
                Result = result,
                SelectedMeaningIndex = persisted.SelectedMeaningIndex,
                LastErrorCode = result.ErrorCode
            };
            if (result.HasUsableData)
            {
                BeginPrefetch(item);
            }

            return updated;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SelectMeaningAsync(int candidateId, int meaningIndex)
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
                    ?? throw new InvalidOperationException("The preparation candidate does not exist.");
                EnsureCurrentCandidate(connection, candidate);

                // KF-MEANING-001 Slice 3 (§6/§7): lazy-upgrade before selection, then reject an
                // already-resolved index (Schema-8 only; Schema-7 never carries a resolved-index ledger).
                var capability = PreparationSchemaCapability.Resolve(connection);
                if (AsSchema8CompatibleCapability(capability) is not null)
                {
                    EnsureCandidateEnvelopeAndSelection(connection, candidate);
                    var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Envelope;
                    if (envelope?.ResolvedProviderMeaningIndexes.Contains(meaningIndex) == true)
                    {
                        throw new InvalidOperationException(
                            $"Provider meaning index {meaningIndex} has already been resolved for this candidate.");
                    }
                }

                var result = DeserializeResult(candidate.ResultJson)
                    ?? throw new InvalidOperationException("The preparation candidate has no lexical result.");
                if (meaningIndex < 0 || meaningIndex >= result.Meanings.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(meaningIndex));
                }

                candidate.SelectedMeaningIndex = meaningIndex;
                candidate.UpdatedAtUtc = clock.UtcNow;
                connection.Update(candidate);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task AcceptAsync(
        int candidateId,
        PreparedMeaningInput input,
        CardDirectionPreference cardDirectionPreference)
    {
        var validationStarted = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.AcronymExpansion)
            && string.IsNullOrWhiteSpace(input.Translation)
            && string.IsNullOrWhiteSpace(input.Definition))
        {
            throw new ArgumentException(
                "An acronym expansion, translation, or definition is required.",
                nameof(input));
        }

        RecordTiming(candidateId, "Accept", PreparationTimingPhase.Validation, validationStarted);

        await _operationGate.WaitAsync();
        try
        {
            var transactionStarted = Stopwatch.GetTimestamp();
            await database.RunInTransactionAsync(connection =>
            {
                // KF-MEANING-001 Slice 3: schema capability is resolved before any mutation, fail-closed,
                // via the preparation-specific PreparationSchemaCapability (validated PRAGMA user_version +
                // Schema8ShapeValidator) — deliberately independent of the backup subsystem's
                // BackupSchemaCapability. The Schema-7 branch below is otherwise byte-for-byte the
                // pre-Slice-3 behavior; the Schema-8 branch lives entirely in PreparationServiceSchema8.cs.
                var capability = PreparationSchemaCapability.Resolve(connection);
                return AsSchema8CompatibleCapability(capability) is { } schema8AcceptCapability
                    ? AcceptSchema8(connection, candidateId, input, cardDirectionPreference, schema8AcceptCapability)
                    : AcceptSchema7(connection, candidateId, input, cardDirectionPreference);
            });
            RecordTiming(
                candidateId,
                "Accept",
                PreparationTimingPhase.DatabaseTransaction,
                transactionStarted);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Strict Schema-7 behavior preservation (KF-MEANING-001 Slice 3): extracted verbatim from the
    /// pre-Slice-3 <c>AcceptAsync</c> transaction body, unchanged. Existing candidate-selection policy,
    /// confirmed-Meaning exclusion, Prepared-state exclusion, raw <c>LexicalResult</c> ResultJson,
    /// <see cref="WordStatus.Prepared"/>/<see cref="PreparationState.Prepared"/> writes, and existing
    /// timestamp/session-count behavior are all identical to before this slice. Issues no Schema-8 SQL.
    /// </summary>
    private bool AcceptSchema7(
        SQLiteConnection connection,
        int candidateId,
        PreparedMeaningInput input,
        CardDirectionPreference cardDirectionPreference)
    {
        // KF-MEANING-001 Slice 3 (§8): Schema 7 validates the same TopicOrDomain/PartOfSpeech API bounds as
        // Schema 8, before any mutation, but never persists them and never changes legacy rows/shape.
        PreparationMetadataPolicy.NormalizeTopicOrDomain(input.TopicOrDomain);
        PreparationMetadataPolicy.NormalizePartOfSpeech(input.PartOfSpeech);

        var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
            ?? throw new InvalidOperationException("The preparation candidate does not exist.");
        EnsureCurrentCandidate(connection, candidate);
        var session = connection.Find<PreparationSessionEntity>(candidate.SessionId)
            ?? throw new InvalidOperationException("The preparation session does not exist.");
        var word = connection.Find<WordEntity>(candidate.WordId)
            ?? throw new InvalidOperationException("The preparation word does not exist.");
        if (connection.Table<MeaningEntity>()
            .Any(meaning => meaning.WordId == word.Id && meaning.ConfirmedByUser))
        {
            throw new InvalidOperationException("This vocabulary item is already prepared.");
        }

        var contextStarted = Stopwatch.GetTimestamp();
        var contextData = BuildContextData(connection, word.Id);
        RecordTiming(candidateId, "Accept", PreparationTimingPhase.ContextLoading, contextStarted);
        var explanationLanguage = contextData.FirstOrDefault()?.ExplanationLanguage ?? word.Language;
        var now = clock.UtcNow;
        var preparedTokenKind = !string.IsNullOrWhiteSpace(input.AcronymExpansion)
            && AcronymExpansionDetector.IsAcronymCandidate(word.CanonicalTerm)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : word.TokenKind;
        var aliases = input.AcceptedAliases
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var meaningSaveStarted = Stopwatch.GetTimestamp();
        var meaning = new MeaningEntity
        {
            WordId = word.Id,
            SourceLanguage = word.Language,
            ExplanationLanguage = explanationLanguage,
            DisplayTerm = string.IsNullOrWhiteSpace(input.CanonicalLearningTerm)
                ? word.CanonicalTerm
                : input.CanonicalLearningTerm.Trim(),
            EncounteredSurfaceForm = input.EncounteredSurfaceForm?.Trim() ?? string.Empty,
            GrammaticalRelationship = input.GrammaticalRelationship?.Trim() ?? string.Empty,
            TokenKind = preparedTokenKind,
            SelectedMeaningId = input.SelectedMeaningId ?? string.Empty,
            AcronymExpansion = input.AcronymExpansion?.Trim() ?? string.Empty,
            Translation = input.Translation?.Trim() ?? string.Empty,
            Definition = input.Definition.Trim(),
            DictionaryExample = input.DictionaryExample?.Trim() ?? string.Empty,
            AdditionalNote = input.AdditionalNote?.Trim() ?? string.Empty,
            AcceptedAliasesJson = JsonSerializer.Serialize(
                aliases,
                LexicalJsonSerializerContext.Default.StringArray),
            TranslationOrDefinition = !string.IsNullOrWhiteSpace(input.Translation)
                ? input.Translation.Trim()
                : !string.IsNullOrWhiteSpace(input.Definition)
                    ? input.Definition.Trim()
                    : input.AcronymExpansion!.Trim(),
            Source = input.ProviderName,
            SourceProject = input.SourceProject,
            SourcePageTitle = input.SourcePageTitle,
            SourceRevisionId = input.SourceRevisionId,
            Attribution = input.Attribution,
            ConfirmedByUser = true,
            CreatedAt = now,
            UpdatedAt = now,
            PreparedAt = now
        };
        connection.Insert(meaning);
        foreach (var context in contextData.Take(MaximumContextSnapshots))
        {
            connection.Insert(new ContextSnapshotEntity
            {
                MeaningId = meaning.Id,
                WordId = word.Id,
                SourceDocumentId = context.DocumentId,
                SourceDocumentTitle = context.DocumentTitle,
                Text = context.Text,
                TargetStart = context.TargetStart,
                TargetLength = context.TargetLength,
                NormalizedFingerprint = CreateFingerprint(NormalizeContext(context.Text)),
                CreatedAtUtc = now
            });
        }

        RecordTiming(
            candidateId,
            "Accept",
            PreparationTimingPhase.PreparedMeaningSave,
            meaningSaveStarted);

        var cardCreationStarted = Stopwatch.GetTimestamp();
        foreach (var direction in CardDirectionPreferencePolicy.GetDirections(cardDirectionPreference))
        {
            connection.Insert(new LearningCardEntity
            {
                WordId = word.Id,
                MeaningId = meaning.Id,
                Direction = direction,
                State = CardState.New,
                DueAtUtc = now,
                EaseFactor = SimpleSpacedRepetitionScheduler.DefaultEaseFactor,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        RecordTiming(
            candidateId,
            "Accept",
            PreparationTimingPhase.LearningCardCreation,
            cardCreationStarted);

        var sessionUpdateStarted = Stopwatch.GetTimestamp();
        word.TokenKind = preparedTokenKind;
        word.Status = WordStatus.Prepared;
        word.PreparationState = PreparationState.Prepared;
        word.UpdatedAt = now;
        connection.Update(word);
        CompleteCandidate(connection, session, candidate, PreparationCandidateStatus.Prepared, now);
        RecordTiming(
            candidateId,
            "Accept",
            PreparationTimingPhase.SessionUpdate,
            sessionUpdateStarted);
        return true;
    }

    public async Task SkipAsync(int candidateId)
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
                    ?? throw new InvalidOperationException("The preparation candidate does not exist.");
                EnsureCurrentCandidate(connection, candidate);
                var session = connection.Find<PreparationSessionEntity>(candidate.SessionId)!;
                var word = connection.Find<WordEntity>(candidate.WordId)!;
                word.PreparationState = PreparationState.Unprepared;
                word.UpdatedAt = clock.UtcNow;
                connection.Update(word);
                CompleteCandidate(
                    connection,
                    session,
                    candidate,
                    PreparationCandidateStatus.Skipped,
                    clock.UtcNow);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> CancelActiveSessionAsync()
    {
        await CancelPrefetchAsync();
        await _operationGate.WaitAsync();
        try
        {
            return await database.RunInTransactionAsync(connection =>
            {
                var session = connection.Table<PreparationSessionEntity>()
                    .FirstOrDefault(item => item.Status == PreparationSessionStatus.Active);
                if (session is null)
                {
                    return false;
                }

                var now = clock.UtcNow;
                var retainedCompletedItems = 0;
                var candidates = connection.Table<PreparationCandidateEntity>()
                    .Where(candidate => candidate.SessionId == session.Id)
                    .ToList();
                foreach (var candidate in candidates)
                {
                    if (candidate.Status is PreparationCandidateStatus.Prepared
                        or PreparationCandidateStatus.MarkedKnown
                        or PreparationCandidateStatus.Excluded)
                    {
                        retainedCompletedItems++;
                        continue;
                    }

                    var word = connection.Find<WordEntity>(candidate.WordId);
                    if (word?.Status == WordStatus.UnknownBacklog)
                    {
                        word.PreparationState = PreparationState.Unprepared;
                        word.UpdatedAt = now;
                        connection.Update(word);
                    }

                    candidate.Status = PreparationCandidateStatus.Cancelled;
                    candidate.ResultJson = string.Empty;
                    candidate.SelectedMeaningIndex = 0;
                    candidate.LastErrorCode = string.Empty;
                    candidate.LookupAttemptCount = 0;
                    candidate.UpdatedAtUtc = now;
                    connection.Update(candidate);
                }

                session.Status = PreparationSessionStatus.Cancelled;
                session.CompletedItems = retainedCompletedItems;
                session.UpdatedAtUtc = now;
                session.CompletedAtUtc = now;
                connection.Update(session);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task MarkKnownAsync(int candidateId) =>
        CompleteWithoutLearningAsync(
            candidateId,
            WordStatus.Known,
            PreparationCandidateStatus.MarkedKnown);

    public Task ExcludeAsync(int candidateId) =>
        CompleteWithoutLearningAsync(
            candidateId,
            WordStatus.Ignored,
            PreparationCandidateStatus.Excluded);

    public async Task CancelPrefetchAsync()
    {
        CancellationTokenSource? cancellation;
        Task<PrefetchedLookup?>? task;
        lock (_prefetchSync)
        {
            cancellation = _prefetchCancellation;
            task = _prefetchTask;
            _prefetchCancellation = null;
            _prefetchTask = null;
            _prefetchOriginCandidateId = null;
        }

        cancellation?.Cancel();
        if (task is not null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

#if DEBUG
    public IReadOnlyList<PreparationTimingMeasurement> GetTimingDiagnostics()
    {
        lock (_timingSync)
        {
            return _timingMeasurements.ToArray();
        }
    }

    public void RecordUiTransition(int? candidateId, TimeSpan elapsed) => RecordTiming(
        candidateId,
        "Accept to next item",
        PreparationTimingPhase.UiTransition,
        elapsed);
#endif

    private async Task CompleteWithoutLearningAsync(
        int candidateId,
        WordStatus finalWordStatus,
        PreparationCandidateStatus finalCandidateStatus)
    {
        await _operationGate.WaitAsync();
        try
        {
            await database.RunInTransactionAsync(connection =>
            {
                var candidate = connection.Find<PreparationCandidateEntity>(candidateId)
                    ?? throw new InvalidOperationException("The preparation candidate does not exist.");
                EnsureCurrentCandidate(connection, candidate);
                var session = connection.Find<PreparationSessionEntity>(candidate.SessionId)
                    ?? throw new InvalidOperationException("The preparation session does not exist.");
                var word = connection.Find<WordEntity>(candidate.WordId)
                    ?? throw new InvalidOperationException("The preparation word does not exist.");
                if (word.Status != WordStatus.UnknownBacklog
                    || connection.Table<MeaningEntity>().Any(meaning => meaning.WordId == word.Id)
                    || connection.Table<LearningCardEntity>().Any(card => card.WordId == word.Id))
                {
                    throw new InvalidOperationException("Only unprepared Unknown vocabulary can be completed without learning.");
                }

                connection.Execute("DELETE FROM ContextSnapshots WHERE WordId = ?", word.Id);
                connection.Execute("DELETE FROM WordOccurrences WHERE WordId = ?", word.Id);
                connection.Execute("DELETE FROM WordForms WHERE WordId = ?", word.Id);
                connection.Execute("DELETE FROM ReviewStates WHERE WordId = ?", word.Id);

                var now = clock.UtcNow;
                word.Status = finalWordStatus;
                word.PreparationState = PreparationState.Unprepared;
                word.TotalOccurrenceCount = 0;
                word.DocumentCount = 0;
                word.UpdatedAt = now;
                connection.Update(word);

                candidate.ResultJson = string.Empty;
                candidate.SelectedMeaningIndex = 0;
                candidate.LastErrorCode = string.Empty;
                CompleteCandidate(connection, session, candidate, finalCandidateStatus, now);
                DocumentCleanupOperations.CleanupEligibleDocuments(connection);
                return true;
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<PreparationItem?> GetLookupItemAsync()
    {
        // KF-MEANING-001 Slice 3 (§6): same lazy-upgrade guarantee as GetCurrentAsync.
        var pendingCandidateId = await FindActiveCandidateIdAsync();
        if (pendingCandidateId is int candidateIdToUpgrade)
        {
            await EnsureSchema8CandidateUpgradedAsync(candidateIdToUpgrade);
        }

        return await database.ReadAsync(async connection =>
        {
            var session = await connection.Table<PreparationSessionEntity>()
                .Where(item => item.Status == PreparationSessionStatus.Active)
                .FirstOrDefaultAsync();
            if (session is null)
            {
                return null;
            }

            var queryStarted = Stopwatch.GetTimestamp();
            var candidate = await FindCurrentCandidateAsync(connection, session.Id);
            RecordTiming(candidate?.Id, "Lookup", PreparationTimingPhase.NextCandidateQuery, queryStarted);
            if (candidate is null)
            {
                return null;
            }

            var contextStarted = Stopwatch.GetTimestamp();
            var item = await CreateItemAsync(connection, session, candidate);
            RecordTiming(candidate.Id, "Lookup", PreparationTimingPhase.ContextLoading, contextStarted);
            return item;
        });
    }

    private Task<string> GetDocumentContentAsync(int wordId) => database.ReadAsync(
        connection => LoadDocumentContentAsync(connection, wordId));

    private static async Task<PreparationLookupSource> CreateLookupSourceAsync(
        SQLiteAsyncConnection connection,
        PreparationSessionEntity session,
        PreparationCandidateEntity candidate)
    {
        var item = await CreateItemAsync(connection, session, candidate);
        var documentContent = await LoadDocumentContentAsync(connection, candidate.WordId);
        return new PreparationLookupSource(item, documentContent);
    }

    private static async Task<string> LoadDocumentContentAsync(
        SQLiteAsyncConnection connection,
        int wordId)
    {
        var documentIds = (await connection.Table<WordOccurrenceEntity>()
                .Where(occurrence => occurrence.WordId == wordId)
                .OrderBy(occurrence => occurrence.DocumentId)
                .ToListAsync())
            .Select(occurrence => occurrence.DocumentId)
            .Distinct()
            .ToArray();
        var documentContents = new List<string>(documentIds.Length);
        foreach (var documentId in documentIds)
        {
            var document = await connection.FindAsync<DocumentEntity>(documentId);
            if (document is not null)
            {
                documentContents.Add(document.Content);
            }
        }

        return string.Join('\n', documentContents);
    }

    private void BeginPrefetch(PreparationItem currentItem)
    {
        lock (_prefetchSync)
        {
            if (_prefetchOriginCandidateId == currentItem.CandidateId && _prefetchTask is not null)
            {
                return;
            }

            _prefetchCancellation?.Cancel();
            _prefetchCancellation?.Dispose();
            _prefetchCancellation = new CancellationTokenSource();
            _prefetchOriginCandidateId = currentItem.CandidateId;
            _prefetchTask = PrefetchNextAsync(
                currentItem.SessionId,
                currentItem.Position - 1,
                _prefetchCancellation.Token);
        }
    }

    private async Task<PrefetchedLookup?> PrefetchNextAsync(
        int sessionId,
        int currentOrder,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = await database.ReadAsync(async connection =>
            {
                var session = await connection.FindAsync<PreparationSessionEntity>(sessionId);
                if (session?.Status != PreparationSessionStatus.Active)
                {
                    return null;
                }

                var candidate = await connection.Table<PreparationCandidateEntity>()
                    .Where(item => item.SessionId == sessionId
                        && item.Order > currentOrder
                        && item.Status == PreparationCandidateStatus.Pending)
                    .OrderBy(item => item.Order)
                    .FirstOrDefaultAsync();
                return candidate is null
                    ? null
                    : await CreateLookupSourceAsync(connection, session, candidate);
            });
            if (source is null)
            {
                return null;
            }

            var networkStarted = Stopwatch.GetTimestamp();
            _diagnosticLog.Write(DiagnosticEvent(source.Item, "prefetch.request.start"));
            var request = CreateLookupRequest(source.Item);
            _diagnosticLog.Write(DiagnosticEvent(source.Item, "prefetch.request.complete"));
            var result = await lexicalEnrichment.EnrichAsync(
                request,
                source.DocumentContent,
                source.Item.Contexts.FirstOrDefault()?.Text,
                cancellationToken);
            RecordTiming(
                source.Item.CandidateId,
                "Prefetch",
                PreparationTimingPhase.NetworkWork,
                networkStarted);
            return new PrefetchedLookup(source.Item.CandidateId, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<LexicalResult?> TryConsumePrefetchAsync(
        int candidateId,
        CancellationToken cancellationToken)
    {
        Task<PrefetchedLookup?>? task;
        lock (_prefetchSync)
        {
            task = _prefetchTask;
        }

        if (task is null)
        {
            return null;
        }

        var prefetched = await task.WaitAsync(cancellationToken);
        if (prefetched?.CandidateId != candidateId)
        {
            return null;
        }

        lock (_prefetchSync)
        {
            if (ReferenceEquals(task, _prefetchTask))
            {
                _prefetchCancellation?.Dispose();
                _prefetchCancellation = null;
                _prefetchTask = null;
                _prefetchOriginCandidateId = null;
            }
        }

        return prefetched.Result;
    }

    private static async Task<PreparationItem> CreateItemAsync(
        SQLiteAsyncConnection connection,
        PreparationSessionEntity session,
        PreparationCandidateEntity candidate)
    {
        var word = await connection.FindAsync<WordEntity>(candidate.WordId)
            ?? throw new InvalidOperationException("The preparation word does not exist.");

        // KF-MEANING-001 Slice 3 (§4): a genuine EnvelopeV1 exposes its frozen evidence as Contexts (in
        // frozen order, so Contexts[0] is the frozen first context sent to the lexical provider);
        // Empty/LegacyLexicalResult (Schema-7, or a not-yet-upgraded Schema-8 row) keep the exact
        // pre-Slice-3 live-scan algorithm, unchanged.
        var read = PreparationCandidatePayloadCodec.Read(candidate.ResultJson);
        var (contexts, explanationLanguage, lookupMode, targetLanguage) = read.Kind == PreparationCandidatePayloadKind.EnvelopeV1
            ? await ResolveFrozenContextsAsync(connection, word, read.Envelope!.FrozenEvidence)
            : await ResolveLiveContextsAsync(connection, word);

        return new PreparationItem(
            session.Id,
            candidate.Id,
            word.Id,
            word.CanonicalTerm,
            word.TokenKind,
            word.Language,
            explanationLanguage,
            word.TotalOccurrenceCount,
            candidate.Order + 1,
            session.TotalItems,
            session.Method,
            candidate.Status,
            contexts,
            read.AnyResult,
            candidate.SelectedMeaningIndex,
            string.IsNullOrWhiteSpace(candidate.LastErrorCode) ? null : candidate.LastErrorCode,
            lookupMode,
            targetLanguage);
    }

    /// <summary>The exact pre-Slice-3 Schema-7 context-loading algorithm: first three valid occurrences,
    /// deduplicated by normalized-text fingerprint, in (DocumentId, Order) order.</summary>
    private static async Task<(List<PreparationContext> Contexts, string ExplanationLanguage, LexicalLookupMode LookupMode, string? TargetLanguage)>
        ResolveLiveContextsAsync(SQLiteAsyncConnection connection, WordEntity word)
    {
        var occurrences = await connection.Table<WordOccurrenceEntity>()
            .Where(item => item.WordId == word.Id)
            .OrderBy(item => item.DocumentId)
            .ThenBy(item => item.Order)
            .ToListAsync();
        var recognizedSurfaceForms = await LoadRecognizedSurfaceFormsAsync(connection, word.Id);
        var contexts = new List<PreparationContext>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        string explanationLanguage = word.Language;
        var lookupMode = LexicalLookupMode.Definition;
        string? targetLanguage = null;
        var lookupSettingsLoaded = false;
        foreach (var occurrence in occurrences)
        {
            var document = await connection.FindAsync<DocumentEntity>(occurrence.DocumentId);
            var sentence = await connection.FindAsync<SentenceSpanEntity>(occurrence.SentenceSpanId);
            if (document is null || sentence is null || !TryCreateContext(document, sentence, occurrence, out var context)
                || !IsAttributableToCandidate(word.Id, context.Text, context.TargetStart, context.TargetLength, recognizedSurfaceForms))
            {
                continue;
            }

            if (!lookupSettingsLoaded)
            {
                (lookupMode, targetLanguage) = ResolveLookupSettings(document);
                explanationLanguage = targetLanguage ?? document.TextLanguage;
                lookupSettingsLoaded = true;
            }
            if (fingerprints.Add(CreateFingerprint(NormalizeContext(context.Text))))
            {
                contexts.Add(context);
            }

            if (contexts.Count == MaximumContextSnapshots)
            {
                break;
            }
        }

        return (contexts, explanationLanguage, lookupMode, targetLanguage);
    }

    /// <summary>
    /// Resolves an envelope's frozen evidence into display-ready contexts (KF-MEANING-001 Slice 3 §4): a
    /// full live occurrence scan builds a key→context lookup (documents/sentences/occurrences are
    /// immutable once imported, so a frozen key always resolves to the same text/position), then the
    /// frozen evidence list is mapped in its own order — never occurrence-scan order — so Contexts[0] is
    /// always the frozen first context.
    /// </summary>
    private static async Task<(List<PreparationContext> Contexts, string ExplanationLanguage, LexicalLookupMode LookupMode, string? TargetLanguage)>
        ResolveFrozenContextsAsync(
            SQLiteAsyncConnection connection, WordEntity word, IReadOnlyList<PreparationCandidateEvidence> frozenEvidence)
    {
        var contexts = new List<PreparationContext>();
        if (frozenEvidence.Count == 0)
        {
            return (contexts, word.Language, LexicalLookupMode.Definition, null);
        }

        var occurrences = await connection.Table<WordOccurrenceEntity>()
            .Where(item => item.WordId == word.Id)
            .OrderBy(item => item.DocumentId)
            .ThenBy(item => item.Order)
            .ToListAsync();
        var recognizedSurfaceForms = await LoadRecognizedSurfaceFormsAsync(connection, word.Id);

        var byKey = new Dictionary<KnownFirst.Core.Preparation.ContextEvidenceKey, (PreparationContext Context, DocumentEntity Document)>();
        foreach (var occurrence in occurrences)
        {
            var document = await connection.FindAsync<DocumentEntity>(occurrence.DocumentId);
            var sentence = await connection.FindAsync<SentenceSpanEntity>(occurrence.SentenceSpanId);
            if (document is null || sentence is null || !TryCreateContext(document, sentence, occurrence, out var context)
                || !IsAttributableToCandidate(word.Id, context.Text, context.TargetStart, context.TargetLength, recognizedSurfaceForms))
            {
                continue;
            }

            var key = KnownFirst.Core.Preparation.PreparationContextEvidencePolicy.CreateKey(
                context.DocumentId, context.Text, context.TargetStart, context.TargetLength);
            byKey.TryAdd(key, (context, document));
        }

        string explanationLanguage = word.Language;
        var lookupMode = LexicalLookupMode.Definition;
        string? targetLanguage = null;
        var lookupSettingsLoaded = false;
        foreach (var evidence in frozenEvidence)
        {
            var key = new KnownFirst.Core.Preparation.ContextEvidenceKey(
                evidence.SourceDocumentId, evidence.NormalizedFingerprint, evidence.TargetStart, evidence.TargetLength);
            if (!byKey.TryGetValue(key, out var match))
            {
                continue;
            }

            if (!lookupSettingsLoaded)
            {
                (lookupMode, targetLanguage) = ResolveLookupSettings(match.Document);
                explanationLanguage = targetLanguage ?? match.Document.TextLanguage;
                lookupSettingsLoaded = true;
            }

            contexts.Add(match.Context);
        }

        return (contexts, explanationLanguage, lookupMode, targetLanguage);
    }

    private static List<ContextData> BuildContextData(SQLiteConnection connection, int wordId)
    {
        var result = new List<ContextData>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = connection.Table<WordOccurrenceEntity>()
            .Where(item => item.WordId == wordId)
            .OrderBy(item => item.DocumentId)
            .ThenBy(item => item.Order)
            .ToList();
        foreach (var occurrence in occurrences)
        {
            var document = connection.Find<DocumentEntity>(occurrence.DocumentId);
            var sentence = connection.Find<SentenceSpanEntity>(occurrence.SentenceSpanId);
            if (document is null || sentence is null || !TryCreateContext(document, sentence, occurrence, out var context))
            {
                continue;
            }

            var fingerprint = CreateFingerprint(NormalizeContext(context.Text));
            if (fingerprints.Add(fingerprint))
            {
                result.Add(new ContextData(
                    context.DocumentId,
                    context.DocumentTitle,
                    document.ExplanationLanguage,
                    context.Text,
                    context.TargetStart,
                    context.TargetLength));
            }

            if (result.Count == MaximumContextSnapshots)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// KF-MEANING-002 context-integrity fail-safe. Loads the existing surface-form registry
    /// (<c>WordForms</c>, populated at import/tokenization time) for one Word — the set of surface
    /// strings already recorded as belonging to this candidate.
    /// </summary>
    private static async Task<HashSet<string>> LoadRecognizedSurfaceFormsAsync(SQLiteAsyncConnection connection, int wordId) =>
        (await connection.Table<WordFormEntity>().Where(form => form.WordId == wordId).ToListAsync())
            .Select(form => form.SurfaceForm)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// KF-MEANING-002 context-integrity fail-safe. A context is attributable to the current candidate only
    /// when the exact text at its own recorded coordinates is a surface form already registered for that
    /// Word in <c>WordForms</c> — the existing registry that already supports every documented inflection,
    /// compound, abbreviation, and acronym relationship, populated once at import time. This is exact-set
    /// membership, never loose substring matching. A mismatch is logged as a bounded, content-free warning
    /// (Word id and coordinates only — never document or context text) so the caller can exclude the
    /// context instead of displaying or persisting evidence that does not belong to this candidate.
    /// </summary>
    private static bool IsAttributableToCandidate(
        int wordId, string text, int targetStart, int targetLength, IReadOnlySet<string> recognizedSurfaceForms)
    {
        if (targetStart < 0 || targetLength < 0 || targetStart + targetLength > text.Length)
        {
            return false;
        }

        var targetText = text.Substring(targetStart, targetLength);
        if (recognizedSurfaceForms.Contains(targetText))
        {
            return true;
        }

        Trace.TraceWarning(
            $"preparation.context.attribution-mismatch wordId={wordId} targetStart={targetStart} targetLength={targetLength}");
        return false;
    }

    internal static bool TryCreateContext(
        DocumentEntity document,
        SentenceSpanEntity sentence,
        WordOccurrenceEntity occurrence,
        out PreparationContext context)
    {
        var relativeStart = occurrence.StartPosition - sentence.StartPosition;
        if (relativeStart < 0
            || occurrence.Length < 0
            || relativeStart + occurrence.Length > sentence.Length
            || sentence.StartPosition + sentence.Length > document.Content.Length)
        {
            context = null!;
            return false;
        }

        var text = document.Content.Substring(sentence.StartPosition, sentence.Length);
        if (!string.Equals(
            text.Substring(relativeStart, occurrence.Length),
            occurrence.SurfaceForm,
            StringComparison.Ordinal))
        {
            context = null!;
            return false;
        }

        context = new PreparationContext(
            document.Id,
            document.Title,
            text,
            relativeStart,
            occurrence.Length);
        return true;
    }

    private static bool ReviewIsResolved(SQLiteConnection connection, int wordId) =>
        !connection.Table<ReviewCandidateEntity>()
            .Any(candidate => candidate.WordId == wordId && candidate.Status == WordStatus.Unreviewed);

    private static void EnsureCurrentCandidate(
        SQLiteConnection connection,
        PreparationCandidateEntity candidate)
    {
        var session = connection.Find<PreparationSessionEntity>(candidate.SessionId);
        if (session?.Status != PreparationSessionStatus.Active)
        {
            throw new InvalidOperationException("The preparation session is not active.");
        }

        var current = connection.Table<PreparationCandidateEntity>()
            .Where(item => item.SessionId == session.Id
                && (item.Status == PreparationCandidateStatus.Pending
                    || item.Status == PreparationCandidateStatus.ResultReady
                    || item.Status == PreparationCandidateStatus.Failed))
            .OrderBy(item => item.Order)
            .FirstOrDefault();
        if (current?.Id != candidate.Id)
        {
            throw new InvalidOperationException("The submitted item is not the current preparation candidate.");
        }
    }

    private static void CompleteCandidate(
        SQLiteConnection connection,
        PreparationSessionEntity session,
        PreparationCandidateEntity candidate,
        PreparationCandidateStatus status,
        DateTime now)
    {
        candidate.Status = status;
        candidate.UpdatedAtUtc = now;
        connection.Update(candidate);
        session.CompletedItems++;
        session.UpdatedAtUtc = now;
        if (session.CompletedItems >= session.TotalItems)
        {
            session.Status = PreparationSessionStatus.Completed;
            session.CompletedAtUtc = now;
        }

        connection.Update(session);
    }

    /// <summary>
    /// Reads <paramref name="resultJson"/> through the discriminated
    /// <see cref="PreparationCandidatePayloadCodec"/> and returns the underlying provider lookup
    /// regardless of shape (Empty/EnvelopeV1/LegacyLexicalResult) — the pre-Slice-3 raw-only
    /// deserialization would silently mis-parse a Schema-8 envelope's own top-level JSON as if it were a
    /// <see cref="LexicalResult"/>. Malformed/UnsupportedEnvelopeVersion rows report no usable result
    /// rather than throwing, matching this method's pre-existing nullable-return contract for callers
    /// that already treat "no result yet" as normal (e.g. <see cref="CreateItemAsync"/>); callers that
    /// require the result to exist (<see cref="SelectMeaningAsync"/>) already throw on a null return.
    /// </summary>
    private static LexicalResult? DeserializeResult(string resultJson) =>
        PreparationCandidatePayloadCodec.Read(resultJson).AnyResult;

    /// <summary>
    /// Merges a freshly-completed provider lookup into whatever envelope shape the candidate already
    /// carried, preserving any evidence frozen earlier (at StartAsync or lazy-upgrade time) byte-for-byte —
    /// the lookup step itself must never recompute or replace frozen evidence.
    /// </summary>
    private static PreparationCandidatePayloadV1 MergeResultIntoEnvelope(string existingResultJson, LexicalResult result)
    {
        var read = PreparationCandidatePayloadCodec.Read(existingResultJson);
        return read.Kind == PreparationCandidatePayloadKind.EnvelopeV1
            ? read.Envelope! with { Result = result }
            : PreparationCandidatePayloadV1.Create(result);
    }

    private static async Task<PreparationCandidateEntity?> FindCurrentCandidateAsync(
        SQLiteAsyncConnection connection,
        int sessionId) => (PreparationCandidateEntity?)await connection.Table<PreparationCandidateEntity>()
            .Where(item => item.SessionId == sessionId
                && (item.Status == PreparationCandidateStatus.Pending
                    || item.Status == PreparationCandidateStatus.ResultReady
                    || item.Status == PreparationCandidateStatus.Failed))
            .OrderBy(item => item.Order)
            .FirstOrDefaultAsync();

#if DEBUG
    private void RecordTiming(
        int? candidateId,
        string operation,
        PreparationTimingPhase phase,
        long startedTimestamp) => RecordTiming(
            candidateId,
            operation,
            phase,
            Stopwatch.GetElapsedTime(startedTimestamp));

    private void RecordTiming(
        int? candidateId,
        string operation,
        PreparationTimingPhase phase,
        TimeSpan elapsed)
    {
        var measurement = new PreparationTimingMeasurement(
            Interlocked.Increment(ref _timingSequence),
            candidateId,
            operation,
            phase,
            elapsed.TotalMilliseconds,
            clock.UtcNow);
        lock (_timingSync)
        {
            _timingMeasurements.Add(measurement);
            if (_timingMeasurements.Count > MaximumTimingMeasurements)
            {
                _timingMeasurements.RemoveRange(
                    0,
                    _timingMeasurements.Count - MaximumTimingMeasurements);
            }
        }
    }
#else
    private static void RecordTiming(
        int? candidateId,
        string operation,
        PreparationTimingPhase phase,
        long startedTimestamp)
    {
    }
#endif

    private static LexicalLookupRequest CreateLookupRequest(PreparationItem item) => new(
        item.SourceLanguage,
        item.LookupMode,
        item.TargetLanguage,
        item.Term,
        item.TokenKind,
        WiktionaryLookupProvider.Name,
        item.EncounteredSurfaceForm ?? item.Term,
        item.Term);

    private static LexicalDiagnosticEvent DiagnosticEvent(
        PreparationItem item,
        string phase) => new(
        phase,
        item.Term,
        item.SourceLanguage,
        item.LookupMode,
        item.TargetLanguage,
        WiktionaryLookupProvider.Name);

    private static (LexicalLookupMode Mode, string? TargetLanguage) ResolveLookupSettings(
        DocumentEntity document)
    {
        if (!string.IsNullOrWhiteSpace(document.TargetLanguage))
        {
            return (document.LookupMode, document.TargetLanguage);
        }

        if (!string.Equals(
            document.TextLanguage,
            document.ExplanationLanguage,
            StringComparison.OrdinalIgnoreCase))
        {
            return (LexicalLookupMode.DefinitionAndTranslation, document.ExplanationLanguage);
        }

        return (LexicalLookupMode.Definition, null);
    }

    /// <summary>
    /// Delegates to the shared, database-independent <see cref="PreparationContextEvidencePolicy"/>
    /// (KF-MEANING-001 Slice 3) — kept as a private wrapper so every existing Schema-7 call site is
    /// unchanged, while the Schema-8 evidence scanner/ledger use the exact same underlying algorithm.
    /// </summary>
    private static string NormalizeContext(string value) => PreparationContextEvidencePolicy.NormalizeText(value);

    private static string CreateFingerprint(string value) => PreparationContextEvidencePolicy.CreateFingerprint(value);

    private sealed record PreparationLookupSource(PreparationItem Item, string DocumentContent);

    private sealed record PrefetchedLookup(int CandidateId, LexicalResult Result);

    internal sealed record ContextData(
        int DocumentId,
        string DocumentTitle,
        string ExplanationLanguage,
        string Text,
        int TargetStart,
        int TargetLength);
}
