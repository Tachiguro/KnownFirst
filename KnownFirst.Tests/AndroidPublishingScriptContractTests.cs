using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace KnownFirst.Tests;

[TestClass]
public sealed class AndroidPublishingScriptContractTests
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

    private static string LoadScript(string relativePath)
    {
        var path = Path.Combine(GetRepositoryRoot(), relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
        return File.ReadAllText(path);
    }

    // --------------------------------------------------------------------------------
    // Expected Green Contracts (Safeguards already present in canonical pipeline)
    // --------------------------------------------------------------------------------

    [TestMethod]
    public void Launcher_UsesCanonicalGooglePlayBundleScriptWithoutExternalIdentityOverrides()
    {
        var launcherScript = LoadScript("scripts/knownfirst.ps1");

        // Extract the function body of Invoke-GooglePlayBundleAction
        var match = Regex.Match(
            launcherScript,
            @"function\s+Invoke-GooglePlayBundleAction\s*\{(?<body>[\s\S]*?)\n\}",
            RegexOptions.IgnoreCase
        );

        Assert.IsTrue(match.Success, "Invoke-GooglePlayBundleAction function body must exist in knownfirst.ps1.");
        var functionBody = match.Groups["body"].Value;

        Assert.IsTrue(
            functionBody.Contains("publish-google-play-bundle.ps1"),
            "Invoke-GooglePlayBundleAction must resolve publish-google-play-bundle.ps1."
        );
        Assert.IsFalse(
            functionBody.Contains("-VersionCode"),
            "Invoke-GooglePlayBundleAction must not pass VersionCode parameter."
        );
        Assert.IsFalse(
            functionBody.Contains("-DisplayVersion"),
            "Invoke-GooglePlayBundleAction must not pass DisplayVersion parameter."
        );
        Assert.IsFalse(
            functionBody.Contains("publish-android-google-play.ps1"),
            "Canonical launcher path must not invoke publish-android-google-play.ps1."
        );
    }

    [TestMethod]
    public void PublishingScript_ReadsIdentityFromKnownFirstProject()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsTrue(
            canonicalScript.Contains("KnownFirst.csproj"),
            "Canonical script must resolve KnownFirst.csproj."
        );
        Assert.IsTrue(
            canonicalScript.Contains("KnownFirstProductVersion"),
            "Canonical script must read KnownFirstProductVersion."
        );
        Assert.IsTrue(
            canonicalScript.Contains("KnownFirstBuildNumber"),
            "Canonical script must read KnownFirstBuildNumber."
        );
        Assert.IsTrue(
            canonicalScript.Contains("Get-KnownFirstVersionInfo"),
            "Canonical script must obtain identity through its Get-KnownFirstVersionInfo flow."
        );

        // Check param block does not expose VersionCode or DisplayVersion
        var paramMatch = Regex.Match(canonicalScript, @"param\s*\((?<params>[\s\S]*?)\)", RegexOptions.IgnoreCase);
        Assert.IsTrue(paramMatch.Success, "Canonical script must have a param block.");
        var paramText = paramMatch.Groups["params"].Value;

        Assert.IsFalse(
            paramText.Contains("VersionCode"),
            "Canonical script param block must not expose VersionCode."
        );
        Assert.IsFalse(
            paramText.Contains("DisplayVersion"),
            "Canonical script param block must not expose DisplayVersion."
        );

        // Does not mutate KnownFirst.csproj
        Assert.IsFalse(
            Regex.IsMatch(canonicalScript, @"(Set-Content|Out-File|\[System.IO.File\]::Write).*KnownFirst\.csproj", RegexOptions.IgnoreCase),
            "Canonical script must not write to KnownFirst.csproj."
        );
    }

    [TestMethod]
    public void PublishingScript_SerializesAndroidPublish()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        // Inspect actual publish argument array construction
        var publishArgMatch = Regex.Match(
            canonicalScript,
            @"\$publishArguments\s*=\s*@\((?<args>[\s\S]*?)\)",
            RegexOptions.IgnoreCase
        );

        Assert.IsTrue(publishArgMatch.Success, "$publishArguments array declaration must be present in canonical script.");
        var argsText = publishArgMatch.Groups["args"].Value;

        Assert.IsTrue(
            argsText.Contains("\"-m:1\"") || argsText.Contains("'-m:1'"),
            "$publishArguments array must contain exactly one explicit single-node MSBuild control: '-m:1'."
        );
    }

    [TestMethod]
    public void PublishingScript_PreservesSecretAndPublicationBoundaries()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsFalse(
            Regex.IsMatch(canonicalScript, @"GooglePlayUpload", RegexOptions.IgnoreCase),
            "Canonical script must perform no Google Play upload."
        );

        Assert.IsTrue(
            canonicalScript.Contains("env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD"),
            "Canonical script must pass signing passwords via environment-variable indirection."
        );

        // Secret values not placed directly into visible arguments
        Assert.IsFalse(
            Regex.IsMatch(canonicalScript, @"-p:AndroidSigningKeyPass=(?!env:)", RegexOptions.IgnoreCase),
            "Signing password value must not be directly placed into visible arguments."
        );

        // Restoration of previous environment values in finally block
        var finallyIndex = canonicalScript.IndexOf("finally", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(finallyIndex >= 0, "Canonical script must have a finally block.");
        var finallyText = canonicalScript.Substring(finallyIndex);

        Assert.IsTrue(
            finallyText.Contains("KNOWNFIRST_ANDROID_SIGNING_PASSWORD"),
            "Finally block must restore KNOWNFIRST_ANDROID_SIGNING_PASSWORD state."
        );
        Assert.IsTrue(
            finallyText.Contains("JAVA_HOME"),
            "Finally block must restore JAVA_HOME state."
        );

        Assert.IsFalse(
            Regex.IsMatch(canonicalScript, @"Remove-Item.*\.keystore", RegexOptions.IgnoreCase),
            "Canonical script must not delete external keystore."
        );
    }

    // --------------------------------------------------------------------------------
    // Expected Red Contracts (Missing safeguards in current canonical script)
    // --------------------------------------------------------------------------------

    [TestMethod]
    public void PublishingScript_DoesNotDeleteOrOverwriteExistingFinalArtifactBeforeVerification()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        // Reject pre-build Remove-Item targeting final bundle path
        Assert.IsFalse(
            Regex.IsMatch(canonicalScript, @"Remove-Item\s+-LiteralPath\s+\$bundlePath", RegexOptions.IgnoreCase),
            "Pre-build Remove-Item of final artifact bundle is not allowed."
        );

        // Require fail-closed check throwing an error if final target exists
        Assert.IsTrue(
            Regex.IsMatch(canonicalScript, @"Test-Path\s+-LiteralPath\s+\$bundlePath[\s\S]*?throw", RegexOptions.IgnoreCase),
            "Canonical script must fail closed when Test-Path on final bundle path is true."
        );
    }

    [TestMethod]
    public void PublishingScript_UsesUniqueStagingAndFailClosedFinalCollision()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        // Require unique staging directory creation
        Assert.IsTrue(
            Regex.IsMatch(canonicalScript, @"\$staging.*New-Item.*-ItemType\s+Directory", RegexOptions.IgnoreCase) ||
            canonicalScript.Contains("stagingDir") || canonicalScript.Contains("stagingBundlePath"),
            "A unique per-invocation staging directory must be created."
        );

        // Require collision check for final sidecar
        Assert.IsTrue(
            canonicalScript.Contains("checksumPath") && canonicalScript.Contains("Test-Path") && canonicalScript.Contains("throw"),
            "Canonical script must perform collision check for the final sidecar path before finalization."
        );
    }

    [TestMethod]
    public void PublishingScript_RejectsAmbiguousSignedBundleCandidates()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        // Reject silent ambiguity resolution via Select-Object -First 1
        var candidateMatch = Regex.Match(
            canonicalScript,
            @"\$signedBundle\s*=\s*Get-ChildItem[\s\S]*?Select-Object\s+-First\s+1",
            RegexOptions.IgnoreCase
        );

        Assert.IsFalse(
            candidateMatch.Success,
            "Candidate selection must fail on ambiguous candidates rather than silently taking Select-Object -First 1."
        );

        Assert.IsTrue(
            canonicalScript.Contains("ambiguous") || canonicalScript.Contains("candidates.Count"),
            "Canonical script must explicitly validate candidate count to reject ambiguous signed bundles."
        );
    }

    [TestMethod]
    public void PublishingScript_RequiresStrictSignatureVerificationBeforeFinalization()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsTrue(
            canonicalScript.Contains("-strict"),
            "jarsigner verification must include the -strict flag."
        );

        var verifyIndex = canonicalScript.IndexOf("jarsigner -verify -strict", StringComparison.OrdinalIgnoreCase);
        var moveIndex = canonicalScript.IndexOf("Move-Item", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(verifyIndex >= 0, "Strict jarsigner verification command must be present.");
        Assert.IsTrue(
            moveIndex >= 0 && verifyIndex < moveIndex,
            "Strict signature verification must be executed on the staged candidate BEFORE moving to final path."
        );
    }

    [TestMethod]
    public void PublishingScript_CreatesAndVerifiesStagedAndFinalSha256Pair()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsTrue(
            canonicalScript.Contains("checksumPath") || canonicalScript.Contains(".sha256.txt"),
            "Canonical script must define a SHA-256 sidecar path."
        );

        // Require writing the sidecar file
        Assert.IsTrue(
            canonicalScript.Contains("Set-Content") && (canonicalScript.Contains("checksumPath") || canonicalScript.Contains(".sha256.txt")),
            "Canonical script must write a SHA-256 sidecar file."
        );

        // Require recomputing hashes after finalization
        var hashMatches = Regex.Matches(canonicalScript, "Get-FileHash");
        Assert.IsTrue(
            hashMatches.Count >= 2,
            "Canonical script must calculate and re-verify SHA-256 hashes multiple times (staged & final)."
        );

        // Require rollback of created final files on error
        Assert.IsTrue(
            canonicalScript.Contains("catch") && canonicalScript.Contains("finalFilesCreated"),
            "Canonical script must track and rollback created final files if final-pair validation fails."
        );
    }

    [TestMethod]
    public void PublishingScript_EnforcesWarningsAsErrorsAtPublishBoundary()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        // Inspect actual publish argument array construction
        var publishArgMatch = Regex.Match(
            canonicalScript,
            @"\$publishArguments\s*=\s*@\((?<args>[\s\S]*?)\)",
            RegexOptions.IgnoreCase
        );

        Assert.IsTrue(publishArgMatch.Success, "$publishArguments array declaration must be present in canonical script.");
        var argsText = publishArgMatch.Groups["args"].Value;

        Assert.IsTrue(
            argsText.Contains("TreatWarningsAsErrors") || argsText.Contains("WarningsAsErrors") || argsText.Contains("/warnaserror"),
            "Deterministic warning-as-error switch/property must be present in $publishArguments."
        );
    }
}
