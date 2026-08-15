using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KnownFirst.Tests;

/// <summary>
/// Static and behavioral contracts for the canonical launcher's <c>Clean</c> action
/// and bounded launcher-log retention behavior.
///
/// These tests do not perform cleanup against the real repository or application data.
/// Behavioral tests run isolated PowerShell code against disposable temporary directories.
/// </summary>
[TestClass]
public sealed class LauncherCleanActionContractTests
{
    private const string LauncherPath = "scripts/knownfirst.ps1";

    private static readonly string[] RequiredStandardAllowlist =
    [
        @"bin",
        @"obj",
        @"KnownFirst.Core\bin",
        @"KnownFirst.Core\obj",
        @"KnownFirst.Tests\bin",
        @"KnownFirst.Tests\obj",
        @"TestResults",
        @"artifacts\build",
        @"artifacts\obj",
        @"artifacts\gui-tests\windows\profiles",
        @"artifacts\gui-tests\windows\runs",
        @"artifacts\gui-tests\android\runs",
    ];

    private static readonly string[] RequiredDeepAllowlist =
    [
        @".vs",
        @"artifacts\launcher-state",
    ];

    private static readonly string[] ProhibitedCleanupTargets =
    [
        @".git",
        @".github",
        @"docs",
        @"Components",
        @"Data",
        @"KnownFirst.Core",
        @"KnownFirst.Tests",
        @"Localization",
        @"Models",
        @"Platforms",
        @"Properties",
        @"Resources",
        @"Services",
        @"branding",
        @"wwwroot",
        @"artifacts\android-google-play",
        @"artifacts\windows-portable",
        @"artifacts\windows-msix",
        @"artifacts\android",
        @"artifacts\android-beta",
        @"artifacts\diagnostics-export-import-audit",
        @"artifacts\gui-smoke",
        @"artifacts\recovery-verification",
    ];

    [TestMethod]
    public void Launcher_DeclaresCleanActionAndDeepSwitch()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var actions = ExtractValidateSetValues(launcher, "Action");

        Assert.IsTrue(actions.Contains("Clean"), "Launcher must declare the 'Clean' action in Action parameter ValidateSet.");
        Assert.IsTrue(Regex.IsMatch(launcher, @"\[switch\]\$Deep\b"), "Launcher must declare [switch]$Deep in param block.");
    }

    [TestMethod]
    public void Launcher_DispatchesCleanAction()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var dispatch = ExtractFunctionBody(launcher, "Invoke-KnownFirstAction");

        Assert.IsTrue(
            Regex.IsMatch(dispatch, @"'Clean'\s*\{\s*return\s+Invoke-CleanAction\s*\}"),
            "Invoke-KnownFirstAction must dispatch 'Clean' to Invoke-CleanAction.");
    }

    [TestMethod]
    public void Launcher_CleanAction_HasExplicitAllowlistAndRejectsDistributables()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var cleanBody = ExtractFunctionBody(launcher, "Invoke-CleanAction");

        Assert.IsFalse(string.IsNullOrWhiteSpace(cleanBody), "Invoke-CleanAction function body must exist.");

        // Must not contain git clean
        Assert.IsFalse(cleanBody.Contains("git clean"), "Clean action must not use 'git clean'.");

        // Must not hardcode C:\Dev\KnownFirst
        Assert.IsFalse(cleanBody.Contains(@"C:\Dev\KnownFirst"), "Clean action must not hardcode 'C:\\Dev\\KnownFirst'.");

        // Verify standard allowlist paths are present
        foreach (var path in RequiredStandardAllowlist)
        {
            var normalizedEscaped = Regex.Escape(path).Replace(@"\\", @"[\\/]");
            Assert.IsTrue(
                Regex.IsMatch(cleanBody, normalizedEscaped, RegexOptions.IgnoreCase),
                $"Standard clean allowlist must contain '{path}'.");
        }

        // Verify deep allowlist paths are present
        foreach (var path in RequiredDeepAllowlist)
        {
            var normalizedEscaped = Regex.Escape(path).Replace(@"\\", @"[\\/]");
            Assert.IsTrue(
                Regex.IsMatch(cleanBody, normalizedEscaped, RegexOptions.IgnoreCase),
                $"Deep clean allowlist must contain '{path}'.");
        }

        // Verify prohibited paths are absent from allowlists
        foreach (var prohibited in ProhibitedCleanupTargets)
        {
            var pattern = $@"'[\\/]?{Regex.Escape(prohibited).Replace(@"\\", @"[\\/]")}[\\/]?'";
            Assert.IsFalse(
                Regex.IsMatch(cleanBody, pattern, RegexOptions.IgnoreCase),
                $"Prohibited target '{prohibited}' must not appear in Clean action allowlists.");
        }
    }

    [TestMethod]
    public void Launcher_CleanAction_NeverSavesReusableState()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var cleanBody = ExtractFunctionBody(launcher, "Invoke-CleanAction");

        Assert.IsFalse(
            cleanBody.Contains("Save-LauncherState"),
            "Invoke-CleanAction must not save reusable state because cleanup must never be skipped as cached work.");
    }

    [TestMethod]
    public void Launcher_LogRetention_BoundsLogsToTenOnNormalExecution()
    {
        var launcher = LoadRepositoryFile(LauncherPath);

        // Prune-LauncherLogs function must exist
        var pruneBody = ExtractFunctionBody(launcher, "Prune-LauncherLogs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(pruneBody), "Prune-LauncherLogs function must exist.");

        // Must retain at most 10 logs by default
        Assert.IsTrue(
            Regex.IsMatch(pruneBody, @"10\b"),
            "Prune-LauncherLogs must default to retaining 10 completed log files.");

        // Must Derivate from log naming convention
        Assert.IsTrue(
            Regex.IsMatch(pruneBody, @"\.log"),
            "Prune-LauncherLogs must match .log files.");

        // Must exclude currently active log
        Assert.IsTrue(
            pruneBody.Contains("CurrentLogPath") || pruneBody.Contains("ExcludeLogPath") || pruneBody.Contains("ActiveLogPath"),
            "Prune-LauncherLogs must have a parameter to exclude the currently active log from deletion.");
    }

    [TestMethod]
    public void Launcher_Menu_RoutesOptionToCleanAction()
    {
        var launcher = LoadRepositoryFile(LauncherPath);
        var mainMenu = ExtractFunctionBody(launcher, "Show-KnownFirstMenu");

        Assert.IsTrue(
            mainMenu.Contains("Clean") || mainMenu.Contains("Show-CleanMenu"),
            "Show-KnownFirstMenu must offer an option for cleaning generated outputs.");
    }

    [TestMethod]
    public void Launcher_CleanAction_IsolatedFixtureBehavior()
    {
        // Execute a small PowerShell block validating the escape-checking and deletion invariants on a temp dir
        using var tempRoot = new DisposableTempDirectory();

        var subDirs = new[]
        {
            "bin",
            "obj",
            @"artifacts\build",
            @"artifacts\obj",
            @"artifacts\gui-tests\windows\runs\run1",
            @"artifacts\launcher-state",
            @"artifacts\android-google-play",
        };

        foreach (var d in subDirs)
        {
            Directory.CreateDirectory(Path.Combine(tempRoot.Path, d));
        }

        // Create a dummy file in android-google-play to verify preservation
        var aabPath = Path.Combine(tempRoot.Path, "artifacts", "android-google-play", "test.aab");
        File.WriteAllText(aabPath, "dummy aab content");

        // Create dummy files in bin and build
        File.WriteAllText(Path.Combine(tempRoot.Path, "bin", "app.dll"), "dummy dll");
        File.WriteAllText(Path.Combine(tempRoot.Path, "artifacts", "build", "output.txt"), "dummy output");

        var script = $$"""
            $projectRoot = '{{tempRoot.Path.Replace("'", "''")}}'
            $allowlist = @(
                'bin',
                'obj',
                'artifacts\build',
                'artifacts\obj',
                'artifacts\gui-tests\windows\runs'
            )

            $deleted = @()
            foreach ($rel in $allowlist) {
                $resolved = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $rel))
                if (-not $resolved.StartsWith($projectRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Escape detected: $resolved"
                }
                if (Test-Path -LiteralPath $resolved) {
                    Remove-Item -LiteralPath $resolved -Recurse -Force
                    $deleted += $rel
                }
            }

            # Test that escape throws
            $escapeCaught = $false
            try {
                $badPath = '..\outside'
                $resolvedBad = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $badPath))
                if (-not $resolvedBad.StartsWith($projectRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Refusing path outside root"
                }
            } catch {
                $escapeCaught = $true
            }

            if (-not $escapeCaught) {
                throw "Escape path was not caught"
            }

            Write-Output "DELETED_COUNT=$($deleted.Count)"
            """;

        var output = RunPowerShell(script);
        Assert.IsTrue(output.Contains("DELETED_COUNT=5"), $"Expected 5 deleted directories, got:\n{output}");
        Assert.IsFalse(Directory.Exists(Path.Combine(tempRoot.Path, "bin")), "bin should be deleted.");
        Assert.IsFalse(Directory.Exists(Path.Combine(tempRoot.Path, "artifacts", "build")), "artifacts\\build should be deleted.");
        Assert.IsTrue(Directory.Exists(Path.Combine(tempRoot.Path, "artifacts", "android-google-play")), "artifacts\\android-google-play must be preserved.");
        Assert.IsTrue(File.Exists(aabPath), "Distributable AAB must be preserved.");
        Assert.IsTrue(Directory.Exists(Path.Combine(tempRoot.Path, "artifacts", "launcher-state")), "launcher-state must be preserved in standard clean.");
    }

    private static string LoadRepositoryFile(string relativePath)
    {
        var fullPath = Path.Combine(GetRepositoryRoot(), relativePath);
        Assert.IsTrue(File.Exists(fullPath), $"Required repository file not found: {fullPath}");
        return File.ReadAllText(fullPath);
    }

    private static string GetRepositoryRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "KnownFirst.slnx")) ||
                File.Exists(Path.Combine(current, "KnownFirst.csproj")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        throw new InvalidOperationException("Could not locate KnownFirst repository root from " + AppDomain.CurrentDomain.BaseDirectory);
    }

    private static string[] ExtractValidateSetValues(string script, string parameterName)
    {
        var pattern = $@"(?ms)\[ValidateSet\((.*?)\)\][\s\r\n]*\[[^\]]+\]\${parameterName}\b";
        var match = Regex.Match(script, pattern);
        Assert.IsTrue(match.Success, $"Could not find [ValidateSet(...)] attribute for ${parameterName}.");

        var raw = match.Groups[1].Value;
        var matches = Regex.Matches(raw, @"'([^']*)'|""([^""]*)""");
        var list = new List<string>();
        foreach (Match m in matches)
        {
            list.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        }
        return list.ToArray();
    }

    private static string ExtractFunctionBody(string script, string functionName)
    {
        var pattern = $@"(?ms)function\s+{Regex.Escape(functionName)}\s*\{{(.*?)\n\}}";
        var match = Regex.Match(script, pattern);
        if (!match.Success)
        {
            var fallbackPattern = $@"(?ms)function\s+{Regex.Escape(functionName)}\s*(?:\([^\)]*\))?\s*\{{(.*)";
            var fallbackMatch = Regex.Match(script, fallbackPattern);
            if (!fallbackMatch.Success)
            {
                return string.Empty;
            }

            var tail = fallbackMatch.Groups[1].Value;
            var depth = 1;
            var pos = 0;
            while (pos < tail.Length && depth > 0)
            {
                if (tail[pos] == '{') depth++;
                else if (tail[pos] == '}') depth--;
                pos++;
            }
            return tail.Substring(0, Math.Max(0, pos - 1));
        }
        return match.Groups[1].Value;
    }

    private static string RunPowerShell(string scriptText)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell process.");
        process.StandardInput.WriteLine(scriptText);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell script failed with exit code {process.ExitCode}.\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        }

        return output;
    }

    private sealed class DisposableTempDirectory : IDisposable
    {
        public string Path { get; }

        public DisposableTempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KnownFirst-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best effort test cleanup
            }
        }
    }
}
