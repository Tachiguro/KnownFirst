using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Models;

namespace KnownFirst.Services.Study;

public interface IPreparationService
{
    Task<PreparationOverview> GetOverviewAsync();

    Task<int> StartAsync(PreparationMethod method, int requestedLimit);

    Task<PreparationItem?> GetCurrentAsync();

    Task<PreparationItem?> LookupCurrentAsync(CancellationToken cancellationToken = default);

    Task SelectMeaningAsync(int candidateId, int meaningIndex);

    Task AcceptAsync(
        int candidateId,
        PreparedMeaningInput input,
        CardDirectionPreference cardDirectionPreference);

    Task MarkKnownAsync(int candidateId);

    Task ExcludeAsync(int candidateId);

    Task SkipAsync(int candidateId);

    Task CancelPrefetchAsync();

#if DEBUG
    IReadOnlyList<PreparationTimingMeasurement> GetTimingDiagnostics();

    void RecordUiTransition(int? candidateId, TimeSpan elapsed);
#endif
}
