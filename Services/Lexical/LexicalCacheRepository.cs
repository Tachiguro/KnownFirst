using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnownFirst.Services.Lexical;

public sealed class LexicalCacheRepository(IKnownFirstDatabase database) : ILexicalCacheRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<LexicalResult?> GetAsync(
        LexicalLookupRequest request,
        string provider,
        int providerSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cacheKey = CreateCacheKey(request, provider, providerSchemaVersion);
        return database.ReadAsync(async connection =>
        {
            var entity = await connection.Table<LexicalCacheEntity>()
                .Where(item => item.CacheKey == cacheKey)
                .FirstOrDefaultAsync();
            if (entity is null)
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<LexicalResult>(entity.ResultJson, SerializerOptions);
            return result is null ? null : result with { IsFromCache = true };
        });
    }

    public Task SaveAsync(
        LexicalLookupRequest request,
        LexicalResult result,
        int providerSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasReferenceData)
        {
            return Task.CompletedTask;
        }

        var cacheKey = CreateCacheKey(request, result.ProviderName, providerSchemaVersion);
        return database.RunInTransactionAsync(connection =>
        {
            var entity = connection.Table<LexicalCacheEntity>()
                .FirstOrDefault(item => item.CacheKey == cacheKey);
            var serializedResult = JsonSerializer.Serialize(result with { IsFromCache = false }, SerializerOptions);
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
}
