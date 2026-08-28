using System.Text.Json.Serialization;

namespace KnownFirst.Core.Settings;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(OnboardingDraft))]
[JsonSerializable(typeof(OnboardingCompletionJournal))]
[JsonSerializable(typeof(ThemePreference))]
[JsonSerializable(typeof(CardDirectionPreference))]
[JsonSerializable(typeof(LearningMode))]
[JsonSerializable(typeof(LearningTimezoneMode))]
public sealed partial class OnboardingJsonSerializerContext : JsonSerializerContext
{
}
