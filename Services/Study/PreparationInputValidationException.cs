using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Study;

public enum PreparationInputValidationReason
{
    DefinitionRequired = 0,
    TranslationRequired = 1,
    AnswerRequired = 2
}

public sealed class PreparationInputValidationException : Exception
{
    public PreparationInputValidationException(
        PreparationInputValidationReason reason,
        LexicalLookupMode inputMode)
        : base(GetSafeMessage(reason))
    {
        Reason = reason;
        InputMode = inputMode;
    }

    public PreparationInputValidationReason Reason { get; }

    public LexicalLookupMode InputMode { get; }

    private static string GetSafeMessage(PreparationInputValidationReason reason) => reason switch
    {
        PreparationInputValidationReason.DefinitionRequired => "A manual definition is required.",
        PreparationInputValidationReason.TranslationRequired => "A manual translation is required.",
        _ => "A manual definition or translation is required."
    };
}
