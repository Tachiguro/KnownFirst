using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnownFirst.Services.Lexical;

public sealed class LexicalCacheRepository(
    IKnownFirstDatabase database,
    ILexicalDiagnosticLog? diagnosticLog = null) : ILexicalCacheRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILexicalDiagnosticLog _diagnosticLog =
        diagnosticLog ?? NullLexicalDiagnosticLog.Instance;

    public async Task<LexicalResult?> GetAsync(
        LexicalLookupRequest request,
        string provider,
        int providerSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            _diagnosticLog.Write(Event(request, provider, "cache.key.start"));
            var cacheKey = CreateCacheKey(request, provider, providerSchemaVersion);
            _diagnosticLog.Write(Event(request, provider, "cache.key.complete"));
            _diagnosticLog.Write(Event(request, provider, "cache.read.start"));
            var result = await database.ReadAsync(async connection =>
            {
                var entity = await connection.Table<LexicalCacheEntity>()
                    .Where(item => item.CacheKey == cacheKey)
                    .FirstOrDefaultAsync();
                if (entity is null)
                {
                    _diagnosticLog.Write(Event(request, provider, "cache.read.complete", "miss"));
                    return null;
                }

                _diagnosticLog.Write(Event(request, provider, "cache.deserialize.start", "hit"));
                var cachedResult = JsonSerializer.Deserialize<LexicalResult>(entity.ResultJson, SerializerOptions);
                _diagnosticLog.Write(Event(request, provider, "cache.deserialize.complete", "hit"));
                return cachedResult is null
                    || !LexicalResultInvariantPolicy.HasReferenceData(cachedResult, request.LookupMode)
                        ? null
                        : cachedResult with
                        {
                            IsFromCache = true,
                            LookupMode = request.LookupMode,
                            TargetLanguage = request.TargetLanguage
                        };
            });
            return result;
        }
        catch (Exception exception)
        {
            _diagnosticLog.Write(Event(request, provider, "cache.read.exception", "exception"), exception);
            throw;
        }
    }

    public async Task SaveAsync(
        LexicalLookupRequest request,
        LexicalResult result,
        int providerSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (!LexicalResultInvariantPolicy.HasReferenceData(result, request.LookupMode))
        {
            _diagnosticLog.Write(Event(request, result.ProviderName, "cache.write.skipped", "not-cacheable"));
            return;
        }

        try
        {
            _diagnosticLog.Write(Event(request, result.ProviderName, "cache.write.key.start"));
            var cacheKey = CreateCacheKey(request, result.ProviderName, providerSchemaVersion);
            _diagnosticLog.Write(Event(request, result.ProviderName, "cache.write.key.complete"));
            _diagnosticLog.Write(Event(request, result.ProviderName, "cache.write.start"));
            await database.RunInTransactionAsync(connection =>
            {
                var entity = connection.Table<LexicalCacheEntity>()
                    .FirstOrDefault(item => item.CacheKey == cacheKey);
                _diagnosticLog.Write(Event(request, result.ProviderName, "cache.serialize.start"));
                var serializedResult = JsonSerializer.Serialize(result with { IsFromCache = false }, SerializerOptions);
                _diagnosticLog.Write(Event(request, result.ProviderName, "cache.serialize.complete"));
                if (entity is null)
                {
                    entity = new LexicalCacheEntity
                    {
                        CacheKey = cacheKey,
                        SourceLanguage = request.SourceLanguage,
                        ExplanationLanguage = request.ExplanationLanguage,
                        NormalizedLemma = request.NormalizedLemma,
                        LookupMode = request.LookupMode,
                        TargetLanguage = request.TargetLanguage ?? string.Empty,
                        CanonicalLookupTerm = request.CanonicalLookupTerm,
                        TokenKind = request.TokenKind,
                        Provider = result.ProviderName,
                        ProviderSchemaVersion = providerSchemaVersion,
                        ResultJson = serializedResult,
                        SourceProject = result.SourceProject,
                        PageTitle = result.PageTitle,
                        RevisionId = result.RevisionId,
                        Attribution = result.Attribution,
                        FetchedAtUtc = result.LookupAtUtc
                    };
                    connection.Insert(entity);
                }
                else if (!string.Equals(entity.ResultJson, serializedResult, StringComparison.Ordinal))
                {
                    entity.ResultJson = serializedResult;
                    entity.SourceProject = result.SourceProject;
                    entity.PageTitle = result.PageTitle;
                    entity.RevisionId = result.RevisionId;
                    entity.Attribution = result.Attribution;
                    entity.FetchedAtUtc = result.LookupAtUtc;
                    connection.Update(entity);
                }

                return true;
            });
            _diagnosticLog.Write(Event(request, result.ProviderName, "cache.write.complete", "stored"));
        }
        catch (Exception exception)
        {
            _diagnosticLog.Write(Event(request, result.ProviderName, "cache.write.exception", "exception"), exception);
            throw;
        }
    }

    public static string CreateCacheKey(
        LexicalLookupRequest request,
        string provider,
        int providerSchemaVersion) => string.Join('|',
        "v2",
        request.SourceLanguage.ToLowerInvariant(),
        request.CanonicalLookupTerm.Normalize(),
        (int)request.LookupMode,
        request.TargetLanguage?.ToLowerInvariant() ?? "-",
        (int)request.TokenKind,
        provider.ToLowerInvariant(),
        providerSchemaVersion);

    private static LexicalDiagnosticEvent Event(
        LexicalLookupRequest request,
        string provider,
        string phase,
        string cacheOutcome = "-") => new(
        phase,
        request.CanonicalLookupTerm,
        request.SourceLanguage,
        request.LookupMode,
        request.TargetLanguage,
        provider,
        CacheOutcome: cacheOutcome);
}
