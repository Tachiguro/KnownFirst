namespace KnownFirst.Services;

public interface IAppSettingsService
{
    int PreparationLimit { get; }

    IReadOnlyList<int> SupportedPreparationLimits { get; }

    void SetPreparationLimit(int preparationLimit);

    void Reset();
}
