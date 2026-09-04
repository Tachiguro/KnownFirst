using KnownFirst.Core.Settings;

namespace KnownFirst.Core.Learning;

/// <summary>
/// Pure target-centric policy and replay engine governing Automatic interaction qualification.
/// Invariants:
/// 1. Low-friction recall/Reading first;
/// 2. Good and Easy advance recall qualification (requires 2 distinct scheduled successes);
/// 3. Hard holds recall qualification;
/// 4. Again resets relevant recall qualification;
/// 5. Entering Typing qualification after 2 qualifying scheduled recall successes;
/// 6. Two successful typing checks on distinct scheduled review events qualify typing;
/// 7. Once qualified, routine Automatic interaction returns to low-friction non-typing maintenance (Reading);
/// 8. FSRS continues indefinitely;
/// 9. A later genuine scheduled recall lapse (Again) re-arms exactly one typing re-check;
/// 10. One successful re-check restores the qualified low-friction state;
/// 11. Same-session Again tail repeats do not count as distinct scheduled qualification reviews;
/// 12. Per-target typing opt-out prevents Automatic from entering typing qualification without altering FSRS identity.
/// </summary>
public static class TargetAutomaticProgressionPolicy
{
    public const int RequiredConsecutiveRecallSuccesses = 2;
    public const int RequiredConsecutiveTypingSuccesses = 2;

    public static LearningInteractionMode ResolveInteraction(
        LearningMode learningMode,
        TargetAutomaticProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return learningMode switch
        {
            LearningMode.Reading => LearningInteractionMode.Reading,
            LearningMode.Typing => state.TypingOptOut ? LearningInteractionMode.Reading : LearningInteractionMode.Typing,
            LearningMode.Automatic => state.InteractionMode,
            _ => LearningInteractionMode.Reading
        };
    }

    public static TargetAutomaticProgressionState ApplyEvent(
        TargetAutomaticProgressionState state,
        TargetInteractionEvent reviewEvent)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Invariant 11: same-session Again tail repeats must not count as distinct scheduled qualification reviews
        if (reviewEvent.IsSessionRepeat)
        {
            return state;
        }

        // Invariant 12 & 13: typing opt-out prevents entering typing qualification
        if (state.TypingOptOut)
        {
            return state with
            {
                InteractionMode = LearningInteractionMode.Reading,
                IsTypingQualified = false,
                IsRecheckArmed = false
            };
        }

        // Branch 1: Target has already qualified typing (is in qualified maintenance or re-checking)
        if (state.IsTypingQualified)
        {
            if (state.IsRecheckArmed)
            {
                // Invariant 10: one successful re-check restores the qualified low-friction state
                if (reviewEvent.WasCorrect && reviewEvent.Rating != ReviewRating.Again)
                {
                    return state with
                    {
                        IsRecheckArmed = false,
                        InteractionMode = LearningInteractionMode.Reading
                    };
                }

                // Failed re-check: lapse out of qualified maintenance back to initial recall qualification
                return state with
                {
                    IsTypingQualified = false,
                    IsRecheckArmed = false,
                    ConsecutiveRecallSuccesses = 0,
                    ConsecutiveTypingSuccesses = 0,
                    InteractionMode = LearningInteractionMode.Reading
                };
            }

            // Routine qualified maintenance:
            // Invariant 9: a later genuine scheduled recall lapse re-arms exactly one typing re-check
            if (reviewEvent.Rating == ReviewRating.Again)
            {
                return state with
                {
                    IsRecheckArmed = true,
                    InteractionMode = LearningInteractionMode.Typing,
                    ConsecutiveRecallSuccesses = 0
                };
            }

            // Normal Good/Easy/Hard during qualified maintenance: remains in low-friction Reading
            return state;
        }

        // Branch 2: Target is in Typing qualification
        if (state.InteractionMode == LearningInteractionMode.Typing)
        {
            // Invariant 6: two successful typing checks on distinct scheduled review events qualify typing
            if (reviewEvent.WasCorrect && reviewEvent.Rating != ReviewRating.Again)
            {
                var successes = Math.Min(
                    RequiredConsecutiveTypingSuccesses,
                    state.ConsecutiveTypingSuccesses + 1);

                if (successes >= RequiredConsecutiveTypingSuccesses)
                {
                    // Invariant 7: once qualified, routine Automatic interaction returns to low-friction non-typing maintenance
                    return state with
                    {
                        IsTypingQualified = true,
                        ConsecutiveTypingSuccesses = successes,
                        InteractionMode = LearningInteractionMode.Reading
                    };
                }

                return state with { ConsecutiveTypingSuccesses = successes };
            }

            // Failed typing check or lapse during typing qualification resets recall qualification
            return state with
            {
                InteractionMode = LearningInteractionMode.Reading,
                ConsecutiveRecallSuccesses = 0,
                ConsecutiveTypingSuccesses = 0
            };
        }

        // Branch 3: Target is in initial Recall qualification (Reading mode)
        switch (reviewEvent.Rating)
        {
            case ReviewRating.Good or ReviewRating.Easy:
                var successes = Math.Min(
                    RequiredConsecutiveRecallSuccesses,
                    state.ConsecutiveRecallSuccesses + 1);

                if (successes >= RequiredConsecutiveRecallSuccesses)
                {
                    // Invariant 5: after two qualifying scheduled recall successes, enter Typing qualification
                    return state with
                    {
                        ConsecutiveRecallSuccesses = successes,
                        InteractionMode = LearningInteractionMode.Typing,
                        ConsecutiveTypingSuccesses = 0
                    };
                }

                return state with { ConsecutiveRecallSuccesses = successes };

            case ReviewRating.Hard:
                // Invariant 3: Hard holds
                return state;

            case ReviewRating.Again:
                // Invariant 4: Again resets relevant recall qualification
                return state with { ConsecutiveRecallSuccesses = 0 };

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(reviewEvent),
                    reviewEvent.Rating,
                    "Unknown review rating.");
        }
    }

    public static TargetAutomaticProgressionState Replay(
        TargetAutomaticProgressionState initialState,
        IEnumerable<TargetInteractionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(events);

        var state = initialState;
        DateTimeOffset? previousTimestamp = null;

        foreach (var reviewEvent in events)
        {
            if (previousTimestamp.HasValue && reviewEvent.ReviewedAtUtc < previousTimestamp.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(events),
                    reviewEvent.ReviewedAtUtc,
                    "Review events must be supplied in non-decreasing timestamp order.");
            }

            state = ApplyEvent(state, reviewEvent);
            previousTimestamp = reviewEvent.ReviewedAtUtc;
        }

        return state;
    }

    public static TargetAutomaticProgressionState Replay(
        LearningTarget target,
        IEnumerable<TargetInteractionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Replay(TargetAutomaticProgressionState.InitialFor(target), events);
    }
}
