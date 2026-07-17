using AngleSharp.Html.Parser;
using KnownFirst.Core.Preparation;
using System.Text.RegularExpressions;
using AngleElement = AngleSharp.Dom.IElement;

namespace KnownFirst.Services.Lexical;

public sealed record LexicalEntryParseResult(
    IReadOnlyList<LexicalMeaning> DirectMeanings,
    IReadOnlyList<ProviderFormRelation> FormRelations);

public sealed partial class WiktionaryHtmlParser
{
    private readonly HtmlParser _parser = new();
    private readonly ILexicalDiagnosticLog _diagnosticLog;

    public WiktionaryHtmlParser(ILexicalDiagnosticLog? diagnosticLog = null)
    {
        _diagnosticLog = diagnosticLog ?? NullLexicalDiagnosticLog.Instance;
    }

    public IReadOnlyList<LexicalMeaning> Parse(
        string html,
        string sourceLanguage,
        string explanationLanguage) => ParseEntry(html, sourceLanguage, explanationLanguage).DirectMeanings;

    public LexicalEntryParseResult ParseEntry(
        string html,
        string sourceLanguage,
        string explanationLanguage,
        string normalizedTerm = "-",
        LexicalLookupMode lookupMode = LexicalLookupMode.Definition,
        string? targetLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new LexicalEntryParseResult([], []);
        }

        _diagnosticLog.Write(Event("parser.html.document.start"));
        var document = _parser.ParseDocument(html);
        _diagnosticLog.Write(Event("parser.html.document.complete"));
        _diagnosticLog.Write(Event("parser.html.language-heading.start"));
        var heading = document.QuerySelectorAll("h2, h3")
            .FirstOrDefault(candidate => IsLanguageHeading(candidate, sourceLanguage));
        _diagnosticLog.Write(Event("parser.html.language-heading.complete"));
        if (heading is null)
        {
            return new LexicalEntryParseResult([], []);
        }

        _diagnosticLog.Write(Event("parser.html.section.start"));
        var sectionStart = GetHeadingContainer(heading);
        var headingLevel = GetHeadingLevel(sectionStart);
        var sectionElements = new List<AngleElement>();
        for (var element = sectionStart.NextElementSibling; element is not null; element = element.NextElementSibling)
        {
            if (IsHeading(element) && GetHeadingLevel(element) <= headingLevel)
            {
                break;
            }

            sectionElements.Add(element);
        }
        _diagnosticLog.Write(Event("parser.html.section.complete"));

        _diagnosticLog.Write(Event("parser.html.german-meanings.start"));
        var meanings = ParseGermanMeaningLists(sectionElements);
        _diagnosticLog.Write(Event("parser.html.german-meanings.complete"));
        if (meanings.Count == 0)
        {
            _diagnosticLog.Write(Event("parser.html.ordered-meanings.start"));
            meanings = ParseOrderedDefinitionLists(sectionElements);
            _diagnosticLog.Write(Event("parser.html.ordered-meanings.complete"));
        }

        _diagnosticLog.Write(Event("parser.html.distinct.start"));
        var distinctMeanings = meanings
            .GroupBy(meaning => $"{meaning.Definition}\u001f{meaning.Translation}", StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(20)
            .ToArray();
        _diagnosticLog.Write(Event("parser.html.distinct.complete"));
        _diagnosticLog.Write(Event("parser.html.relations.start"));
        var directMeanings = distinctMeanings
            .Where(meaning => ProviderFormRelationPolicy.Resolve(meaning.Definition) is null)
            .ToArray();
        var formRelations = distinctMeanings
            .Select(meaning => ProviderFormRelationPolicy.Resolve(meaning.Definition))
            .Where(relation => relation is not null)
            .Cast<ProviderFormRelation>()
            .Distinct()
            .ToArray();
        _diagnosticLog.Write(Event("parser.html.relations.complete"));
        return new LexicalEntryParseResult(directMeanings, formRelations);

        LexicalDiagnosticEvent Event(string phase) => new(
            phase,
            normalizedTerm,
            sourceLanguage,
            lookupMode,
            targetLanguage,
            WiktionaryLookupProvider.Name,
            ParserOutcome: "parsing");
    }

    private static List<LexicalMeaning> ParseGermanMeaningLists(
        IReadOnlyList<AngleElement> sectionElements)
    {
        var meanings = new List<LexicalMeaning>();
        for (var index = 0; index < sectionElements.Count; index++)
        {
            var label = sectionElements[index];
            if (!IsGermanMeaningLabel(label))
            {
                continue;
            }

            var partOfSpeech = FindPreviousPartOfSpeech(sectionElements, index);
            for (var listIndex = index + 1; listIndex < sectionElements.Count; listIndex++)
            {
                var element = sectionElements[listIndex];
                if (IsHeading(element) || IsSemanticLabel(element))
                {
                    break;
                }

                var definitionItems = GetDirectItems(element, "DL", "DD");
                if (definitionItems.Count == 0)
                {
                    continue;
                }

                foreach (var item in definitionItems)
                {
                    AddMeaning(meanings, item, partOfSpeech);
                }

                break;
            }
        }

        return meanings;
    }

    private static List<LexicalMeaning> ParseOrderedDefinitionLists(
        IReadOnlyList<AngleElement> sectionElements)
    {
        var meanings = new List<LexicalMeaning>();
        string? partOfSpeech = null;
        var partOfSpeechLevel = int.MaxValue;
        foreach (var element in sectionElements)
        {
            var heading = GetHeadingElement(element);
            if (heading is not null)
            {
                var headingText = Clean(heading.TextContent);
                var headingLevel = GetHeadingLevel(heading);
                if (IsPartOfSpeechHeading(headingText))
                {
                    partOfSpeech = headingText;
                    partOfSpeechLevel = headingLevel;
                }
                else if (partOfSpeech is not null && headingLevel >= partOfSpeechLevel)
                {
                    partOfSpeech = null;
                    partOfSpeechLevel = int.MaxValue;
                }

                continue;
            }

            if (partOfSpeech is null)
            {
                continue;
            }

            foreach (var item in GetDirectItems(element, "OL", "LI", includeNestedItems: true))
            {
                AddMeaning(meanings, item, partOfSpeech);
            }
        }

        return meanings;
    }

    private static void AddMeaning(
        ICollection<LexicalMeaning> meanings,
        AngleElement item,
        string? partOfSpeech)
    {
        var definitionNode = item.QuerySelector(".definition, [data-definition]");
        var translationNode = item.QuerySelector(".translation, .translation-item, [data-translation]");
        var exampleNode = item.QuerySelector(
            ".example, [data-example], .h-usage-example, .e-example, .ux");
        var definition = RemoveSenseMarker(Clean(
            definitionNode?.TextContent ?? ExtractPrimaryText(item)));
        var translation = CleanNullable(translationNode?.TextContent);
        var example = CleanNullable(exampleNode?.TextContent);
        if (string.IsNullOrWhiteSpace(definition) && string.IsNullOrWhiteSpace(translation))
        {
            return;
        }

        var labels = item.QuerySelectorAll(
                ".usage-label, .label, [data-label], .ib-content, .qualifier-content")
            .Select(label => Clean(label.TextContent))
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        meanings.Add(new LexicalMeaning(
            $"wiktionary-{meanings.Count + 1}",
            partOfSpeech,
            definition,
            translation,
            example,
            labels));
    }

    private static string ExtractPrimaryText(AngleElement item) => string.Concat(
        item.ChildNodes
            .Where(node => node is not AngleElement child || !IsExcludedDefinitionChild(child))
            .Select(node => node.TextContent));

    private static bool IsExcludedDefinitionChild(AngleElement element) =>
        element.TagName is "OL" or "UL" or "DL"
        || element.Matches(
            ".translation, .translation-item, [data-translation], .example, [data-example], .h-usage-example, .e-example, .ux, .usage-label, .label, [data-label]")
        || element.TagName == "SUP" && HasClass(element, "reference");

    private static IReadOnlyList<AngleElement> GetDirectItems(
        AngleElement element,
        string containerTag,
        string itemTag,
        bool includeNestedItems = false)
    {
        var containers = element.TagName == containerTag
            ? new[] { element }
            : element.Children.Where(child => child.TagName == containerTag).ToArray();
        return containers
            .SelectMany(container => includeNestedItems
                ? container.QuerySelectorAll(itemTag.ToLowerInvariant())
                : container.Children.Where(child => child.TagName == itemTag))
            .Where(item => !includeNestedItems || item.ParentElement?.TagName == containerTag)
            .ToArray();
    }

    private static string? FindPreviousPartOfSpeech(
        IReadOnlyList<AngleElement> elements,
        int beforeIndex)
    {
        for (var index = beforeIndex - 1; index >= 0; index--)
        {
            var heading = GetHeadingElement(elements[index]);
            if (heading is null)
            {
                continue;
            }

            var text = Clean(heading.TextContent);
            return IsPartOfSpeechHeading(text) ? text : null;
        }

        return null;
    }

    private static bool IsGermanMeaningLabel(AngleElement element)
    {
        var text = Clean(element.TextContent);
        return element.Matches("p")
            && (text.Equals("Bedeutungen", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Bedeutungen:", StringComparison.OrdinalIgnoreCase)
                || element.GetAttribute("title")?.Contains("Semantik", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool IsSemanticLabel(AngleElement element) => element.Matches("p")
        && element.GetAttribute("style")?.Contains("font-weight:bold", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsPartOfSpeechHeading(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        string[] markers =
        [
            "noun", "proper noun", "verb", "adjective", "adverb", "pronoun",
            "determiner", "numeral", "conjunction", "interjection", "preposition",
            "phrase", "initialism", "acronym", "abbreviation", "substantiv",
            "eigenname", "adjektiv", "adverb", "pronomen", "artikel", "numerale",
            "konjunktion", "interjektion", "präposition", "wortverbindung", "abkürzung"
        ];
        return markers.Any(marker => normalized.Equals(marker, StringComparison.Ordinal)
            || normalized.StartsWith($"{marker},", StringComparison.Ordinal)
            || normalized.StartsWith($"{marker} ", StringComparison.Ordinal));
    }

    private static AngleElement GetHeadingContainer(AngleElement heading) =>
        heading.ParentElement is { } parent && HasClass(parent, "mw-heading")
            ? parent
            : heading;

    private static AngleElement? GetHeadingElement(AngleElement element)
    {
        if (IsRawHeading(element))
        {
            return element;
        }

        return HasClass(element, "mw-heading")
            ? element.Children.FirstOrDefault(IsRawHeading)
            : null;
    }

    private static bool HasClass(AngleElement element, string className)
    {
        var classAttribute = element.GetAttribute("class");
        if (string.IsNullOrWhiteSpace(classAttribute))
        {
            return false;
        }

        var tokens = classAttribute.Split(
            [' ', '\t', '\r', '\n', '\f'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            if (string.Equals(tokens[index], className, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLanguageHeading(AngleElement heading, string sourceLanguage)
    {
        var markers = string.Equals(sourceLanguage, "de", StringComparison.OrdinalIgnoreCase)
            ? new[] { "German", "Deutsch" }
            : new[] { "English", "Englisch" };
        var id = heading.Id;
        var text = Clean(heading.TextContent);
        return markers.Any(marker =>
            string.Equals(id, marker, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, marker, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith($"{marker} (", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith($"({marker})", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHeading(AngleElement element) => GetHeadingElement(element) is not null;

    private static bool IsRawHeading(AngleElement element) =>
        element.TagName.Length == 2
        && element.TagName[0] == 'H'
        && char.IsDigit(element.TagName[1]);

    private static int GetHeadingLevel(AngleElement element)
    {
        var heading = GetHeadingElement(element);
        return heading is null ? int.MaxValue : heading.TagName[1] - '0';
    }

    private static string RemoveSenseMarker(string value) =>
        SenseMarkerRegex().Replace(value, string.Empty);

    private static string Clean(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim(' ', ':', ';', '\u2013', '\u2014');

    private static string? CleanNullable(string? value)
    {
        var cleaned = value is null ? string.Empty : Clean(value);
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^\[[^\]]+\]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SenseMarkerRegex();
}
