using AngleSharp.Html.Parser;
using KnownFirst.Core.Preparation;
using System.Text.RegularExpressions;
using AngleElement = AngleSharp.Dom.IElement;

namespace KnownFirst.Services.Lexical;

public sealed record LexicalEntryParseResult(
    IReadOnlyList<LexicalMeaning> DirectMeanings,
    IReadOnlyList<ProviderFormRelation> FormRelations,
    bool LanguageSectionFound);

public sealed partial class WiktionaryHtmlParser
{
    private const string LanguageNodeSelector = "[lang], [hreflang], [data-lang-code]";
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
            return new LexicalEntryParseResult([], [], false);
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
            return new LexicalEntryParseResult([], [], false);
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
        var meanings = string.Equals(sourceLanguage, "de", StringComparison.OrdinalIgnoreCase)
            ? ParseGermanLexicalLists(sectionElements)
            : [];
        _diagnosticLog.Write(Event("parser.html.german-meanings.complete"));
        _diagnosticLog.Write(Event("parser.html.ordered-meanings.start"));
        meanings.AddRange(ParseOrderedDefinitionLists(sectionElements));
        _diagnosticLog.Write(Event("parser.html.ordered-meanings.complete"));

        _diagnosticLog.Write(Event("parser.html.distinct.start"));
        var distinctMeanings = meanings
            .GroupBy(meaning => $"{meaning.PartOfSpeech}\u001f{meaning.Definition}", StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(20)
            .ToArray();
        _diagnosticLog.Write(Event("parser.html.distinct.complete"));
        _diagnosticLog.Write(Event("parser.html.relations.start"));
        var directDefinitions = distinctMeanings
            .Where(meaning => ProviderFormRelationPolicy.Resolve(meaning.Definition) is null)
            .ToArray();
        var formRelations = distinctMeanings
            .Select(meaning => ProviderFormRelationPolicy.Resolve(meaning.Definition))
            .Where(relation => relation is not null)
            .Cast<ProviderFormRelation>()
            .Distinct()
            .ToArray();
        _diagnosticLog.Write(Event("parser.html.relations.complete"));
        _diagnosticLog.Write(Event("parser.html.translations.start"));
        var translations = lookupMode == LexicalLookupMode.Definition || targetLanguage is null
            ? []
            : ParseTargetTranslations(sectionElements, targetLanguage);
        _diagnosticLog.Write(Event("parser.html.translations.complete"));
        var directMeanings = MergeDefinitionsAndTranslations(
            directDefinitions,
            translations,
            lookupMode);
        return new LexicalEntryParseResult(directMeanings, formRelations, true);

        LexicalDiagnosticEvent Event(string phase) => new(
            phase,
            normalizedTerm,
            sourceLanguage,
            lookupMode,
            targetLanguage,
            WiktionaryLookupProvider.Name,
            ParserOutcome: "parsing");
    }

    private static List<LexicalMeaning> ParseGermanLexicalLists(
        IReadOnlyList<AngleElement> sectionElements)
    {
        var meanings = new List<LexicalMeaning>();
        for (var index = 0; index < sectionElements.Count; index++)
        {
            var label = sectionElements[index];
            if (!IsGermanLexicalLabel(label))
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

    private static IReadOnlyList<LexicalMeaning> MergeDefinitionsAndTranslations(
        IReadOnlyList<LexicalMeaning> definitions,
        IReadOnlyList<string> translations,
        LexicalLookupMode lookupMode)
    {
        if (lookupMode == LexicalLookupMode.Definition)
        {
            return definitions
                .Select(meaning => meaning with { Translation = null })
                .ToArray();
        }

        var effectiveTranslations = translations.Count > 0
            ? translations
            : definitions
                .Select(meaning => meaning.Translation)
                .Where(translation => !string.IsNullOrWhiteSpace(translation))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        if (lookupMode == LexicalLookupMode.Translation)
        {
            return effectiveTranslations
                .Select((translation, index) => new LexicalMeaning(
                    $"wiktionary-translation-{index + 1}",
                    null,
                    string.Empty,
                    translation,
                    null,
                    []))
                .ToArray();
        }

        var result = new List<LexicalMeaning>(Math.Max(definitions.Count, effectiveTranslations.Count));
        var count = Math.Max(definitions.Count, effectiveTranslations.Count);
        for (var index = 0; index < count; index++)
        {
            var definition = index < definitions.Count ? definitions[index] : null;
            var translation = index < effectiveTranslations.Count ? effectiveTranslations[index] : null;
            result.Add(definition is null
                ? new LexicalMeaning(
                    $"wiktionary-translation-{index + 1}",
                    null,
                    string.Empty,
                    translation,
                    null,
                    [])
                : definition with { Translation = translation });
        }

        return result;
    }

    private static IReadOnlyList<string> ParseTargetTranslations(
        IReadOnlyList<AngleElement> sectionElements,
        string targetLanguage)
    {
        var translations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inTranslationRegion = false;
        var translationHeadingLevel = int.MaxValue;

        foreach (var element in sectionElements)
        {
            if (IsTranslationLabel(element))
            {
                inTranslationRegion = true;
                translationHeadingLevel = GetHeadingElement(element) is null
                    ? int.MaxValue
                    : GetHeadingLevel(element);
                AddTargetTranslations(element, targetLanguage, translations, seen);
                continue;
            }

            if (!inTranslationRegion)
            {
                continue;
            }

            if (IsTranslationBoundary(element, translationHeadingLevel))
            {
                inTranslationRegion = false;
                translationHeadingLevel = int.MaxValue;
                continue;
            }

            AddTargetTranslations(element, targetLanguage, translations, seen);
        }

        return translations.Take(20).ToArray();
    }

    private static void AddTargetTranslations(
        AngleElement element,
        string targetLanguage,
        ICollection<string> translations,
        ISet<string> seen)
    {
        var languageNodes = new[] { element }
            .Concat(element.QuerySelectorAll(LanguageNodeSelector))
            .Where(node => IsTargetLanguageNode(node, targetLanguage))
            .Where(node => !node.QuerySelectorAll(LanguageNodeSelector)
                .Any(descendant => IsTargetLanguageNode(descendant, targetLanguage)))
            .ToArray();
        foreach (var node in languageNodes)
        {
            AddTranslation(node.TextContent, translations, seen);
        }

        foreach (var item in element.QuerySelectorAll("li"))
        {
            if (!StartsWithTargetLanguageName(Clean(item.TextContent), targetLanguage)
                || item.QuerySelectorAll(LanguageNodeSelector)
                    .Any(node => IsTargetLanguageNode(node, targetLanguage)))
            {
                continue;
            }

            var linkedTerms = item.QuerySelectorAll("a")
                .Select(link => Clean(link.TextContent))
                .Where(value => value.Length > 0)
                .ToArray();
            if (linkedTerms.Length > 0)
            {
                foreach (var linkedTerm in linkedTerms)
                {
                    AddTranslation(linkedTerm, translations, seen);
                }

                continue;
            }

            var text = Clean(item.TextContent);
            var separator = text.IndexOf(':');
            if (separator < 0 || separator == text.Length - 1)
            {
                continue;
            }

            foreach (var value in text[(separator + 1)..].Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddTranslation(value, translations, seen);
            }
        }
    }

    private static void AddTranslation(
        string value,
        ICollection<string> translations,
        ISet<string> seen)
    {
        var cleaned = RemoveSenseMarker(Clean(value)).Trim(' ', ',', ';', '/');
        if (cleaned.Length > 0 && seen.Add(cleaned))
        {
            translations.Add(cleaned);
        }
    }

    private static bool IsTargetLanguageNode(AngleElement element, string targetLanguage)
    {
        var language = element.GetAttribute("lang")
            ?? element.GetAttribute("hreflang")
            ?? element.GetAttribute("data-lang-code");
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var languageFamily = language.Trim()
            .Replace('_', '-')
            .Split('-', 2, StringSplitOptions.TrimEntries)[0];
        return string.Equals(languageFamily, targetLanguage, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithTargetLanguageName(string value, string targetLanguage)
    {
        var names = string.Equals(targetLanguage, "de", StringComparison.OrdinalIgnoreCase)
            ? new[] { "German", "Deutsch" }
            : new[] { "English", "Englisch" };
        return names.Any(name => value.StartsWith($"{name}:", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTranslationLabel(AngleElement element)
    {
        var heading = GetHeadingElement(element);
        if (heading is not null && IsTranslationMarker(Clean(heading.TextContent)))
        {
            return true;
        }

        if (element.Matches("p") && IsTranslationMarker(Clean(element.TextContent)))
        {
            return true;
        }

        return new[] { element }
            .Concat(element.QuerySelectorAll("[id]"))
            .Select(candidate => candidate.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .Any(id => IsTranslationMarker(id!));
    }

    private static bool IsTranslationMarker(string value)
    {
        var normalized = value.Trim().TrimEnd(':').Replace('_', ' ');
        return normalized.StartsWith("Translations", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Translation", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Übersetzungen", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Uebersetzungen", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTranslationBoundary(AngleElement element, int translationHeadingLevel)
    {
        if (IsHeading(element))
        {
            return translationHeadingLevel == int.MaxValue
                || GetHeadingLevel(element) <= translationHeadingLevel;
        }

        return IsSemanticLabel(element);
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

    private static bool IsGermanLexicalLabel(AngleElement element)
    {
        var text = Clean(element.TextContent);
        var title = element.GetAttribute("title");
        return IsGermanLexicalMarker(text)
            || title?.Contains("Semantik", StringComparison.OrdinalIgnoreCase) == true
            || title?.Contains("Grammatische Merkmale", StringComparison.OrdinalIgnoreCase) == true
            || new[] { element }
                .Concat(element.QuerySelectorAll("[id]"))
                .Select(candidate => candidate.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .Any(id => IsGermanLexicalMarker(id!.Replace('_', ' ')));
    }

    private static bool IsGermanLexicalMarker(string value)
    {
        var normalized = value.Trim().TrimEnd(':').Replace('_', ' ');
        return normalized.StartsWith("Bedeutungen", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Grammatische Merkmale", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSemanticLabel(AngleElement element)
    {
        if (!element.Matches("p"))
        {
            return false;
        }

        var style = element.GetAttribute("style") ?? string.Empty;
        return style.Replace(" ", string.Empty)
                .Contains("font-weight:bold", StringComparison.OrdinalIgnoreCase)
            || element.QuerySelector("[id]") is not null;
    }

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
        var isKnownPartOfSpeech = markers.Any(marker => normalized.Equals(marker, StringComparison.Ordinal)
            || normalized.StartsWith($"{marker},", StringComparison.Ordinal)
            || normalized.StartsWith($"{marker} ", StringComparison.Ordinal));
        return isKnownPartOfSpeech
            || normalized.StartsWith("redewendung", StringComparison.Ordinal)
            || normalized.StartsWith("deklinierte form", StringComparison.Ordinal)
            || normalized.StartsWith("konjugierte form", StringComparison.Ordinal)
            || normalized.StartsWith("flektierte form", StringComparison.Ordinal);
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
