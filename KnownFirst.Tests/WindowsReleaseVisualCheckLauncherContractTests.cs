using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public class WindowsReleaseVisualCheckLauncherContractTests
{
    [TestMethod]
    public void LauncherScript_ExistsAndEnforcesSafetyContracts()
    {
        var root = FindRepositoryRoot();
        var launcherPath = Path.Combine(root, "scripts", "gui-tests", "windows", "run-windows-release-visual-check.ps1");

        Assert.IsTrue(File.Exists(launcherPath), "The Windows Release visual check launcher script must exist.");

        var script = File.ReadAllText(launcherPath);

        Assert.Contains("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("KNOWNFIRST_GUI_TEST_ROOT", script);
        Assert.Contains("artifacts", script);
        Assert.Contains("gui-tests", script);
        Assert.Contains("windows", script);
        Assert.Contains("profiles", script);
        Assert.Contains("bin\\Release\\net10.0-windows10.0.19041.0\\win-x64\\KnownFirst.exe", script);

        // Must never build, test, package, or validate as a side effect
        Assert.DoesNotContain("dotnet build", script);
        Assert.DoesNotContain("dotnet test", script);
        Assert.DoesNotContain("dotnet publish", script);
        Assert.DoesNotContain("ValidateAll", script);
        Assert.DoesNotContain("Validate-All", script);
        Assert.DoesNotContain("knownfirst.ps1", script);

        // Must preserve real profile integrity and avoid destructive operations
        Assert.DoesNotContain("Remove-Item -Recurse $localAppData", script);
        Assert.DoesNotContain("[Environment]::SetEnvironmentVariable", script);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KnownFirst.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the KnownFirst repository root.");
    }
}
