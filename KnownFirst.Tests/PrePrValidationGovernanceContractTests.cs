using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PrePrValidationGovernanceContractTests
{
    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string LoadDoc(params string[] relativePathSegments)
    {
        var path = Path.Combine([GetRepositoryRoot(), .. relativePathSegments]);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Documentation file not found: {path}");
        }
        return File.ReadAllText(path);
    }

    [TestMethod]
    public void AgentsMd_DeclaresMandatoryPrePrValidationGate()
    {
        var content = LoadDoc("AGENTS.md");

        Assert.Contains("Mandatory pre-PR full validation:", content, StringComparison.Ordinal);
        Assert.Contains("FULL_VALIDATION", content, StringComparison.Ordinal);
        Assert.Contains("PUSH_ONLY", content, StringComparison.Ordinal);
        Assert.Contains("PR_ONLY", content, StringComparison.Ordinal);
        Assert.Contains("exact final candidate commit", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalidate", content, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void PromptRouting_EncodesPrePrValidationAndDelegationBoundaries()
    {
        var content = LoadDoc("docs", "PROMPT_AND_TASK_ROUTING.md");

        Assert.Contains("Mandatory Pre-PR Full-Validation Gate", content, StringComparison.Ordinal);
        Assert.Contains("ValidateAll", content, StringComparison.Ordinal);
        Assert.Contains("exact final candidate commit", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Standing delegation covers mandatory pre-PR validation", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exposes a genuine unresolved material decision (see Section E)", content, StringComparison.Ordinal);
        Assert.DoesNotContain("exposes a genuine unresolved material decision (see Section F)", content, StringComparison.Ordinal);
        Assert.Contains("Non-Delegable Operations", content, StringComparison.Ordinal);
        Assert.Contains("Ad-hoc `BUILD_ONLY`", content, StringComparison.Ordinal);
        Assert.Contains("APK or AAB", content, StringComparison.Ordinal);
        Assert.Contains("stale", content, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void AgentWorkflow_ExplicitPhaseSequence_MandatesPrePrFullValidation()
    {
        var content = LoadDoc("docs", "AGENT_WORKFLOW.md");

        Assert.Contains("Mandatory Pre-PR FULL_VALIDATION", content, StringComparison.Ordinal);
        Assert.Contains("COMMIT_ONLY", content, StringComparison.Ordinal);
        Assert.Contains("PUSH_ONLY", content, StringComparison.Ordinal);
        Assert.Contains("exact candidate HEAD", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalidat", content, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void TestingGuide_FullValidation_DefinesPrePrValidationMatrixAndEvidenceBoundaries()
    {
        var content = LoadDoc("docs", "TESTING.md");

        Assert.Contains("Mandatory Pre-PR Full-Validation Gate", content, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\knownfirst.ps1 -Action ValidateAll", content, StringComparison.Ordinal);
        Assert.Contains("ALL_AUTOMATED", content, StringComparison.Ordinal);
        Assert.Contains("Windows Debug", content, StringComparison.Ordinal);
        Assert.Contains("Windows Release", content, StringComparison.Ordinal);
        Assert.Contains("Android Debug", content, StringComparison.Ordinal);
        Assert.Contains("Android Release", content, StringComparison.Ordinal);
        Assert.Contains("Evidence boundaries", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fail-Closed Stop Policy", content, StringComparison.Ordinal);
    }
}
