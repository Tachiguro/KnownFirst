using System.Text.Json.Serialization;

namespace KnownFirst.Services.Lexical.Wikipedia;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WikipediaApiResponse))]
public sealed partial class WikipediaJsonSerializerContext : JsonSerializerContext
{
}
