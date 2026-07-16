using KnownFirst.Core.Learning;

namespace KnownFirst.Core.Settings;

public static class CardDirectionPreferencePolicy
{
    public const CardDirectionPreference DefaultPreference = CardDirectionPreference.Both;

    public static CardDirectionPreference Normalize(int value) => value switch
    {
        (int)CardDirectionPreference.TermToMeaning => CardDirectionPreference.TermToMeaning,
        (int)CardDirectionPreference.MeaningToTerm => CardDirectionPreference.MeaningToTerm,
        (int)CardDirectionPreference.Both => CardDirectionPreference.Both,
        _ => DefaultPreference
    };

    public static IReadOnlyList<CardDirection> GetDirections(CardDirectionPreference preference) =>
        Normalize((int)preference) switch
        {
            CardDirectionPreference.TermToMeaning => [CardDirection.TermToMeaning],
            CardDirectionPreference.MeaningToTerm => [CardDirection.MeaningToTerm],
            _ => [CardDirection.TermToMeaning, CardDirection.MeaningToTerm]
        };
}
