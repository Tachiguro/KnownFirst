using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KnownFirst.Tests;

/// <summary>
/// Static and behavioral contracts for the two Windows distribution channels:
/// <c>WindowsPortablePackage</c> (self-contained unpackaged win-x64 payload archived as a ZIP)
/// and <c>WindowsMsixPackage</c> (x64 MSIX, unsigned by default, Microsoft Store oriented).
///
/// These tests never invoke either publishing script end to end, so no restore, build, publish,
/// archive, MSIX creation, signing, certificate access, installation, or upload occurs. The
/// behavioral tests execute small fragments lifted verbatim out of the production scripts between
/// stable markers, so changing the real script changes what is executed here.
///
/// Per docs/TESTING.md, this is release-script contract evidence: it proves argument binding,
/// guard conditions, and static invariants. It does not prove that a publish, package, or signing
/// operation succeeds end to end on the current toolchain.
/// </summary>
[TestClass]
public sealed class WindowsDistributionPackagingContractTests
{
    private const string LauncherPath = "scripts/knownfirst.ps1";
    private const string PortableScriptPath = "scripts/packaging/publish-windows-portable.ps1";
    private const string MsixScriptPath = "scripts/packaging/publish-windows-msix.ps1";
    private const string CommonHelperPath = "scripts/packaging/windows-distribution-common.ps1";

    private const string WindowsTargetFramework = "net10.0-windows10.0.19041.0";
    private const string ThumbprintEnvironmentVariable = "KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT";

    /// <summary>
    /// Properties that change what NuGet restore resolves, so a <c>--no-restore</c> publish cannot
    /// introduce them afterwards.
    /// </summary>
    private static readonly string[] RestoreGraphAffectingProperties =
    [
        "Configuration",
        "SelfContained",
        "WindowsAppSDKSelfContained",
        "WindowsPackageType",
        "RuntimeIdentifierOverride",
        "KnownFirstWindowsPortablePackaging",
        "KnownFirstWindowsMsixPackaging",
    ];

    // === A. Launcher recognises both channels ==========================================

    [TestMethod]
    public void Launcher_DeclaresBothWindowsDistributionActionsAndPreservesTheExistingOnes()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var actions = ExtractValidateSetValues(launcher, "Action");

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Test", "GuiTest", "WindowsBuild", "AndroidTestPackage", "GooglePlayBundle",
                "WindowsPortablePackage", "WindowsMsixPackage", "ValidateAll", "Clean",
            },
            actions,
            "The launcher must add the Clean and Windows distribution actions and keep every existing action.");
    }

    [TestMethod]
    public void Launcher_DeclaresMsixSigningWithExactlyNoneAndExternalDefaultingToNone()
    {
        var launcher = LoadRepositoryFile(LauncherPath);

        CollectionAssert.AreEquivalent(
            new[] { "None", "External" },
            ExtractValidateSetValues(launcher, "MsixSigning"),
            "MsixSigning must accept exactly None and External.");

        Assert.IsTrue(
            Regex.IsMatch(launcher, @"\[ValidateSet\('None',\s*'External'\)\][\s\r\n]*\[string\]\$MsixSigning\s*=\s*'None'"),
            "MsixSigning must default to the unsigned None mode.");
    }

    [TestMethod]
    public void Launcher_DispatchesBothWindowsDistributionActions()
    {
        var dispatch = ExtractFunctionBody(LoadRepositoryFile(LauncherPath), "Invoke-KnownFirstAction");

        Assert.Contains("'WindowsPortablePackage' { return Invoke-WindowsPortablePackageAction }", dispatch);
        Assert.Contains("'WindowsMsixPackage' { return Invoke-WindowsMsixPackageAction }", dispatch);
    }

    [TestMethod]
    public void Launcher_RoutesTheInteractiveWindowsDistributionSubmenuToBothActions()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var submenu = ExtractFunctionBody(launcher, "Show-WindowsDistributionMenu");

        Assert.Contains("Invoke-KnownFirstAction -SelectedAction 'WindowsPortablePackage'", submenu);
        Assert.Contains("Invoke-KnownFirstAction -SelectedAction 'WindowsMsixPackage'", submenu);

        // The submenu must be reachable from the top-level menu, and the top-level menu must not
        // invoke the packaging actions directly (a single reachable route per action).
        var mainMenu = ExtractFunctionBody(launcher, "Show-KnownFirstMenu");
        Assert.Contains("Show-WindowsDistributionMenu", mainMenu);
        Assert.DoesNotContain("Invoke-KnownFirstAction -SelectedAction 'WindowsPortablePackage'", mainMenu);
        Assert.DoesNotContain("Invoke-KnownFirstAction -SelectedAction 'WindowsMsixPackage'", mainMenu);
    }

    // === B. Exact publish properties ====================================================

    [TestMethod]
    public void PortableScript_PublishesExactlyTheSelfContainedUnpackagedWindowsX64Release()
    {
        var arguments = ExtractArgumentArray(LoadRepositoryFile(PortableScriptPath), "publishArguments");

        foreach (var expected in new[]
        {
            "\"publish\"",
            "\"-f\", \"net10.0-windows10.0.19041.0\"",
            "\"-c\", \"Release\"",
            "\"--no-restore\"",
            "\"-p:KnownFirstWindowsPortablePackaging=true\"",
            "\"-p:RuntimeIdentifierOverride=win-x64\"",
            "\"-p:WindowsPackageType=None\"",
            "\"-p:WindowsAppSDKSelfContained=true\"",
            "\"-p:SelfContained=true\"",
            "\"-p:PublishAppXPackage=false\"",
        })
        {
            Assert.Contains(expected, arguments, $"Portable publish arguments must contain {expected}.");
        }

        Assert.DoesNotContain("WindowsPackageType=MSIX", arguments, "The portable channel must never build a package.");
        foreach (var forbidden in new[]
        {
            "AppxPackageSigningEnabled", "AppxPackageDir", "AppxBundle", "UapAppxPackageBuildMode",
        })
        {
            Assert.DoesNotContain(forbidden, arguments,
                $"The portable channel must never touch MSIX packaging property {forbidden}.");
        }
    }

    [TestMethod]
    public void MsixScript_PublishesExactlyTheX64ReleaseMsix()
    {
        var arguments = ExtractArgumentArray(LoadRepositoryFile(MsixScriptPath), "publishArguments");

        foreach (var expected in new[]
        {
            "\"publish\"",
            "\"-f\", \"net10.0-windows10.0.19041.0\"",
            "\"-c\", \"Release\"",
            "\"--no-restore\"",
            "\"-p:KnownFirstWindowsMsixPackaging=true\"",
            "\"-p:RuntimeIdentifierOverride=win-x64\"",
            "\"-p:WindowsPackageType=MSIX\"",
            "\"-p:AppxBundle=Never\"",
            "\"-p:UapAppxPackageBuildMode=SideloadOnly\"",
            "\"-p:SelfContained=true\"",
            "\"-p:WindowsAppSDKSelfContained=true\"",
        })
        {
            Assert.Contains(expected, arguments, $"MSIX publish arguments must contain {expected}.");
        }

        Assert.DoesNotContain("WindowsPackageType=None", arguments);
        Assert.Contains("AppxPackageDir=", arguments, "The MSIX output directory must be pinned explicitly.");
    }

    [TestMethod]
    public void MsixScript_DisablesSigningByDefaultAndEnablesItOnlyThroughAThumbprint()
    {
        var script = LoadRepositoryFile(MsixScriptPath);

        Assert.Contains("-p:AppxPackageSigningEnabled=false", script,
            "The default MSIX mode must produce an unsigned package.");
        Assert.Contains("-p:AppxPackageSigningEnabled=true", script);
        Assert.Contains("-p:PackageCertificateThumbprint=", script,
            "External signing must be enabled only through an already installed certificate's thumbprint.");
    }

    [TestMethod]
    public void BothScripts_RestoreTheIsolatedVariantBeforePublishingWithNoRestore()
    {
        foreach (var (scriptPath, packagingProperty) in new[]
        {
            (PortableScriptPath, "KnownFirstWindowsPortablePackaging"),
            (MsixScriptPath, "KnownFirstWindowsMsixPackaging"),
        })
        {
            var script = LoadRepositoryFile(scriptPath);
            var restoreArguments = ExtractArgumentArray(script, "restoreArguments");
            var publishArguments = ExtractArgumentArray(script, "publishArguments");

            Assert.Contains("\"restore\"", restoreArguments);
            Assert.Contains($"-p:{packagingProperty}=true", restoreArguments,
                "The isolated intermediate tree needs its own restore; a shared restore would write the wrong assets file.");
            Assert.Contains("\"--no-restore\"", publishArguments,
                "The publish must consume the dedicated restore rather than restoring again.");

            var restoreIndex = script.IndexOf("$restoreArguments", StringComparison.Ordinal);
            var publishIndex = script.IndexOf("$publishArguments", StringComparison.Ordinal);
            Assert.IsTrue(restoreIndex > 0 && publishIndex > restoreIndex,
                $"{scriptPath} must restore before it publishes.");
        }
    }

    /// <summary>
    /// A <c>--no-restore</c> publish can only consume what the preceding restore resolved.
    /// <c>SelfContained</c> and <c>WindowsAppSDKSelfContained</c> are restore-graph-affecting:
    /// they are what place the .NET runtime pack and the Windows App SDK runtime content into
    /// project.assets.json. Restoring without them and publishing with them yields NETSDK1112
    /// ("the runtime pack ... was not downloaded"), or - worse - succeeds only on a machine where
    /// an unrelated restore happened to populate those packs, making packaging irreproducible.
    /// </summary>
    [TestMethod]
    public void BothScripts_RestoreWithEveryRestoreGraphAffectingPropertyThePublishRequires()
    {
        foreach (var scriptPath in new[] { PortableScriptPath, MsixScriptPath })
        {
            var script = LoadRepositoryFile(scriptPath);
            var restoreProperties = ParseMsBuildProperties(ExtractArgumentArray(script, "restoreArguments"));
            var publishProperties = ParseMsBuildProperties(ExtractArgumentArray(script, "publishArguments"));

            foreach (var property in RestoreGraphAffectingProperties)
            {
                if (!publishProperties.TryGetValue(property, out var publishValue))
                {
                    continue;
                }

                Assert.IsTrue(restoreProperties.TryGetValue(property, out var restoreValue),
                    $"{scriptPath}: publish sets -p:{property}={publishValue} but the restore does not set it at all. " +
                    "A --no-restore publish cannot resolve packages the restore never considered.");
                Assert.AreEqual(publishValue, restoreValue,
                    $"{scriptPath}: -p:{property} must have the same value for restore and publish.");
            }

            // Stated explicitly, so removing either from the restore fails this test loudly even
            // if the allowlist above is ever edited.
            foreach (var required in new[] { "SelfContained", "WindowsAppSDKSelfContained" })
            {
                Assert.IsTrue(restoreProperties.TryGetValue(required, out var value) && value == "true",
                    $"{scriptPath}: the restore must set -p:{required}=true.");
                Assert.IsTrue(publishProperties.TryGetValue(required, out var publishValue) && publishValue == "true",
                    $"{scriptPath}: the publish must set -p:{required}=true.");
            }

            Assert.Contains("-p:Configuration=Release", ExtractArgumentArray(script, "restoreArguments"),
                $"{scriptPath}: the restore must pin the same configuration the publish builds.");
        }
    }

    // === C. Existing actions are untouched ==============================================

    [TestMethod]
    public void WindowsBuildAction_RemainsAnUnpackagedCompileOnlyOperation()
    {
        var body = ExtractFunctionBody(LoadRepositoryFile(LauncherPath), "Invoke-WindowsBuildAction");

        Assert.DoesNotContain("publish", body, "WindowsBuild must not publish.");
        Assert.DoesNotContain("WindowsPackageType", body);
        Assert.DoesNotContain("Appx", body);
        Assert.DoesNotContain("SelfContained", body);
        Assert.DoesNotContain("KnownFirstWindowsPortablePackaging", body);
        Assert.DoesNotContain("KnownFirstWindowsMsixPackaging", body);
        Assert.Contains("'build', $projectPath, '-c', $configuration, '-f', $windowsTargetFramework, '--no-restore'", body);
    }

    [TestMethod]
    public void ValidateAllAction_KeepsItsExactFiveStepMatrixAndNoPackagingBehaviour()
    {
        var body = ExtractFunctionBody(LoadRepositoryFile(LauncherPath), "Invoke-ValidateAllAction");

        foreach (var stateKey in new[]
        {
            "'Test'", "'WindowsBuild-Debug'", "'WindowsBuild-Release'",
            "'AndroidBuildValidation-Debug'", "'AndroidBuildValidation-Release'",
        })
        {
            Assert.Contains($"StateKey = {stateKey}", body, $"ValidateAll must keep its {stateKey} step.");
        }

        Assert.AreEqual(5, Regex.Matches(body, @"StateKey\s*=\s*'").Count,
            "ValidateAll must keep exactly its five existing steps.");

        Assert.DoesNotContain("publish", body, "ValidateAll must never publish or package.");
        Assert.DoesNotContain("WindowsPackageType", body);
        Assert.DoesNotContain("Appx", body);
        Assert.DoesNotContain("WindowsPortablePackage", body);
        Assert.DoesNotContain("WindowsMsixPackage", body);
    }

    // === D. Channel boundaries ============================================================

    [TestMethod]
    public void WindowsDistributionPaths_NeverInvokeAndroidBuildingOrPackaging()
    {
        var launcher = LoadRepositoryFile(LauncherPath);

        foreach (var body in new[]
        {
            ExtractFunctionBody(launcher, "Invoke-WindowsPortablePackageAction"),
            ExtractFunctionBody(launcher, "Invoke-WindowsMsixPackageAction"),
            LoadRepositoryFile(PortableScriptPath),
            LoadRepositoryFile(MsixScriptPath),
        })
        {
            foreach (var forbidden in new[]
            {
                "net10.0-android", ".aab", ".apk", "AndroidPackageFormats", "AndroidKeyStore",
                "jarsigner", "KNOWNFIRST_ANDROID_SIGNING_PASSWORD",
                "publish-android-test-packages.ps1", "publish-google-play-bundle.ps1",
            })
            {
                Assert.DoesNotContain(forbidden, body,
                    $"A Windows distribution path must never reference Android packaging ({forbidden}).");
            }
        }
    }

    [TestMethod]
    public void WindowsDistributionPaths_NeverInstallUploadOrOperateADevice()
    {
        foreach (var script in new[] { LoadRepositoryFile(PortableScriptPath), LoadRepositoryFile(MsixScriptPath) })
        {
            foreach (var forbidden in new[]
            {
                "Add-AppxPackage", "Add-AppxProvisionedPackage", "Start-Process",
                "Invoke-WebRequest", "Invoke-RestMethod", "curl", "StoreBroker",
                "appinstaller", "adb", "Setup.exe", "msiexec", "WACK", "appcert",
            })
            {
                Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase,
                    $"Creating an artifact must never install, launch, upload, or publish it ({forbidden}).");
            }
        }
    }

    [TestMethod]
    public void MsixScript_ContainsNoSecretBearingCertificatePropertiesAndCreatesNoCertificate()
    {
        var script = LoadRepositoryFile(MsixScriptPath);
        var launcher = LoadRepositoryFile(LauncherPath);

        foreach (var forbidden in new[]
        {
            "PackageCertificateKeyFile", "PackageCertificatePassword",
            "New-SelfSignedCertificate", "Import-PfxCertificate", "Import-Certificate",
            ".pfx", "signtool", "CertUtil",
        })
        {
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase,
                $"Signing material must stay outside the repository ({forbidden}).");
            Assert.DoesNotContain(forbidden, launcher, StringComparison.OrdinalIgnoreCase,
                $"Signing material must stay outside the launcher ({forbidden}).");
        }
    }

    [TestMethod]
    public void Launcher_NeverHandlesTheSigningThumbprintItself()
    {
        var launcher = LoadRepositoryFile(LauncherPath);

        // The launcher prints every command it runs and records parameter signatures in
        // artifacts\launcher-state. It must therefore forward only the signing MODE and let the
        // publishing script read the thumbprint from the environment itself, so the value can
        // never reach a printed command line, a launcher log, or a state record.
        Assert.DoesNotContain(ThumbprintEnvironmentVariable, launcher,
            "The launcher must not read or forward the signing thumbprint.");
        Assert.DoesNotContain("Thumbprint", launcher,
            "The launcher must not handle certificate thumbprints at all.");

        var msixAction = ExtractFunctionBody(launcher, "Invoke-WindowsMsixPackageAction");
        Assert.Contains("MsixSigning", msixAction,
            "The launcher forwards only the signing mode.");
    }

    // === E. Launcher state, outputs and reuse ==============================================

    [TestMethod]
    public void PortableAction_ExpectsBothTheArchiveAndItsChecksumSidecar()
    {
        var body = ExtractFunctionBody(LoadRepositoryFile(LauncherPath), "Invoke-WindowsPortablePackageAction");

        Assert.Contains("$checksumPath = \"$zipPath.sha256.txt\"", body,
            "The sidecar path must be derived from the archive path so the launcher's sidecar re-verification applies.");
        AssertOutputFilesAre(body, "$zipPath, $checksumPath", "WindowsPortablePackage");
    }

    [TestMethod]
    public void MsixAction_ExpectsBothThePackageAndItsChecksumSidecar()
    {
        var body = ExtractFunctionBody(LoadRepositoryFile(LauncherPath), "Invoke-WindowsMsixPackageAction");

        Assert.Contains("$checksumPath = \"$msixPath.sha256.txt\"", body,
            "The sidecar path must be derived from the package path so the launcher's sidecar re-verification applies.");
        AssertOutputFilesAre(body, "$msixPath, $checksumPath", "WindowsMsixPackage");
    }

    [TestMethod]
    public void StateKeys_AreDistinctPerChannelAndPerSigningModeAndDoNotCollideWithExistingKeys()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var portable = ExtractFunctionBody(launcher, "Invoke-WindowsPortablePackageAction");
        var msix = ExtractFunctionBody(launcher, "Invoke-WindowsMsixPackageAction");

        Assert.Contains("-StateKey 'WindowsPortablePackage-Release'", portable);
        Assert.DoesNotContain("WindowsMsixPackage-", portable,
            "The portable channel must never read or write an MSIX state record.");

        Assert.Contains("\"WindowsMsixPackage-Release-$MsixSigning\"", msix,
            "The MSIX state key must include the signing mode so a signed and an unsigned result can never be interchanged.");
        Assert.DoesNotContain("WindowsPortablePackage-", msix,
            "The MSIX channel must never read or write a portable state record.");

        // The two new keys must not shadow any pre-existing state key.
        foreach (var existingKey in new[]
        {
            "Test", "WindowsBuild-Debug", "WindowsBuild-Release",
            "AndroidBuildValidation-Debug", "AndroidBuildValidation-Release",
            "AndroidTestPackage-", "GooglePlayBundle",
        })
        {
            Assert.DoesNotContain($"-StateKey '{existingKey}'", portable,
                $"The portable channel must not reuse the existing {existingKey} state key.");
            Assert.DoesNotContain($"-StateKey '{existingKey}'", msix,
                $"The MSIX channel must not reuse the existing {existingKey} state key.");
        }
    }

    [TestMethod]
    public void BothActions_InvalidateOnTheLauncherAndTheirOwnPublishingScript()
    {
        var launcher = LoadRepositoryFile(LauncherPath);

        var portable = ExtractFunctionBody(launcher, "Invoke-WindowsPortablePackageAction");
        Assert.Contains("publish-windows-portable.ps1", portable);
        Assert.Contains("knownfirst.ps1", portable);
        Assert.DoesNotContain("publish-windows-msix.ps1", portable);

        var msix = ExtractFunctionBody(launcher, "Invoke-WindowsMsixPackageAction");
        Assert.Contains("publish-windows-msix.ps1", msix);
        Assert.Contains("knownfirst.ps1", msix);
        Assert.DoesNotContain("publish-windows-portable.ps1", msix);

        // Both channels now share scripts/windows-distribution-common.ps1, so editing it must
        // invalidate both reuse records - otherwise a changed naming rule would leave a stale
        // record pointing at an artifact name that can no longer be produced.
        foreach (var (body, ownScript) in new[]
        {
            (portable, "publish-windows-portable.ps1"),
            (msix, "publish-windows-msix.ps1"),
        })
        {
            var relevantScripts = Regex.Match(body, @"\$relevantScripts\s*=\s*@\((?<scripts>[\s\S]*?)\n\s*\)");
            Assert.IsTrue(relevantScripts.Success, "Each action must declare its relevant script hashes.");

            var declared = relevantScripts.Groups["scripts"].Value;
            Assert.Contains("knownfirst.ps1", declared);
            Assert.Contains("$scriptPath", declared, "The channel's own publishing script must be hashed.");
            Assert.Contains("$commonPath", declared,
                "A change to the shared helper must invalidate this channel's reuse record.");

            // ...and those two variables must actually resolve to the intended files.
            Assert.IsTrue(
                body.Contains($"$scriptPath = Join-Path $scriptRoot 'packaging\\{ownScript}'") ||
                body.Contains($"$scriptPath = Join-Path $scriptRoot 'packaging/{ownScript}'"),
                $"Script path must resolve to packaging/{ownScript}.");
            Assert.IsTrue(
                body.Contains("$commonPath = Join-Path $scriptRoot 'packaging\\windows-distribution-common.ps1'") ||
                body.Contains("$commonPath = Join-Path $scriptRoot 'packaging/windows-distribution-common.ps1'"),
                "Common path must resolve to packaging/windows-distribution-common.ps1.");
        }
    }

    [TestMethod]
    public void WhatIf_CreatesNothingAtAllForEitherChannel()
    {
        var launcher = LoadRepositoryFile(LauncherPath);

        foreach (var functionName in new[] { "Invoke-WindowsPortablePackageAction", "Invoke-WindowsMsixPackageAction" })
        {
            var body = ExtractFunctionBody(launcher, functionName);

            var whatIfIndex = body.IndexOf("if ($WhatIf)", StringComparison.Ordinal);
            Assert.IsTrue(whatIfIndex > 0, $"{functionName} must have a WhatIf branch.");

            var closingIndex = body.IndexOf("\n    }", whatIfIndex, StringComparison.Ordinal);
            Assert.IsTrue(closingIndex > whatIfIndex, $"{functionName} WhatIf branch must be delimited.");
            var whatIfBranch = body.Substring(whatIfIndex, closingIndex - whatIfIndex);

            Assert.Contains("[WhatIf] no commands executed.", whatIfBranch);
            Assert.Contains("-Succeeded $true", whatIfBranch);
            foreach (var forbidden in new[]
            {
                "Invoke-KnownFirstCommand", "New-LauncherLogPath", "New-Item",
                "Save-LauncherState", "Compress-Archive", "Move-Item", "Out-File", "Set-Content",
            })
            {
                Assert.DoesNotContain(forbidden, whatIfBranch,
                    $"{functionName} WhatIf must not {forbidden}.");
            }

            // The WhatIf branch must return before any of the real work is reached.
            Assert.IsTrue(
                whatIfIndex < body.IndexOf("Invoke-KnownFirstCommand", StringComparison.Ordinal),
                $"{functionName} must evaluate WhatIf before running any command.");
        }
    }

    [TestMethod]
    public void InvokeKnownFirstCommand_PreservesChildOutputInTheLogWhenTheChildScriptThrows()
    {
        var root = CreateTemporaryProjectRoot();
        try
        {
            var childScriptPath = Path.Combine(root, "synthetic-child.ps1");
            File.WriteAllText(childScriptPath, """
                Write-Output 'SYNTHETIC_STDOUT_LINE_1'
                Write-Output 'SYNTHETIC_STDOUT_LINE_2'
                throw 'Simulated terminating child failure.'
                """);

            var logPath = Path.Combine(root, "test-launcher.log");
            var function = ExtractFunctionBody(LoadRepositoryFile(LauncherPath), "Invoke-KnownFirstCommand");

            var harness = new StringBuilder();
            harness.AppendLine("$ErrorActionPreference = 'Stop'");
            harness.AppendLine($"function Invoke-KnownFirstCommand {{{function}");
            harness.AppendLine("}");
            harness.AppendLine($"$result = Invoke-KnownFirstCommand -StepName 'TestStep' -FilePath '{childScriptPath.Replace("'", "''")}' -CommandArguments @{{}} -LogPath '{logPath.Replace("'", "''")}'");
            harness.AppendLine("Write-Output '---RESULT---'");
            harness.AppendLine("Write-Output \"SUCCEEDED:$($result.Succeeded)\"");
            harness.AppendLine("Write-Output \"EXITCODE:$($result.ExitCode)\"");
            harness.AppendLine("Write-Output \"ERROR:$($result.ErrorMessage)\"");

            var processResult = RunHarness(harness.ToString(), workingDirectory: root);

            Assert.IsTrue(File.Exists(logPath), "The launcher log file must be created.");
            var logContent = File.ReadAllText(logPath);

            Assert.Contains("SYNTHETIC_STDOUT_LINE_1", logContent,
                "The log must preserve child stdout emitted before the terminating error.");
            Assert.Contains("SYNTHETIC_STDOUT_LINE_2", logContent,
                "The log must preserve all child stdout lines emitted before the terminating error.");
            Assert.Contains("ERROR: Simulated terminating child failure.", logContent,
                "The log must contain the final ERROR record.");

            Assert.Contains("SUCCEEDED:False", processResult.StandardOutput,
                "The structured result must indicate failure.");
            Assert.Contains("ERROR:Simulated terminating child failure.", processResult.StandardOutput,
                "The structured result must report the exception error message.");
        }
        finally
        {
            ForceDeleteDirectory(root);
        }
    }

    // === F. Fail-closed packaging safety ====================================================

    [TestMethod]
    public void BothScripts_FailClosedOnDirtyWorktreeCollisionAndUseLockStagingAndSidecar()
    {
        foreach (var scriptPath in new[] { PortableScriptPath, MsixScriptPath })
        {
            var script = LoadRepositoryFile(scriptPath);

            Assert.Contains("$ErrorActionPreference = \"Stop\"", script);
            Assert.Contains("git", script, "The artifact name carries a commit id, so HEAD must be resolved.");
            Assert.Contains("status --porcelain", script,
                "A commit id in a distribution filename must never describe a modified worktree.");
            Assert.Contains("[System.IO.File]::Open", script, "Concurrent packaging must be serialised.");
            Assert.Contains("'None'", script, "The packaging lock must be opened with FileShare.None.");
            Assert.Contains("NewGuid", script, "Artifacts must be built in a staging directory.");
            Assert.Contains("Final artifact paths already exist", script, "Final paths must fail closed on collision.");
            Assert.Contains("KnownFirstProductVersion", script);
            Assert.Contains("KnownFirstBuildNumber", script);

            Assert.Contains("Move-Item -LiteralPath $staging", script,
                "Finalisation must move the verified staged artifact into place.");
            Assert.IsFalse(
                Regex.IsMatch(script, @"Move-Item[^\r\n]*-Force"),
                $"{scriptPath} must never finalise an artifact with -Force: an existing final artifact must fail closed, never be overwritten.");

            // Same sidecar grammar as the Google Play bundle contract, so the launcher's existing
            // sidecar re-verification in Test-LauncherStateReusable applies unchanged.
            Assert.Contains(@"\A([a-f0-9]{64})[ \t]+([^\r\n]+)\r?\n?\z", script);
            Assert.DoesNotContain(@"\s+", script, "The sidecar parser must not use a loose whitespace class.");

            // Anchored at column zero so the OUTER packaging try/catch/finally is matched rather
            // than an indented inner one (the portable script legitimately uses a nested
            // try/finally to dispose a ZipArchive handle).
            var outerCatch = Regex.Match(script, @"^catch \{", RegexOptions.Multiline);
            var outerFinally = Regex.Match(script, @"^finally \{", RegexOptions.Multiline);
            Assert.IsTrue(outerCatch.Success, $"{scriptPath} must have a top-level catch block.");
            Assert.IsTrue(outerFinally.Success, $"{scriptPath} must have a top-level finally block.");
            Assert.IsTrue(outerFinally.Index > outerCatch.Index,
                $"{scriptPath} must use structured try/catch/finally.");

            Assert.Contains("finalFilesCreated", script.Substring(outerCatch.Index, outerFinally.Index - outerCatch.Index),
                "Files created by a failed invocation must be rolled back.");
            Assert.Contains("Remove-Item", script.Substring(outerFinally.Index),
                "The staging directory must be cleaned up on every exit path.");
            Assert.Contains("throw $failureRecord", script,
                "The original ErrorRecord must be preserved.");
        }
    }

    [TestMethod]
    public void PortableScript_ArchivesTheCompletePublishDirectoryAndProvesItIsSelfContained()
    {
        var script = LoadRepositoryFile(PortableScriptPath);

        Assert.Contains("CreateFromDirectory", script,
            "The complete publish directory must be archived, never a hand-picked file list.");
        Assert.Contains("KnownFirst.exe", script);
        Assert.Contains("KnownFirst.dll", script);
        Assert.Contains("hostfxr.dll", script,
            "A self-contained payload must carry the .NET host; its absence means SelfContained silently did not apply.");
        Assert.Contains("Microsoft.WindowsAppRuntime.Bootstrap.dll", script,
            "A self-contained unpackaged app must carry the Windows App SDK bootstrapper.");

        // The name itself is owned by the shared helper; the architecture and extension are
        // asserted there so there is exactly one place to change them.
        var helper = LoadRepositoryFile(CommonHelperPath);
        Assert.Contains("-win-x64-", helper, "The archive name must carry the architecture.");
        Assert.Contains(".zip", helper);
    }

    // === G. Single source of truth for artifact naming and identity =======================

    [TestMethod]
    public void AllThreeScripts_ConsumeTheSharedArtifactNamingHelper()
    {
        foreach (var path in new[] { LauncherPath, PortableScriptPath, MsixScriptPath })
        {
            Assert.Contains("windows-distribution-common.ps1", LoadRepositoryFile(path),
                $"{path} must dot-source the shared helper rather than derive artifact names itself.");
        }

        var launcher = LoadRepositoryFile(LauncherPath);
        Assert.Contains("Get-KnownFirstPortableArchiveName", launcher);
        Assert.Contains("Get-KnownFirstMsixPackageName", launcher);
        Assert.Contains("Get-KnownFirstMsixSigningMarker", launcher);
        Assert.Contains("Get-KnownFirstMsixIdentityMarker -ManifestPath", launcher);
        Assert.Contains("Get-KnownFirstShortCommitPrefix", launcher);

        Assert.Contains("Get-KnownFirstPortableArchiveName", LoadRepositoryFile(PortableScriptPath));
        Assert.Contains("Get-KnownFirstZipFileEntryCount", LoadRepositoryFile(PortableScriptPath));

        var msix = LoadRepositoryFile(MsixScriptPath);
        Assert.Contains("Get-KnownFirstMsixPackageName", msix);
        Assert.Contains("Get-KnownFirstMsixSigningMarker", msix);
        Assert.Contains("Get-KnownFirstMsixIdentityMarker -ManifestPath", msix);
        Assert.Contains("Select-KnownFirstMsixCandidate", msix);
    }

    [TestMethod]
    public void NoCallerRedefinesTheArtifactNameTemplatesOrTheIdentityAndSigningMarkers()
    {
        var helper = LoadRepositoryFile(CommonHelperPath);

        // The literal templates and marker literals must exist exactly once in the repository,
        // inside the shared helper.
        foreach (var soleTemplate in new[]
        {
            "KnownFirst-$ProductVersion-build$BuildNumber-win-x64-$ShortCommit.zip",
            "KnownFirst-$ProductVersion-build$BuildNumber-x64-$ShortCommit-$SigningMarker-$IdentityMarker.msix",
        })
        {
            Assert.Contains(soleTemplate, helper, "The shared helper owns the artifact name templates.");
        }

        // For the launcher only the two packaging actions are in scope: unrelated launcher code
        // (for example Write-ReuseMessage's own short-SHA formatting) is not artifact naming.
        var launcherScript = LoadRepositoryFile(LauncherPath);
        var callers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{LauncherPath} (Invoke-WindowsPortablePackageAction)"] = ExtractFunctionBody(launcherScript, "Invoke-WindowsPortablePackageAction"),
            [$"{LauncherPath} (Invoke-WindowsMsixPackageAction)"] = ExtractFunctionBody(launcherScript, "Invoke-WindowsMsixPackageAction"),
            [PortableScriptPath] = LoadRepositoryFile(PortableScriptPath),
            [MsixScriptPath] = LoadRepositoryFile(MsixScriptPath),
        };

        foreach (var (callerPath, caller) in callers)
        {
            Assert.DoesNotContain("-win-x64-$shortCommit.zip", caller,
                $"{callerPath} must not re-derive the portable archive name.");
            Assert.DoesNotContain("-x64-$shortCommit-$signingMarker-$identityMarker.msix", caller,
                $"{callerPath} must not re-derive the MSIX package name.");
            Assert.DoesNotContain("{ 'signed' }", caller,
                $"{callerPath} must not re-derive the signing marker with an inline ternary.");
            Assert.DoesNotContain("= 'devidentity'", caller,
                $"{callerPath} must not assign the identity marker value itself.");
            Assert.DoesNotContain("return 'devidentity'", caller,
                $"{callerPath} must not re-derive the identity marker value.");
            Assert.DoesNotContain("return 'storeidentity'", caller,
                $"{callerPath} must not re-derive the identity marker value.");
            Assert.DoesNotContain("Substring(0, 7)", caller,
                $"{callerPath} must not re-derive the short commit prefix.");
        }

        // The helper is the only place these derivations live.
        Assert.Contains("Substring(0, 7)", helper);
        Assert.Contains("return 'devidentity'", helper);
        Assert.Contains("return 'storeidentity'", helper);
        Assert.Contains("return 'signed'", helper);
        Assert.Contains("return 'unsigned'", helper);
    }

    [TestMethod]
    public void SharedHelper_HasNoBuildPackageInstallNetworkOrSigningBehaviour()
    {
        var helper = LoadRepositoryFile(CommonHelperPath);

        foreach (var forbidden in new[]
        {
            "dotnet ", "Invoke-KnownFirstCommand", "New-Item", "Set-Content", "Out-File",
            "Move-Item", "Copy-Item", "Remove-Item", "::CreateFromDirectory", "Compress-Archive",
            "Add-AppxPackage", "Invoke-WebRequest", "Invoke-RestMethod", "StoreBroker",
            "KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT", "PackageCertificate",
            "New-SelfSignedCertificate", ".pfx", "signtool", "& git", "git -C",
            "$env:", "Get-Credential",
        })
        {
            Assert.DoesNotContain(forbidden, helper, StringComparison.OrdinalIgnoreCase,
                $"The shared helper must stay pure and side-effect free ({forbidden}).");
        }
    }

    // --- Behavioral: signing marker and full artifact names -------------------------------

    [TestMethod]
    public void SigningMarker_MapsNoneToUnsignedAndExternalToSigned()
    {
        Assert.AreEqual("unsigned", InvokeHelper("Get-KnownFirstMsixSigningMarker -MsixSigning 'None'"));
        Assert.AreEqual("signed", InvokeHelper("Get-KnownFirstMsixSigningMarker -MsixSigning 'External'"));
    }

    [TestMethod]
    public void MsixArtifactName_CarriesTheCorrectSigningMarkerForBothModes()
    {
        // Exercises the production helper end to end for the current development Store identity.
        foreach (var (signingMode, expectedSuffix) in new[]
        {
            ("None", "-unsigned-devidentity.msix"),
            ("External", "-signed-devidentity.msix"),
        })
        {
            var name = InvokeHelper($"""
                $marker = Get-KnownFirstMsixSigningMarker -MsixSigning '{signingMode}'
                Get-KnownFirstMsixPackageName -ProductVersion '1.0.0-beta.13' -BuildNumber '13' -ShortCommit 'd3a82be' -SigningMarker $marker -IdentityMarker 'devidentity'
                """);

            Assert.AreEqual($"KnownFirst-1.0.0-beta.13-build13-x64-d3a82be{expectedSuffix}", name,
                $"MsixSigning={signingMode} must produce a correctly labelled artifact name.");
        }
    }

    [TestMethod]
    public void PortableArtifactName_IsDeterministicAndCarriesVersionBuildArchitectureAndCommit()
    {
        var name = InvokeHelper(
            "Get-KnownFirstPortableArchiveName -ProductVersion '1.0.0-beta.13' -BuildNumber '13' -ShortCommit 'd3a82be'");

        Assert.AreEqual("KnownFirst-1.0.0-beta.13-build13-win-x64-d3a82be.zip", name);
    }

    [TestMethod]
    public void ShortCommitPrefix_IsAFixedSevenCharacterPrefixOfTheFullSha()
    {
        Assert.AreEqual("d3a82be", InvokeHelper(
            "Get-KnownFirstShortCommitPrefix -HeadSha 'd3a82becf93efacae1ac9a745161837799a74cd8'"));
        Assert.AreEqual(string.Empty, InvokeHelper("Get-KnownFirstShortCommitPrefix -HeadSha 'abc'"));
    }

    // --- Behavioral: MSIX candidate selection --------------------------------------------

    [TestMethod]
    public void MsixCandidateSelection_FailsClosedOnZeroCandidates()
    {
        var result = RunAgainstSyntheticAppPackages(msixFileNames: []);

        Assert.AreNotEqual(0, result.ExitCode, $"Zero candidates must fail closed. StdOut: {result.StandardOutput}");
        Assert.Contains("no .msix package was found", result.StandardError);
    }

    [TestMethod]
    public void MsixCandidateSelection_SelectsTheSingleCandidate()
    {
        var result = RunAgainstSyntheticAppPackages(["KnownFirst_1.0.13.0_x64.msix"]);

        Assert.AreEqual(0, result.ExitCode, $"Exactly one candidate must be selected. StdErr: {result.StandardError}");
        Assert.Contains("KnownFirst_1.0.13.0_x64.msix", result.StandardOutput);
    }

    [TestMethod]
    public void MsixCandidateSelection_FailsClosedOnMultipleCandidates()
    {
        var result = RunAgainstSyntheticAppPackages(["KnownFirst_1.0.13.0_x64.msix", "KnownFirst_1.0.12.0_x64.msix"]);

        Assert.AreNotEqual(0, result.ExitCode, $"Two candidates must fail closed. StdOut: {result.StandardOutput}");
        Assert.Contains("candidate selection is ambiguous", result.StandardError);
    }

    [TestMethod]
    public void MsixCandidateSelection_FailsClosedWhenNoPackageDirectoryExists()
    {
        var result = RunAgainstSyntheticAppPackages(msixFileNames: null);

        Assert.AreNotEqual(0, result.ExitCode, $"A missing package directory must fail closed. StdOut: {result.StandardOutput}");
        Assert.Contains("no package directory was created", result.StandardError);
    }

    // --- Behavioral: ZIP file-entry counting ---------------------------------------------

    [TestMethod]
    public void ZipFileEntryCount_IgnoresEmptyDirectoryEntriesSoAValidPayloadIsNotRejected()
    {
        var root = CreateTemporaryProjectRoot();
        try
        {
            // A representative self-contained payload plus one empty directory. Empty directories
            // become their own ZIP entries, which previously made a valid archive look inflated.
            var payload = Path.Combine(root, "publish");
            Directory.CreateDirectory(payload);
            foreach (var file in new[] { "KnownFirst.exe", "KnownFirst.dll", "hostfxr.dll", "Microsoft.WindowsAppRuntime.Bootstrap.dll" })
            {
                File.WriteAllText(Path.Combine(payload, file), "inert placeholder");
            }
            Directory.CreateDirectory(Path.Combine(payload, "nested"));
            File.WriteAllText(Path.Combine(payload, "nested", "resources.pri"), "inert placeholder");
            Directory.CreateDirectory(Path.Combine(payload, "an-empty-directory"));

            var payloadFileCount = Directory.GetFiles(payload, "*", SearchOption.AllDirectories).Length;
            Assert.AreEqual(5, payloadFileCount);

            var archivePath = Path.Combine(root, "payload.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(payload, archivePath);

            // Precondition: the raw entry count really does exceed the file count, so this test
            // would have caught the previous equality check.
            using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
            {
                Assert.IsTrue(archive.Entries.Count > payloadFileCount,
                    "Precondition: the empty directory must add an extra raw ZIP entry.");
            }

            var counted = InvokeHelper($"Get-KnownFirstZipFileEntryCount -ArchivePath '{archivePath.Replace("'", "''")}'");

            Assert.AreEqual(payloadFileCount.ToString(), counted,
                "Only file entries may be counted, so an empty directory cannot fail a valid payload.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // === G. Behavioral fragments (executed verbatim from the production scripts) ============

    [TestMethod]
    public void ExternalSigningThumbprint_FailsClosedWhenMissing()
    {
        var result = RunExternalSigningContract(thumbprint: null);

        Assert.AreNotEqual(0, result.ExitCode,
            $"A missing thumbprint must fail closed. StdOut: {result.StandardOutput}");
        Assert.Contains("The external MSIX signing thumbprint is invalid or missing", result.StandardError);
    }

    [TestMethod]
    public void ExternalSigningThumbprint_FailsClosedWhenMalformed()
    {
        foreach (var malformed in new[] { "not-a-thumbprint", "ABC123", new string('A', 39), new string('A', 41), new string('Z', 40) })
        {
            var result = RunExternalSigningContract(malformed);

            Assert.AreNotEqual(0, result.ExitCode,
                $"Thumbprint '{malformed}' must be rejected. StdOut: {result.StandardOutput}");
            Assert.Contains("The external MSIX signing thumbprint is invalid or missing", result.StandardError);
        }
    }

    [TestMethod]
    public void ExternalSigningThumbprint_AcceptsExactlyFortyHexadecimalCharacters()
    {
        var result = RunExternalSigningContract("DE8B962E7BF797CB48CCF66C8BCACE65C6585E2F");

        Assert.AreEqual(0, result.ExitCode,
            $"A valid 40-character hexadecimal thumbprint must be accepted. StdErr: {result.StandardError}");
        Assert.Contains("THUMBPRINT_ACCEPTED", result.StandardOutput);
    }

    [TestMethod]
    public void UnsignedMode_NeverReadsTheSigningEnvironmentVariableAtAll()
    {
        var result = RunExternalSigningContract(thumbprint: null, signingMode: "None");

        Assert.AreEqual(0, result.ExitCode,
            $"The default unsigned mode must not require any external signing input. StdErr: {result.StandardError}");
        Assert.Contains("THUMBPRINT_ACCEPTED", result.StandardOutput);
    }

    /// <summary>
    /// Every required Partner Center identity component must be inspected. MAUI substitutes the
    /// package Identity Name from <c>$(ApplicationId)</c>, which is a development identity and not
    /// a name reserved in Partner Center, and it never substitutes Publisher or
    /// PublisherDisplayName at all. Classification must therefore fail safe: any single remaining
    /// placeholder yields <c>devidentity</c>.
    /// </summary>
    [TestMethod]
    public void StoreIdentityClassification_TreatsAnyRemainingPlaceholderComponentAsDevelopmentIdentity()
    {
        const string realName = "ExampleReservedStoreName";
        const string realPublisher = "CN=ExamplePublisherIdFromPartnerCenter";
        const string realDisplayName = "Example Publisher Display Name";

        foreach (var (identityName, publisher, publisherDisplayName, expected, description) in new[]
        {
            // Historical template identity remains a supported classifier fixture.
            ("maui-package-name-placeholder", "CN=User Name", "User Name", "devidentity", "historical template placeholders"),
            // A stable runtime publisher does not supply a Partner Center signing identity.
            ("maui-package-name-placeholder", "CN=User Name", "Tachiguro", "devidentity", "stable publisher in source manifest"),
            // Synthetic generated-manifest identity after MAUI substitutes ApplicationId.
            ("com.tachiguro.knownfirst", "CN=User Name", "Tachiguro", "devidentity", "stable publisher with generated package name"),
            // 2. real Publisher but the Identity Name is still the MAUI placeholder
            ("maui-package-name-placeholder", realPublisher, realDisplayName, "devidentity", "placeholder Identity Name"),
            // 3. real Identity Name but the Publisher is still the template placeholder
            (realName, "CN=User Name", realDisplayName, "devidentity", "placeholder Publisher"),
            // 3b. real Identity Name and Publisher but placeholder PublisherDisplayName
            (realName, realPublisher, "User Name", "devidentity", "placeholder PublisherDisplayName"),
            // 4. all three explicitly non-placeholder
            (realName, realPublisher, realDisplayName, "storeidentity", "all real identity values"),
        })
        {
            var marker = RunStoreIdentityClassification(identityName, publisher, publisherDisplayName);

            Assert.AreEqual(expected, marker,
                $"{description}: Identity Name='{identityName}', Publisher='{publisher}', " +
                $"PublisherDisplayName='{publisherDisplayName}' must classify as {expected}.");
        }
    }

    [TestMethod]
    public void StoreIdentityClassification_TreatsMissingOrEmptyIdentityComponentsAsDevelopmentIdentity()
    {
        // Fail safe: an incomplete manifest must never be presented as Store-submission ready.
        Assert.AreEqual("devidentity", RunStoreIdentityClassification(
            "ExampleReservedStoreName", "CN=ExamplePublisherIdFromPartnerCenter", string.Empty));
        Assert.AreEqual("devidentity", RunStoreIdentityClassification(
            "ExampleReservedStoreName", "   ", "Example Publisher Display Name"));
    }

    [TestMethod]
    public void CurrentRepositoryManifest_ClassifiesAsDevelopmentIdentity()
    {
        // The real manifest, read by the real helper. The application must keep producing
        // devidentity until authoritative Partner Center values exist.
        var manifestPath = Path.Combine(GetRepositoryRoot(), "Platforms", "Windows", "Package.appxmanifest");
        var marker = InvokeHelper($"Get-KnownFirstMsixIdentityMarker -ManifestPath '{manifestPath.Replace("'", "''")}'");

        Assert.AreEqual("devidentity", marker,
            "The current manifest still carries placeholder Partner Center identity values.");
    }

    [TestMethod]
    public void MsixScript_ReportsThePlaceholderIdentityExplicitlyRatherThanSilentlyProceeding()
    {
        var script = LoadRepositoryFile(MsixScriptPath);

        Assert.Contains("STORE_IDENTITY_PLACEHOLDER", script);
        Assert.Contains("NOT a Microsoft Store submission candidate", script);
    }

    [TestMethod]
    public void CurrentRepositoryManifest_UsesStableRuntimePublisherAndPreservesDevelopmentPackageIdentity()
    {
        // MAUI derives unpackaged PublisherName metadata from PublisherDisplayName. This source
        // contract does not prove the generated assembly metadata or the actual runtime path.
        var manifest = XDocument.Parse(LoadRepositoryFile("Platforms/Windows/Package.appxmanifest"));
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var package = manifest.Root!;
        var identity = package.Element(ns + "Identity")!;

        Assert.AreEqual("Tachiguro", (string?)package.Element(ns + "Properties")?.Element(ns + "PublisherDisplayName"));
        Assert.AreEqual("CN=User Name", (string?)identity.Attribute("Publisher"),
            "The development MSIX signing identity is separate from the unpackaged runtime publisher.");
        Assert.AreEqual("maui-package-name-placeholder", (string?)identity.Attribute("Name"),
            "MAUI must continue substituting ApplicationId into the generated manifest.");
        Assert.AreEqual("0.0.0.0", (string?)identity.Attribute("Version"));
    }

    // === H. Windows PowerShell 5.1 HEAD-resolution exit-code contract ========================

    /// <summary>
    /// Regression coverage for the observed WindowsPortablePackage runtime failure
    /// (<c>ERROR: Could not resolve HEAD for C:\Dev\KnownFirst.</c>).
    ///
    /// Under Windows PowerShell 5.1 - the edition the canonical launcher runs under -
    /// <c>Select-Object -First 1</c> stops the upstream pipeline as soon as it holds its single
    /// object. If the native git process has not exited at that instant, PowerShell terminates it
    /// and sets <c>$LASTEXITCODE</c> to <c>-1</c>, even though the SHA itself was captured
    /// correctly. Observing <c>$LASTEXITCODE</c> after the pipeline therefore reports a spurious
    /// failure, and packaging aborts before restore and publish.
    ///
    /// The shim makes that otherwise timing-dependent window deterministic: it prints a valid SHA
    /// and then deliberately stays alive. A pipelined implementation is therefore always stopped
    /// mid-flight, while an implementation that captures the native exit code before selecting
    /// always succeeds.
    /// </summary>
    [TestMethod]
    public void HeadResolution_ResolvesTheShortCommitWhenTheGitProcessHasNotYetExited()
    {
        foreach (var scriptPath in new[] { PortableScriptPath, MsixScriptPath })
        {
            var result = RunHeadResolution(scriptPath, new GitShimBehavior());

            Assert.AreEqual(0, result.ExitCode,
                $"{scriptPath}: HEAD resolution must survive a git process that has not yet exited when " +
                $"the pipeline is stopped.\nStdOut: {result.StandardOutput}\nStdErr: {result.StandardError}");
            Assert.AreEqual(FixtureShortCommit, result.StandardOutput.Trim(),
                $"{scriptPath}: the seven-character artifact identity must be derived from the resolved SHA.");
        }
    }

    /// <summary>
    /// Structural guard for the defect class itself, so it cannot silently return to either channel.
    /// </summary>
    [TestMethod]
    public void WindowsDistributionScripts_NeverObserveLastExitCodeAfterAPipelinedNativeCommand()
    {
        foreach (var scriptPath in new[] { PortableScriptPath, MsixScriptPath })
        {
            var lines = SplitLines(LoadRepositoryFile(scriptPath));

            for (var index = 0; index < lines.Length; index++)
            {
                // A native invocation piped into another pipeline element must never be followed by
                // a $LASTEXITCODE observation: the downstream element can stop the upstream process
                // and replace the real exit code with the termination code.
                if (Regex.IsMatch(lines[index], @"&\s*(git|dotnet)\b[^|]*\|"))
                {
                    Assert.DoesNotContain("$LASTEXITCODE", NextMeaningfulLine(lines, index),
                        $"{scriptPath} line {index + 1}: '{lines[index].Trim()}' pipes a native command and the " +
                        "next statement observes $LASTEXITCODE. Capture the native exit code immediately after " +
                        "the invocation, then select from the captured output.");
                }

                // ...and every native git invocation the script relies on must capture its exit code
                // on the immediately following statement, before anything else can overwrite it.
                if (Regex.IsMatch(lines[index], @"^\s*\$\w+\s*=\s*&\s*git\b"))
                {
                    var next = NextMeaningfulLine(lines, index);
                    Assert.IsTrue(
                        Regex.IsMatch(next, @"^\s*\$\w+\s*=\s*\$LASTEXITCODE\s*$"),
                        $"{scriptPath} line {index + 1}: '{lines[index].Trim()}' must be followed immediately by a " +
                        $"'$<name> = $LASTEXITCODE' capture, but the next statement is '{next.Trim()}'.");
                }
            }
        }

        // The correction must not migrate process execution into the deliberately pure shared helper.
        var helper = LoadRepositoryFile(CommonHelperPath);
        foreach (var forbidden in new[] { "& git", "git -C", "& dotnet" })
        {
            Assert.DoesNotContain(forbidden, helper, StringComparison.OrdinalIgnoreCase,
                $"The shared helper must stay process-free ({forbidden}).");
        }
    }

    /// <summary>
    /// Characterization/hardening: the fail-closed Git identity semantics both channels depend on.
    /// A distribution filename carries a commit identity, so anything short of a clean worktree and
    /// a full 40-character SHA must abort before any artifact is produced.
    /// </summary>
    [TestMethod]
    public void HeadResolution_FailsClosedOnDirtyWorktreeNonZeroGitExitAndNonFullSha()
    {
        foreach (var scriptPath in new[] { PortableScriptPath, MsixScriptPath })
        {
            // 1. clean worktree and a full SHA -> the seven-character artifact identity.
            var resolved = RunHeadResolution(scriptPath, new GitShimBehavior(DelayHeadExit: false));
            Assert.AreEqual(0, resolved.ExitCode,
                $"{scriptPath}: a clean worktree with a full SHA must resolve.\nStdErr: {resolved.StandardError}");
            Assert.AreEqual(FixtureShortCommit, resolved.StandardOutput.Trim(),
                $"{scriptPath}: the resolved identity must be the first seven SHA characters.");

            // 2. dirty worktree -> a commit identity must never describe modified content.
            AssertHeadResolutionFailsClosed(
                scriptPath,
                new GitShimBehavior(StatusOutput: " M Components/Pages/Settings.razor"),
                "The worktree is not clean.");

            // 3. rev-parse failure -> HEAD is genuinely unresolvable.
            AssertHeadResolutionFailsClosed(
                scriptPath,
                new GitShimBehavior(HeadSha: "", HeadExitCode: 128, DelayHeadExit: false),
                "Could not resolve HEAD for");

            // 4. an abbreviated SHA -> the artifact identity must come from a full commit SHA.
            AssertHeadResolutionFailsClosed(
                scriptPath,
                new GitShimBehavior(HeadSha: "0123456", DelayHeadExit: false),
                "HEAD did not resolve to a full commit SHA.");

            // 5. git status failure (for example dubious ownership) -> the worktree state is unknown.
            AssertHeadResolutionFailsClosed(
                scriptPath,
                new GitShimBehavior(StatusExitCode: 128),
                "Could not determine the Git worktree state for");
        }
    }

    /// <summary>
    /// Characterization/hardening: binds the shim-based contract above to real git behavior, using
    /// a disposable temporary repository only. KnownFirst history, the KnownFirst worktree, and all
    /// user data are untouched.
    /// </summary>
    [TestMethod]
    public void HeadResolution_MatchesRealGitForAnIsolatedTemporaryRepository()
    {
        var repository = CreateTemporaryProjectRoot();
        try
        {
            RunGitFixtureCommand(repository, "init", "-q", "-b", "main");
            RunGitFixtureCommand(
                repository,
                "-c", "user.name=KnownFirst Test",
                "-c", "user.email=test@knownfirst.invalid",
                "-c", "commit.gpgsign=false",
                "commit", "-q", "--allow-empty", "--no-gpg-sign",
                "-m", "isolated head-resolution fixture");

            var expectedShortCommit = RunGitFixtureCommand(repository, "rev-parse", "HEAD").Trim()[..7];

            foreach (var scriptPath in new[] { PortableScriptPath, MsixScriptPath })
            {
                var clean = RunHeadResolution(scriptPath, projectRoot: repository);

                Assert.AreEqual(0, clean.ExitCode,
                    $"{scriptPath}: a clean real repository must resolve.\nStdErr: {clean.StandardError}");
                Assert.AreEqual(expectedShortCommit, clean.StandardOutput.Trim(),
                    $"{scriptPath}: the resolved identity must match real git for the same repository.");
            }

            File.WriteAllText(Path.Combine(repository, "uncommitted.txt"), "makes the worktree dirty");

            foreach (var scriptPath in new[] { PortableScriptPath, MsixScriptPath })
            {
                var dirty = RunHeadResolution(scriptPath, projectRoot: repository);

                Assert.AreNotEqual(0, dirty.ExitCode,
                    $"{scriptPath}: a dirty real repository must fail closed.\nStdOut: {dirty.StandardOutput}");
                Assert.Contains("The worktree is not clean.", dirty.StandardError,
                    $"{scriptPath}: the existing clean-worktree rejection must be preserved for real git.");
            }
        }
        finally
        {
            ForceDeleteDirectory(repository);
        }
    }

    // === Helpers ==============================================================================

    private static void AssertOutputFilesAre(string functionBody, string expectedOutputs, string channel)
    {
        var matches = Regex.Matches(functionBody, @"-OutputFiles\s+@\((?<outputs>[^\)]+)\)");
        Assert.IsTrue(matches.Count >= 2,
            $"{channel} must declare its expected outputs for both the reuse check and the state record.");

        foreach (Match match in matches)
        {
            Assert.AreEqual(expectedOutputs, match.Groups["outputs"].Value.Trim(),
                $"{channel} must expect exactly the artifact and its checksum sidecar.");
        }
    }

    private static string[] ExtractValidateSetValues(string script, string parameterName)
    {
        var match = Regex.Match(
            script,
            @"\[ValidateSet\((?<values>[^\)]*)\)\][\s\r\n]*\[string\]\$" + Regex.Escape(parameterName) + @"\b");

        Assert.IsTrue(match.Success, $"A ValidateSet for -{parameterName} must exist.");

        return Regex.Matches(match.Groups["values"].Value, @"'(?<value>[^']+)'")
            .Select(value => value.Groups["value"].Value)
            .ToArray();
    }

    private static string ExtractFunctionBody(string script, string functionName)
    {
        var match = Regex.Match(
            script,
            @"function\s+" + Regex.Escape(functionName) + @"\s*\{(?<body>[\s\S]*?)\n\}",
            RegexOptions.IgnoreCase);

        Assert.IsTrue(match.Success, $"The function {functionName} must exist.");
        return match.Groups["body"].Value;
    }

    private static string ExtractArgumentArray(string script, string variableName)
    {
        var match = Regex.Match(script, @"\$" + Regex.Escape(variableName) + @"\s*=\s*@\((?<args>[\s\S]*?)\n\s*\)");

        Assert.IsTrue(match.Success, $"The ${variableName} array declaration must exist.");
        return match.Groups["args"].Value;
    }

    private const string ExternalSigningStartMarker = "# --- BEGIN EXTERNAL SIGNING INPUT CONTRACT ---";
    private const string ExternalSigningEndMarker = "# --- END EXTERNAL SIGNING INPUT CONTRACT ---";
    private static string ExtractMarkedFragment(string scriptPath, string startMarker, string endMarker)
    {
        var script = LoadRepositoryFile(scriptPath);

        var start = script.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Production start marker '{startMarker}' was not found in {scriptPath}.");

        var end = script.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, $"Production end marker '{endMarker}' was not found after the start marker.");

        return script.Substring(start, end - start + endMarker.Length);
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);

    private static PowerShellResult RunExternalSigningContract(string? thumbprint, string signingMode = "External")
    {
        var fragment = ExtractMarkedFragment(MsixScriptPath, ExternalSigningStartMarker, ExternalSigningEndMarker);

        Assert.Contains(ThumbprintEnvironmentVariable, fragment,
            "The extracted fragment must be the real external-signing input contract.");

        var harness = new StringBuilder();
        harness.AppendLine("$ErrorActionPreference = \"Stop\"");
        harness.AppendLine($"$MsixSigning = '{signingMode}'");
        harness.AppendLine(fragment);
        harness.AppendLine("Write-Output \"THUMBPRINT_ACCEPTED\"");

        return RunHarness(harness.ToString(), environment: thumbprint is null
            ? null
            : (ThumbprintEnvironmentVariable, thumbprint));
    }

    /// <summary>
    /// Runs the real shared helper against a synthetic manifest and returns the classification.
    /// </summary>
    private static string RunStoreIdentityClassification(
        string identityName,
        string publisher,
        string publisherDisplayName)
    {
        var root = CreateTemporaryProjectRoot();
        try
        {
            var manifestPath = Path.Combine(root, "Package.appxmanifest");
            File.WriteAllText(
                manifestPath,
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Identity Name="{identityName}" Publisher="{publisher}" Version="0.0.0.0" />
                  <Properties>
                    <DisplayName>$placeholder$</DisplayName>
                    <PublisherDisplayName>{publisherDisplayName}</PublisherDisplayName>
                  </Properties>
                </Package>
                """);

            return InvokeHelper($"Get-KnownFirstMsixIdentityMarker -ManifestPath '{manifestPath.Replace("'", "''")}'");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Runs the real shared candidate-selection logic against a synthetic AppPackages directory.
    /// A null <paramref name="msixFileNames"/> means the directory itself is never created.
    /// </summary>
    private static PowerShellResult RunAgainstSyntheticAppPackages(string[]? msixFileNames)
    {
        var root = CreateTemporaryProjectRoot();
        try
        {
            var appPackages = Path.Combine(root, "AppPackages");
            if (msixFileNames is not null)
            {
                Directory.CreateDirectory(appPackages);
                foreach (var fileName in msixFileNames)
                {
                    var nested = Path.Combine(appPackages, Path.GetFileNameWithoutExtension(fileName) + "_Test");
                    Directory.CreateDirectory(nested);
                    File.WriteAllText(Path.Combine(nested, fileName), "inert placeholder, not a real package");
                }
            }

            var harness = new StringBuilder();
            harness.AppendLine("$ErrorActionPreference = \"Stop\"");
            harness.AppendLine($". '{HelperFullPath().Replace("'", "''")}'");
            harness.AppendLine($"Select-KnownFirstMsixCandidate -AppxPackageDir '{appPackages.Replace("'", "''")}'");

            return RunHarness(harness.ToString(), workingDirectory: root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Dot-sources the real production helper and evaluates <paramref name="statements"/>,
    /// returning trimmed stdout. Any non-zero exit fails the calling test.
    /// </summary>
    private static string InvokeHelper(string statements)
    {
        var harness = new StringBuilder();
        harness.AppendLine("$ErrorActionPreference = \"Stop\"");
        harness.AppendLine($". '{HelperFullPath().Replace("'", "''")}'");
        harness.AppendLine(statements);

        var result = RunHarness(harness.ToString());
        Assert.AreEqual(0, result.ExitCode,
            $"The shared helper harness failed.\nStatements:\n{statements}\nStdErr:\n{result.StandardError}");

        return result.StandardOutput.Trim();
    }

    private static string HelperFullPath() =>
        Path.Combine(GetRepositoryRoot(), CommonHelperPath.Replace('/', Path.DirectorySeparatorChar));

    private static Dictionary<string, string> ParseMsBuildProperties(string argumentArray)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(argumentArray, @"-p:(?<name>[A-Za-z0-9_]+)=(?<value>[^""']*)"))
        {
            properties[match.Groups["name"].Value] = match.Groups["value"].Value;
        }
        return properties;
    }

    private static PowerShellResult RunHarness(
        string harnessScript,
        (string Name, string Value)? environment = null,
        string? workingDirectory = null)
    {
        var harnessRoot = workingDirectory ?? CreateTemporaryProjectRoot();
        var ownsRoot = workingDirectory is null;

        try
        {
            var harnessPath = Path.Combine(harnessRoot, "windows-distribution-contract-harness.ps1");
            File.WriteAllText(harnessPath, harnessScript, new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = harnessRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(harnessPath);

            // Always clear the variable first so a value in the developer's own environment can
            // never make a fail-closed test pass by accident.
            startInfo.Environment.Remove(ThumbprintEnvironmentVariable);
            if (environment is { } value)
            {
                startInfo.Environment[value.Name] = value.Value;
            }

            using var process = Process.Start(startInfo);
            Assert.IsNotNull(process, "powershell.exe could not be started.");

            var standardOutput = process!.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            Assert.IsTrue(process.WaitForExit(120000), "The contract harness did not exit within 120 seconds.");

            return new PowerShellResult(
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }
        finally
        {
            if (ownsRoot)
            {
                Directory.Delete(harnessRoot, recursive: true);
            }
        }
    }

    private static string CreateTemporaryProjectRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "knownfirst-windows-dist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string LoadRepositoryFile(string relativePath)
    {
        var path = Path.Combine(GetRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"File not found: {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the KnownFirst repository root.");
    }

    // --- HEAD-resolution harness ----------------------------------------------------------

    private const string HeadResolutionFunctionName = "Get-KnownFirstShortCommit";
    private const string FixtureHeadSha = "0123456789abcdef0123456789abcdef01234567";
    private const string FixtureShortCommit = "0123456";

    /// <summary>
    /// Deterministic stand-in for git. <paramref name="DelayHeadExit"/> keeps the process alive
    /// after it has written the SHA, which is exactly the window in which Windows PowerShell 5.1
    /// terminates a pipelined native command and overwrites <c>$LASTEXITCODE</c> with <c>-1</c>.
    /// </summary>
    private sealed record GitShimBehavior(
        string StatusOutput = "",
        int StatusExitCode = 0,
        string HeadSha = FixtureHeadSha,
        bool DelayHeadExit = true,
        int HeadExitCode = 0);

    /// <summary>
    /// Executes the real <c>Get-KnownFirstShortCommit</c> function text lifted out of
    /// <paramref name="scriptPath"/> under Windows PowerShell 5.1. Nothing is restored, built,
    /// published, packaged, signed, installed, or uploaded.
    /// </summary>
    private static PowerShellResult RunHeadResolution(
        string scriptPath,
        GitShimBehavior? shim = null,
        string? projectRoot = null)
    {
        var ownedRoot = projectRoot is null ? CreateTemporaryProjectRoot() : null;
        var shimDirectory = shim is null ? null : CreateTemporaryProjectRoot();

        try
        {
            var function = ExtractFunctionBody(LoadRepositoryFile(scriptPath), HeadResolutionFunctionName);

            var harness = new StringBuilder();
            harness.AppendLine("$ErrorActionPreference = \"Stop\"");
            if (shim is not null)
            {
                WriteGitShim(shimDirectory!, shim);
                // Child-process only: this never mutates the caller's or the machine's PATH.
                harness.AppendLine($"$env:Path = '{shimDirectory!.Replace("'", "''")};' + $env:Path");
            }
            harness.AppendLine($". '{HelperFullPath().Replace("'", "''")}'");
            harness.AppendLine($"function {HeadResolutionFunctionName} {{{function}");
            harness.AppendLine("}");
            harness.AppendLine(
                $"{HeadResolutionFunctionName} -ProjectRoot '{(projectRoot ?? ownedRoot!).Replace("'", "''")}'");

            return RunHarness(harness.ToString());
        }
        finally
        {
            if (ownedRoot is not null)
            {
                ForceDeleteDirectory(ownedRoot);
            }
            if (shimDirectory is not null)
            {
                ForceDeleteDirectory(shimDirectory);
            }
        }
    }

    private static void AssertHeadResolutionFailsClosed(
        string scriptPath,
        GitShimBehavior shim,
        string expectedMessage)
    {
        var result = RunHeadResolution(scriptPath, shim);

        Assert.AreNotEqual(0, result.ExitCode,
            $"{scriptPath}: this condition must fail closed.\nStdOut: {result.StandardOutput}");
        Assert.Contains(expectedMessage, result.StandardError,
            $"{scriptPath}: the existing fail-closed message must be preserved.");
    }

    private static void WriteGitShim(string shimDirectory, GitShimBehavior behavior)
    {
        var shim = new StringBuilder();
        shim.AppendLine("@echo off");
        // Arguments are always `-C <root> <subcommand> ...`, so %3 is the subcommand.
        shim.AppendLine("if /I \"%~3\"==\"rev-parse\" goto revparse");
        if (behavior.StatusOutput.Length > 0)
        {
            shim.AppendLine($"echo {behavior.StatusOutput}");
        }
        if (behavior.StatusExitCode != 0)
        {
            shim.AppendLine("echo fatal: simulated git status failure 1>&2");
        }
        shim.AppendLine($"exit /b {behavior.StatusExitCode}");
        shim.AppendLine(":revparse");
        if (behavior.HeadSha.Length > 0)
        {
            shim.AppendLine($"echo {behavior.HeadSha}");
        }
        if (behavior.DelayHeadExit)
        {
            // Stays alive past the pipeline stop without requiring a console (unlike timeout /t).
            shim.AppendLine("ping -n 3 127.0.0.1 >nul");
        }
        if (behavior.HeadExitCode != 0)
        {
            shim.AppendLine("echo fatal: simulated git rev-parse failure 1>&2");
        }
        shim.AppendLine($"exit /b {behavior.HeadExitCode}");

        File.WriteAllText(Path.Combine(shimDirectory, "git.cmd"), shim.ToString(), new UTF8Encoding(false));
    }

    private static string RunGitFixtureCommand(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.IsNotNull(process, "git could not be started.");

        var standardOutput = process!.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        Assert.IsTrue(process.WaitForExit(60000), "The git fixture command did not exit within 60 seconds.");

        var output = standardOutput.GetAwaiter().GetResult();
        Assert.AreEqual(0, process.ExitCode,
            $"git {string.Join(' ', arguments)} exited {process.ExitCode}.\n" +
            $"{output}\n{standardError.GetAwaiter().GetResult()}");

        return output;
    }

    /// <summary>Git object files are read-only, so a plain recursive delete would fail.</summary>
    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    /// <summary>The next statement, skipping blank lines and whole-line comments.</summary>
    private static string NextMeaningfulLine(string[] lines, int index)
    {
        for (var next = index + 1; next < lines.Length; next++)
        {
            var candidate = lines[next].Trim();
            if (candidate.Length == 0 || candidate.StartsWith('#'))
            {
                continue;
            }

            return lines[next];
        }

        return string.Empty;
    }
}
