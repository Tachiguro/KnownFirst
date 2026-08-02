using KnownFirst.Core.Preparation;
using KnownFirst.Data;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Study;
using Microsoft.Extensions.DependencyInjection;

namespace KnownFirst.Services.Isolation;

/// <summary>
/// Debug/GUI-test-only deterministic data seeding (KF-MEANING-002 GUI hardening, Part 3). Populates the
/// isolated GUI-test database with a preparation candidate before first render, using the app's own
/// <see cref="ITextReviewService"/>/<see cref="IPreparationService"/> — never a hand-written database
/// row — so seeded data is always shaped exactly like data the real workflow would produce. Fails closed
/// exactly like <see cref="GuiTestProfile"/>: requesting seeding without the isolated profile active
/// throws rather than silently seeding (or not seeding) real application data.
/// </summary>
public static class GuiTestScenarioSeed
{
    public const string EnvironmentVariableName = "KNOWNFIRST_GUI_TEST_SEED_SCENARIO";

    public const string PreparationSelectedMeaning = "PreparationSelectedMeaning";
    public const string PreparationAcceptFailureRecovery = "PreparationAcceptFailureRecovery";
    public const string PreparationInvalidContextRecovery = "PreparationInvalidContextRecovery";

    public static string? Resolve()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!GuiTestProfile.IsActive)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} was set, but the isolated GUI test profile is not active. " +
                "Failing closed instead of risking seeding real application data.");
        }

        return raw.Trim();
    }

    public static bool IsActive => Resolve() is not null;

    public static async Task SeedAsync(IServiceProvider services)
    {
        var scenario = Resolve();
        if (scenario is null)
        {
            return;
        }

        var review = services.GetRequiredService<ITextReviewService>();
        var preparation = services.GetRequiredService<IPreparationService>();

        switch (scenario)
        {
            case PreparationSelectedMeaning:
            case PreparationAcceptFailureRecovery:
                await SeedTwoCandidateBatchAsync(review, preparation);
                break;

            case PreparationInvalidContextRecovery:
                await SeedInvalidContextCandidateAsync(review, preparation, services.GetRequiredService<IKnownFirstDatabase>());
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown {EnvironmentVariableName} value: '{scenario}'.");
        }
    }

    /// <summary>
    /// Two deterministic unknown words ("bank", "vault"), each the sole UnknownBacklog decision from its
    /// own short import (every other discovered candidate is explicitly marked Known, so real-dictionary
    /// recognition differences can never change which words end up in the batch). An AutomaticOnline batch
    /// of 2 is started and the first candidate is looked up, so <see cref="GuiTestSeedLookupProvider"/>'s
    /// three canned meanings are already attached when the UI first renders the preparation workflow.
    /// AutomaticOnline is required, not Manual: PrepareWords.razor always forces the manual-entry editor
    /// for PreparationMethod.Manual candidates, so the lexical-result "Other meaning" picker this scenario
    /// exercises only ever renders for AutomaticOnline candidates.
    /// </summary>
    private static async Task SeedTwoCandidateBatchAsync(ITextReviewService review, IPreparationService preparation)
    {
        await ImportWithOnlyThisWordUnknownAsync(review, "Bank fees apply here.", "bank");
        await ImportWithOnlyThisWordUnknownAsync(review, "Vault contents are secure.", "vault");

        await preparation.StartAsync(PreparationMethod.AutomaticOnline, 2);
        await preparation.LookupCurrentAsync();
    }

    /// <summary>
    /// One candidate ("equitable") with two occurrences: its own genuine one, plus a second, unrelated
    /// word's ("urgent") occurrence reassigned onto it directly via SQL — reproducing the exact
    /// misattribution shape KF-MEANING-002's context-integrity fail-safe defends against, with a valid
    /// context still present alongside the invalid one.
    /// </summary>
    private static async Task SeedInvalidContextCandidateAsync(
        ITextReviewService review, IPreparationService preparation, IKnownFirstDatabase database)
    {
        var targetWordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "An equitable approach is preferred.", "equitable");
        var otherWordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "List these urgent items now.", "urgent");

        // The reassignment is a write, so it goes through RunInTransactionAsync (the database's
        // write path) rather than ReadAsync - a mutation issued on the read path would bypass the
        // transaction boundary every other write in the app uses.
        await database.RunInTransactionAsync(transaction =>
        {
            var otherOccurrenceId = transaction.ExecuteScalar<int>(
                "SELECT Id FROM WordOccurrences WHERE WordId = ? LIMIT 1", otherWordId);
            transaction.Execute(
                "UPDATE WordOccurrences SET WordId = ? WHERE Id = ?", targetWordId, otherOccurrenceId);
            return true;
        });

        await preparation.StartAsync(PreparationMethod.AutomaticOnline, 1);
        await preparation.LookupCurrentAsync();
    }

    /// <summary>Imports a short deterministic text and decides exactly one word (<paramref name="unknownTerm"/>)
    /// as UnknownBacklog; every other discovered candidate is explicitly decided Known.</summary>
    private static async Task<int> ImportWithOnlyThisWordUnknownAsync(
        ITextReviewService review, string content, string unknownTerm)
    {
        var request = new ImportTextRequest(
            $"GUI test seed {Guid.NewGuid():N}", content, "en", LexicalLookupMode.Definition, null);
        await review.ImportAsync(request);

        var wordId = -1;
        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, unknownTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
                await review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        if (wordId == -1)
        {
            throw new InvalidOperationException(
                $"GUI-test seeding could not establish '{unknownTerm}' as an unknown-backlog word.");
        }

        return wordId;
    }
}
