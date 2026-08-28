namespace KnownFirst.Core.Learning;

public static class ActiveLearningEligibilityPolicy
{
    public static bool IsEligible(WordLearningControl wordControl, SenseLearningControl senseControl)
    {
        ArgumentNullException.ThrowIfNull(wordControl);
        ArgumentNullException.ThrowIfNull(senseControl);

        if (wordControl.IsAlreadyKnown)
        {
            return false;
        }

        if (senseControl.IsStopped)
        {
            return false;
        }

        return true;
    }
}
