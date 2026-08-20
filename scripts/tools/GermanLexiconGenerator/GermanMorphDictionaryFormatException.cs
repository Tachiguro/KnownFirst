namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// Thrown when the upstream German morphological dictionary text does not follow the expected
/// word-form-header / analysis-line format. Parsing fails closed: it never guesses a recovery.
/// </summary>
public sealed class GermanMorphDictionaryFormatException(string message) : Exception(message);
