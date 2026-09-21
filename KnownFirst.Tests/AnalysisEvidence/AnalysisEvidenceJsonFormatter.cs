using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnownFirst.Tests.AnalysisEvidence;

public static class AnalysisEvidenceJsonFormatter
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateOptions();
    private static readonly AnalysisEvidenceJsonSerializerContext DefaultContext = new(DefaultOptions);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            NewLine = "\n"
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string Format(AnalysisEvidenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, DefaultContext.AnalysisEvidenceDocument);
    }

    public static AnalysisEvidenceDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, DefaultContext.AnalysisEvidenceDocument)
            ?? throw new InvalidOperationException("Failed to deserialize AnalysisEvidenceDocument.");
    }

    public static string Format(GermanGoldCorpusEvidenceArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return JsonSerializer.Serialize(artifact, DefaultContext.GermanGoldCorpusEvidenceArtifact);
    }

    public static GermanGoldCorpusEvidenceArtifact DeserializeCorpusArtifact(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, DefaultContext.GermanGoldCorpusEvidenceArtifact)
            ?? throw new InvalidOperationException("Failed to deserialize GermanGoldCorpusEvidenceArtifact.");
    }
}
