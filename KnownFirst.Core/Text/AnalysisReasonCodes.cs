namespace KnownFirst.Core.Text;

public static class AnalysisReasonCodes
{
    public const string SentenceBoundaryTerminatorWhitespace = nameof(SentenceBoundaryTerminatorWhitespace);
    public const string SentenceBoundaryTerminatorLineBreak = nameof(SentenceBoundaryTerminatorLineBreak);
    public const string SentenceBoundaryTerminatorCitation = nameof(SentenceBoundaryTerminatorCitation);
    public const string SentenceBoundaryTerminatorEnd = nameof(SentenceBoundaryTerminatorEnd);
    public const string SentenceBoundaryFinalRemainder = nameof(SentenceBoundaryFinalRemainder);

    public const string IncludedUnicodeWord = nameof(IncludedUnicodeWord);
    public const string IncludedAcronymPattern = nameof(IncludedAcronymPattern);
    public const string IncludedAbbreviationPattern = nameof(IncludedAbbreviationPattern);
    public const string IncludedTechnicalTokenPattern = nameof(IncludedTechnicalTokenPattern);
    public const string IncludedCveFamilyPattern = nameof(IncludedCveFamilyPattern);
    public const string IncludedShaFamilyPattern = nameof(IncludedShaFamilyPattern);
    public const string ExcludedUrl = nameof(ExcludedUrl);
    public const string ExcludedEmailAddress = nameof(ExcludedEmailAddress);
    public const string ExcludedStandaloneNumber = nameof(ExcludedStandaloneNumber);
    public const string ExcludedWhitespace = nameof(ExcludedWhitespace);
    public const string ExcludedPunctuationOnly = nameof(ExcludedPunctuationOnly);
    public const string ExcludedSymbolOnly = nameof(ExcludedSymbolOnly);

    public const string OrdinaryWordCaseGrouping = nameof(OrdinaryWordCaseGrouping);
    public const string RepeatedIdentity = nameof(RepeatedIdentity);
    public const string FirstIdentityOccurrence = nameof(FirstIdentityOccurrence);
    public const string TechnicalFamilyGrouping = nameof(TechnicalFamilyGrouping);
    public const string ExplicitLanguageRuleGrouping = nameof(ExplicitLanguageRuleGrouping);

    public const string SelectedFirstUniqueContext = nameof(SelectedFirstUniqueContext);
    public const string SelectedUniqueContext = nameof(SelectedUniqueContext);
    public const string RejectedDuplicateContext = nameof(RejectedDuplicateContext);
    public const string RejectedMaximumContexts = nameof(RejectedMaximumContexts);
    public const string RejectedInvalidCoordinates = nameof(RejectedInvalidCoordinates);
}
