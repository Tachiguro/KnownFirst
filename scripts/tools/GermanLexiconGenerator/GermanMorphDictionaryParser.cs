namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// Deterministic streaming parser for the upstream <c>DE_morph_dict.txt</c> word-form text
/// format (DuyguA/german-morph-dictionaries, CC BY-SA 4.0 data; this parser is KnownFirst
/// Apache-2.0 code).
///
/// Format: a line containing no space starts a new word-form block; every following line up to
/// the next such line is an analysis line for that word form, formatted as
/// <c>&lt;lemma&gt; &lt;comma,delimited,tags&gt;</c>. Blank lines and analysis lines that
/// precede any word-form header are rejected as malformed input.
/// </summary>
public static class GermanMorphDictionaryParser
{
    public static IEnumerable<GermanMorphDictionaryAnalysis> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return ParseCore(reader);
    }

    private static IEnumerable<GermanMorphDictionaryAnalysis> ParseCore(TextReader reader)
    {
        string? currentWord = null;
        var lineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (line.Length == 0)
            {
                throw new GermanMorphDictionaryFormatException(
                    $"Line {lineNumber}: blank lines are not valid in the upstream morphological dictionary format.");
            }

            var spaceIndex = line.IndexOf(' ');
            if (spaceIndex < 0)
            {
                currentWord = line;
                continue;
            }

            if (currentWord is null)
            {
                throw new GermanMorphDictionaryFormatException(
                    $"Line {lineNumber}: analysis line encountered before any word-form header line.");
            }

            var lemma = line[..spaceIndex];
            var tags = line[(spaceIndex + 1)..];
            if (lemma.Length == 0 || tags.Length == 0)
            {
                throw new GermanMorphDictionaryFormatException(
                    $"Line {lineNumber}: analysis line is missing a lemma or a tag list.");
            }

            var commaIndex = tags.IndexOf(',');
            var rawCategory = commaIndex < 0 ? tags : tags[..commaIndex];
            if (rawCategory.Length == 0)
            {
                throw new GermanMorphDictionaryFormatException(
                    $"Line {lineNumber}: analysis line has an empty leading tag.");
            }

            yield return new GermanMorphDictionaryAnalysis(currentWord, lemma, rawCategory, tags);
        }
    }
}
