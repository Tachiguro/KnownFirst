using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
            functionBody.Contains("packaging\\publish-google-play-bundle.ps1") ||
            functionBody.Contains("packaging/publish-google-play-bundle.ps1"),
            "Invoke-GooglePlayBundleAction must resolve packaging/publish-google-play-bundle.ps1."
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
    public void ObsoleteAndroidScripts_AreCompletelyRemoved()
    {
        var repoRoot = GetRepositoryRoot();
        var legacyGooglePlay = Path.Combine(repoRoot, "scripts", "publish-android-google-play.ps1");
        var legacyBeta = Path.Combine(repoRoot, "scripts", "publish-android-beta.ps1");
        Assert.IsFalse(File.Exists(legacyGooglePlay), "scripts/publish-android-google-play.ps1 must not exist.");
        Assert.IsFalse(File.Exists(legacyBeta), "scripts/publish-android-beta.ps1 must not exist.");
    }

    // B. Identity and boundaries

    [TestMethod]
    public void CanonicalScript_ReadsIdentityFromKnownFirstProject()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

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
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        Assert.IsTrue(
            canonicalScript.Contains("[System.IO.File]::Open") && canonicalScript.Contains("'None'"),
            "Canonical script must open a lock with FileShare.None."
        );
    }

    [TestMethod]
    public void CanonicalScript_AcquiresLockBeforeCriticalOperations()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

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
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        var finallyIndex = canonicalScript.IndexOf("finally", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(finallyIndex > 0, "Finally block must exist.");

        var finallyText = canonicalScript.Substring(finallyIndex);
        Assert.IsTrue(finallyText.Contains(".Dispose()"), "Lock disposal must occur in finally block.");
    }

    // D. Warning enforcement

    [TestMethod]
    public void CanonicalScript_EnforcesWarningsAsErrorsViaMSBuildEngineSwitch()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

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
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        Assert.IsFalse(canonicalScript.Contains("LastWriteTimeUtc"), "No LastWriteTimeUtc filtering remains.");

        var candidateSection = canonicalScript.Substring(canonicalScript.IndexOf("$candidates ="));
        Assert.IsFalse(candidateSection.Contains("Select-Object -First 1"), "No Select-Object -First 1 exists in candidate selection.");
        Assert.IsTrue(candidateSection.Contains("$candidates[0]"), "Candidate selection uses direct array access.");
    }

    [TestMethod]
    public void CanonicalScript_ValidatesEmptyPrePublishCandidateList()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        var cleanIndex = canonicalScript.IndexOf("dotnet clean", StringComparison.OrdinalIgnoreCase);
        var publishIndex = canonicalScript.IndexOf("dotnet @publishArguments", StringComparison.OrdinalIgnoreCase);

        var betweenCleanAndPublish = canonicalScript.Substring(cleanIndex, publishIndex - cleanIndex);
        Assert.IsTrue(betweenCleanAndPublish.Contains("*-Signed.aab"), "Post-clean candidate enumeration must exist.");
        Assert.IsTrue(betweenCleanAndPublish.Contains(".Count -gt 0"), "Post-clean candidate enumeration must be empty.");
    }

    [TestMethod]
    public void CanonicalScript_FailsOnZeroOrMultiplePostPublishCandidates()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

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
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        var verifyIndex = canonicalScript.IndexOf("-verify -strict", StringComparison.OrdinalIgnoreCase);
        var moveIndex = canonicalScript.IndexOf("Move-Item", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(verifyIndex > 0, "jarsigner uses -verify and -strict.");
        Assert.IsTrue(verifyIndex < moveIndex, "jarsigner verifies against staged output before finalization.");
    }

    [TestMethod]
    public void CanonicalScript_SidecarParserRejectsMultilineAndDoesNotUseWhitespaceRegex()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        Assert.IsFalse(canonicalScript.Contains(@"\s+"), "Sidecar parser must not use \\s+.");
        Assert.IsTrue(canonicalScript.Contains(@"\A") && canonicalScript.Contains(@"\z"), "Sidecar parser must anchor to start and end of string.");
        Assert.IsFalse(canonicalScript.Contains("Move-Item -LiteralPath $stagingBundlePath -Destination $bundlePath -Force"), "Finalization must not use -Force.");
    }

    [TestMethod]
    public void CanonicalScript_FailurePreservesOriginalErrorRecord()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        Assert.IsFalse(canonicalScript.Contains("$failureMessage = $_.Exception.Message"), "Script must not store only the exception message.");
        Assert.IsTrue(canonicalScript.Contains("throw $failureRecord") || Regex.IsMatch(canonicalScript, @"catch\s*\{[\s\S]*?throw\s+\$_[\s\S]*?\}"), "Catch must use bare throw or equivalent original-ErrorRecord-preserving flow.");

        var catchIndex = canonicalScript.IndexOf("catch {", StringComparison.OrdinalIgnoreCase);
        var finallyIndex = canonicalScript.IndexOf("finally {", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(catchIndex > 0 && finallyIndex > catchIndex, "Script must use structured try/catch/finally.");

        var catchBody = canonicalScript.Substring(catchIndex, finallyIndex - catchIndex);
        Assert.IsTrue(catchBody.Contains("finalFilesCreated"), "Final files created by the invocation must be tracked for rollback in catch.");
        Assert.IsTrue(catchBody.Contains("ErrorAction SilentlyContinue"), "Cleanup in catch must be silent.");
    }

    // H. Post-clean stale-output check — behavioral evidence
    //
    // These tests execute the ACTUAL production stale-output-check fragment lifted verbatim out of
    // scripts/publish-google-play-bundle.ps1, in an isolated temporary project root, under the same
    // $ErrorActionPreference = 'Stop' the real script sets. The fragment is not reimplemented here:
    // it is extracted between stable production markers so that a change to the real script changes
    // what these tests execute. The complete publish script is never invoked, so no clean, publish,
    // build, packaging, signing, or signing-material access occurs.

    private const string StaleOutputCheckStartMarker = "$releaseRoot = Join-Path $projectRoot";
    private const string StaleOutputCheckThrowMarker = "Stale output prevents trustworthy selection";

    private static string ExtractStaleOutputCheckFragment()
    {
        var canonicalScript = LoadScript("scripts/packaging/publish-google-play-bundle.ps1");

        var start = canonicalScript.IndexOf(StaleOutputCheckStartMarker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Production start marker '{StaleOutputCheckStartMarker}' was not found in the canonical script.");

        var throwIndex = canonicalScript.IndexOf(StaleOutputCheckThrowMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(throwIndex > start, $"Production throw marker '{StaleOutputCheckThrowMarker}' was not found after the start marker.");

        var closingBrace = canonicalScript.IndexOf('}', throwIndex);
        Assert.IsTrue(closingBrace > throwIndex, "The closing brace of the stale-output guard was not found.");

        var fragment = canonicalScript.Substring(start, closingBrace - start + 1);

        // Guard the harness itself: the extracted text must be the real guard, not an arbitrary slice.
        Assert.IsTrue(fragment.Contains("*-Signed.aab"), "Extracted fragment must contain the *-Signed.aab enumeration.");
        Assert.IsTrue(fragment.Contains("$preCandidates"), "Extracted fragment must contain the pre-candidate collection.");
        Assert.IsTrue(fragment.Contains(".Count -gt 0"), "Extracted fragment must contain the fail-closed count check.");

        return fragment;
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);

    private static PowerShellResult RunStaleOutputCheck(string temporaryProjectRoot)
    {
        var fragment = ExtractStaleOutputCheckFragment();

        var harness = new StringBuilder();
        harness.AppendLine("$ErrorActionPreference = \"Stop\"");
        harness.AppendLine($"$projectRoot = '{temporaryProjectRoot.Replace("'", "''")}'");
        harness.AppendLine(fragment);
        harness.AppendLine("Write-Output \"PRECANDIDATES=$($preCandidates.Count)\"");

        var harnessPath = Path.Combine(temporaryProjectRoot, "stale-output-check-harness.ps1");
        File.WriteAllText(harnessPath, harness.ToString(), new UTF8Encoding(false));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(harnessPath);

        using var process = Process.Start(startInfo);
        Assert.IsNotNull(process, "powershell.exe could not be started.");

        var standardOutput = process!.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(120000), "The stale-output-check harness did not exit within 120 seconds.");

        return new PowerShellResult(process.ExitCode, standardOutput, standardError);
    }

    private static string CreateTemporaryProjectRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "knownfirst-stale-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [TestMethod]
    public void CanonicalScript_StaleOutputCheckTreatsMissingReleaseDirectoryAsZeroCandidates()
    {
        var projectRoot = CreateTemporaryProjectRoot();
        try
        {
            var releaseRoot = Path.Combine(projectRoot, "bin", "Release", "net10.0-android");
            Assert.IsFalse(Directory.Exists(releaseRoot), "Precondition: the Android Release output directory must not exist.");

            var result = RunStaleOutputCheck(projectRoot);

            Assert.AreEqual(
                0,
                result.ExitCode,
                "A missing Android Release output directory means zero stale signed AABs and must not terminate the packaging run. " +
                $"Exit code: {result.ExitCode}. StdErr: {result.StandardError}"
            );
            Assert.IsTrue(
                result.StandardOutput.Contains("PRECANDIDATES=0"),
                $"The stale-output check must report zero pre-existing candidates. StdOut: {result.StandardOutput}"
            );
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [TestMethod]
    public void CanonicalScript_StaleOutputCheckStillFailsClosedOnRemainingSignedAab()
    {
        var projectRoot = CreateTemporaryProjectRoot();
        try
        {
            var releaseRoot = Path.Combine(projectRoot, "bin", "Release", "net10.0-android");
            Directory.CreateDirectory(releaseRoot);
            File.WriteAllText(
                Path.Combine(releaseRoot, "com.tachiguro.knownfirst-Signed.aab"),
                "not a real bundle - inert placeholder for the stale-output regression"
            );

            var result = RunStaleOutputCheck(projectRoot);

            Assert.AreNotEqual(
                0,
                result.ExitCode,
                $"A remaining *-Signed.aab after clean must still fail closed. StdOut: {result.StandardOutput}"
            );
            Assert.IsTrue(
                result.StandardError.Contains("Stale output prevents trustworthy selection"),
                $"The existing fail-closed stale-output message must be preserved. StdErr: {result.StandardError}"
            );
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [TestMethod]
    public void Launcher_ValidateAllAndroidReleaseBuild_EnforcesWarningStrictnessAndFreshCompilationParity()
    {
        var launcherScript = LoadScript("scripts/knownfirst.ps1");

        var match = Regex.Match(
            launcherScript,
            @"function\s+Invoke-ValidateAllAction\s*\{(?<body>[\s\S]*?)\n\}",
            RegexOptions.IgnoreCase
        );

        Assert.IsTrue(match.Success, "Invoke-ValidateAllAction function body must exist in knownfirst.ps1.");
        var functionBody = match.Groups["body"].Value;

        var androidReleaseStepMatch = Regex.Match(
            functionBody,
            @"DisplayName\s*=\s*'Android Release build'[\s\S]*?CommandArguments\s*=\s*@\((?<args>[^\)]+)\)",
            RegexOptions.IgnoreCase
        );

        Assert.IsTrue(androidReleaseStepMatch.Success, "ValidateAll must define the 'Android Release build' step with CommandArguments.");
        var argsString = androidReleaseStepMatch.Groups["args"].Value;

        Assert.IsTrue(argsString.Contains("'build'"), "ValidateAll Android Release step must use 'build'.");
        Assert.IsFalse(argsString.Contains("'publish'"), "ValidateAll Android Release step must not use 'publish'.");
        Assert.IsTrue(argsString.Contains("'-c'") && argsString.Contains("'Release'"), "ValidateAll Android Release step must use Configuration 'Release'.");
        Assert.IsTrue(argsString.Contains("'-f'") && (argsString.Contains("$androidTargetFramework") || argsString.Contains("'net10.0-android'")), "ValidateAll Android Release step must target net10.0-android.");
        Assert.IsTrue(argsString.Contains("'-m:1'"), "ValidateAll Android Release step must retain '-m:1'.");
        Assert.IsTrue(argsString.Contains("'--no-restore'"), "ValidateAll Android Release step must retain '--no-restore'.");
        Assert.IsTrue(argsString.Contains("'-warnaserror'"), "ValidateAll Android Release step must contain '-warnaserror' for parity with AAB packaging.");
        Assert.IsTrue(argsString.Contains("'-p:ILLinkTreatWarningsAsErrors=true'"), "ValidateAll Android Release step must contain '-p:ILLinkTreatWarningsAsErrors=true'.");
        Assert.IsTrue(argsString.Contains("'--no-incremental'"), "ValidateAll Android Release step must contain '--no-incremental' to force fresh diagnostic evaluation.");

        Assert.IsFalse(argsString.Contains("AndroidPackageFormats"), "ValidateAll Android Release build must not specify AndroidPackageFormats (creates no APK or AAB).");
        Assert.IsFalse(argsString.Contains(".aab"), "ValidateAll Android Release build must not reference AAB output.");
        Assert.IsFalse(argsString.Contains(".apk"), "ValidateAll Android Release build must not reference APK output.");
        Assert.IsFalse(functionBody.Contains("publish-google-play-bundle.ps1"), "ValidateAll must not invoke publish-google-play-bundle.ps1.");
    }
}
