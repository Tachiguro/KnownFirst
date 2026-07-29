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

    private static string LoadDocs(string relativePath)
    {
        var path = Path.Combine(GetRepositoryRoot(), relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
        return File.ReadAllText(path);
    }

    // A. Canonical entry point and legacy path

    [TestMethod]
    public void Launcher_UsesCanonicalGooglePlayBundleScriptWithoutExternalIdentityOverrides()
    {
        var launcherScript = LoadScript("scripts/knownfirst.ps1");

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
    public void Docs_DocumentCanonicalLauncherCommand()
    {
        var docs = LoadDocs("docs/BUILD_AND_RELEASE.md");
        Assert.IsTrue(docs.Contains("knownfirst.ps1 -Action GooglePlayBundle"), "BUILD_AND_RELEASE.md must document the canonical launcher command.");
        Assert.IsFalse(docs.Contains("publish-android-google-play.ps1"), "BUILD_AND_RELEASE.md must not advertise the legacy script as supported.");
    }

    [TestMethod]
    public void LegacyScript_FailsClosedBeforeAnyBuildOrArtifactBehavior()
    {
        var legacyScript = LoadScript("scripts/publish-android-google-play.ps1");

        Assert.IsTrue(legacyScript.Contains("throw "), "Legacy script must contain a throw statement.");
        Assert.IsTrue(legacyScript.Contains("knownfirst.ps1 -Action GooglePlayBundle"), "Legacy script must point to the canonical entry point.");

        Assert.IsFalse(legacyScript.Contains("dotnet publish"), "Legacy script must not contain dotnet publish.");
        Assert.IsFalse(legacyScript.Contains("jarsigner"), "Legacy script must not contain jarsigner.");
        Assert.IsFalse(legacyScript.Contains("Remove-Item"), "Legacy script must not delete files.");
    }

    // B. Identity and boundaries

    [TestMethod]
    public void CanonicalScript_ReadsIdentityFromKnownFirstProject()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsTrue(
            canonicalScript.Contains("KnownFirstProductVersion") && canonicalScript.Contains("KnownFirstBuildNumber"),
            "Canonical script must read KnownFirstProductVersion and KnownFirstBuildNumber."
        );
        Assert.IsFalse(
            Regex.IsMatch(canonicalScript, @"(Set-Content|Out-File|\[System.IO.File\]::Write).*KnownFirst\.csproj", RegexOptions.IgnoreCase),
            "Canonical script must not write to KnownFirst.csproj."
        );
        Assert.IsFalse(
            Regex.IsMatch(canonicalScript, @"GooglePlayUpload", RegexOptions.IgnoreCase),
            "Canonical script must perform no Google Play upload."
        );
        Assert.IsTrue(
            canonicalScript.Contains("env:KNOWNFIRST_ANDROID_SIGNING_PASSWORD"),
            "Canonical script must pass signing passwords via environment-variable indirection."
        );
    }

    // C. Cross-process serialization

    [TestMethod]
    public void CanonicalScript_OpensDeterministicLockWithFileShareNone()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsTrue(
            canonicalScript.Contains("[System.IO.File]::Open") && canonicalScript.Contains("'None'"),
            "Canonical script must open a lock with FileShare.None."
        );
    }

    [TestMethod]
    public void CanonicalScript_AcquiresLockBeforeCriticalOperations()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        var lockIndex = canonicalScript.IndexOf("[System.IO.File]::Open", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(lockIndex > 0, "Lock acquisition must exist.");

        var collisionIndex = canonicalScript.IndexOf("Final artifact paths already exist", StringComparison.OrdinalIgnoreCase);
        var stagingIndex = canonicalScript.IndexOf("NewGuid", StringComparison.OrdinalIgnoreCase);
        var cleanIndex = canonicalScript.IndexOf("dotnet clean", StringComparison.OrdinalIgnoreCase);
        var publishIndex = canonicalScript.IndexOf("dotnet @publishArguments", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(collisionIndex > lockIndex, "Lock must be acquired before collision check.");
        Assert.IsTrue(stagingIndex > lockIndex, "Lock must be acquired before staging.");
        Assert.IsTrue(cleanIndex > lockIndex, "Lock must be acquired before clean.");
        Assert.IsTrue(publishIndex > lockIndex, "Lock must be acquired before publish.");
    }

    [TestMethod]
    public void CanonicalScript_LockDisposalOccursInFinally()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        var finallyIndex = canonicalScript.IndexOf("finally", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(finallyIndex > 0, "Finally block must exist.");

        var finallyText = canonicalScript.Substring(finallyIndex);
        Assert.IsTrue(finallyText.Contains(".Dispose()"), "Lock disposal must occur in finally block.");
    }

    // D. Warning enforcement

    [TestMethod]
    public void CanonicalScript_EnforcesWarningsAsErrorsViaMSBuildEngineSwitch()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        var publishArgMatch = Regex.Match(
            canonicalScript,
            @"\$publishArguments\s*=\s*@\((?<args>[\s\S]*?)\)",
            RegexOptions.IgnoreCase
        );

        Assert.IsTrue(publishArgMatch.Success, "$publishArguments array declaration must be present in canonical script.");
        var argsText = publishArgMatch.Groups["args"].Value;

        Assert.IsFalse(argsText.Contains("-p:TreatWarningsAsErrors=true"), "The test must not accept TreatWarningsAsErrors text as a substitute for -warnaserror.");
        Assert.IsTrue(argsText.Contains("\"-warnaserror\"") || argsText.Contains("'-warnaserror'"), "Actual publish arguments must contain -warnaserror.");
        Assert.IsTrue(argsText.Contains("ILLinkTreatWarningsAsErrors=true"), "Actual publish arguments must contain ILLinkTreatWarningsAsErrors=true.");
        Assert.IsTrue(argsText.Contains("\"-m:1\"") || argsText.Contains("'-m:1'"), "Actual publish arguments must retain exactly one effective -m:1.");
    }

    // E. Candidate ownership

    [TestMethod]
    public void CanonicalScript_RemovesLastWriteTimeUtcFiltering()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsFalse(canonicalScript.Contains("LastWriteTimeUtc"), "No LastWriteTimeUtc filtering remains.");

        var candidateSection = canonicalScript.Substring(canonicalScript.IndexOf("$candidates ="));
        Assert.IsFalse(candidateSection.Contains("Select-Object -First 1"), "No Select-Object -First 1 exists in candidate selection.");
        Assert.IsTrue(candidateSection.Contains("$candidates[0]"), "Candidate selection uses direct array access.");
    }

    [TestMethod]
    public void CanonicalScript_ValidatesEmptyPrePublishCandidateList()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        var cleanIndex = canonicalScript.IndexOf("dotnet clean", StringComparison.OrdinalIgnoreCase);
        var publishIndex = canonicalScript.IndexOf("dotnet @publishArguments", StringComparison.OrdinalIgnoreCase);

        var betweenCleanAndPublish = canonicalScript.Substring(cleanIndex, publishIndex - cleanIndex);
        Assert.IsTrue(betweenCleanAndPublish.Contains("*-Signed.aab"), "Post-clean candidate enumeration must exist.");
        Assert.IsTrue(betweenCleanAndPublish.Contains(".Count -gt 0"), "Post-clean candidate enumeration must be empty.");
    }

    [TestMethod]
    public void CanonicalScript_FailsOnZeroOrMultiplePostPublishCandidates()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        var publishIndex = canonicalScript.IndexOf("dotnet @publishArguments", StringComparison.OrdinalIgnoreCase);
        var afterPublish = canonicalScript.Substring(publishIndex);

        Assert.IsTrue(afterPublish.Contains(".Count -eq 0"), "Post-publish zero candidates must fail.");
        Assert.IsTrue(afterPublish.Contains(".Count -gt 1"), "Post-publish multiple candidates must fail.");
    }

    // F. Reuse integrity

    [TestMethod]
    public void Launcher_ReuseVerifiesSidecarFormatAndHash()
    {
        var launcherScript = LoadScript("scripts/knownfirst.ps1");

        var actionMatch = Regex.Match(launcherScript, @"function\s+Invoke-GooglePlayBundleAction\s*\{(?<body>[\s\S]*?)\n\}", RegexOptions.IgnoreCase);
        Assert.IsTrue(actionMatch.Success);
        var actionBody = actionMatch.Groups["body"].Value;

        // GooglePlayBundle output state includes both AAB and sidecar
        var outputMatch = Regex.Match(actionBody, @"Test-LauncherStateReusable[\s\S]*?-OutputFiles\s+@\((?<outputs>[^\)]+)\)", RegexOptions.IgnoreCase);
        Assert.IsTrue(outputMatch.Success, "OutputFiles argument must be present for Test-LauncherStateReusable in GooglePlayBundle action.");
        Assert.IsTrue(outputMatch.Groups["outputs"].Value.Contains("$checksumPath"), "GooglePlayBundle output state must include sidecar.");
        Assert.IsTrue(launcherScript.Contains("$checksumPath = \"$aabPath.sha256.txt\"") || launcherScript.Contains("$checksumPath = \"$($aabPath).sha256.txt\""), "Sidecar path must be derived from AAB path.");

        // test logic
        var testFunctionMatch = Regex.Match(launcherScript, @"function\s+Test-LauncherStateReusable\s*\{(?<body>[\s\S]*?)\n\}", RegexOptions.IgnoreCase);
        Assert.IsTrue(testFunctionMatch.Success);
        var testBody = testFunctionMatch.Groups["body"].Value;

        Assert.IsTrue(testBody.Contains("-notmatch"), "Reuse verifies sidecar format.");
        Assert.IsTrue(testBody.Contains("Get-FileHash"), "Reuse verifies real AAB hash.");
        Assert.IsTrue(testBody.Contains("Substring") || testBody.Contains("Replace"), "Reuse derives AAB path from sidecar.");
        Assert.IsTrue(testBody.Contains("return $null"), "Incomplete or malformed pairs cannot be reported reusable.");
    }

    // G. Signature, checksum, finalization, and failure behavior

    [TestMethod]
    public void CanonicalScript_SignatureVerificationIsStrictAndStaged()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        var verifyIndex = canonicalScript.IndexOf("-verify -strict", StringComparison.OrdinalIgnoreCase);
        var moveIndex = canonicalScript.IndexOf("Move-Item", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(verifyIndex > 0, "jarsigner uses -verify and -strict.");
        Assert.IsTrue(verifyIndex < moveIndex, "jarsigner verifies against staged output before finalization.");
    }

    [TestMethod]
    public void CanonicalScript_SidecarParserRejectsMultilineAndDoesNotUseWhitespaceRegex()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsFalse(canonicalScript.Contains(@"\s+"), "Sidecar parser must not use \\s+.");
        Assert.IsTrue(canonicalScript.Contains(@"\A") && canonicalScript.Contains(@"\z"), "Sidecar parser must anchor to start and end of string.");
        Assert.IsFalse(canonicalScript.Contains("Move-Item -LiteralPath $stagingBundlePath -Destination $bundlePath -Force"), "Finalization must not use -Force.");
    }

    [TestMethod]
    public void CanonicalScript_FailurePreservesOriginalErrorRecord()
    {
        var canonicalScript = LoadScript("scripts/publish-google-play-bundle.ps1");

        Assert.IsFalse(canonicalScript.Contains("$failureMessage = $_.Exception.Message"), "Script must not store only the exception message.");
        Assert.IsTrue(canonicalScript.Contains("throw $failureRecord") || Regex.IsMatch(canonicalScript, @"catch\s*\{[\s\S]*?throw\s+\$_[\s\S]*?\}"), "Catch must use bare throw or equivalent original-ErrorRecord-preserving flow.");

        var catchIndex = canonicalScript.IndexOf("catch {", StringComparison.OrdinalIgnoreCase);
        var finallyIndex = canonicalScript.IndexOf("finally {", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(catchIndex > 0 && finallyIndex > catchIndex, "Script must use structured try/catch/finally.");

        var catchBody = canonicalScript.Substring(catchIndex, finallyIndex - catchIndex);
        Assert.IsTrue(catchBody.Contains("finalFilesCreated"), "Final files created by the invocation must be tracked for rollback in catch.");
        Assert.IsTrue(catchBody.Contains("ErrorAction SilentlyContinue"), "Cleanup in catch must be silent.");
    }
}
