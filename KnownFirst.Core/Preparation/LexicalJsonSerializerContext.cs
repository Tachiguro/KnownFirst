using System.Text.Json.Serialization;

namespace KnownFirst.Core.Preparation;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LexicalResult))]
[JsonSerializable(typeof(string[]))]
public sealed partial class LexicalJsonSerializerContext : JsonSerializerContext
{
}
