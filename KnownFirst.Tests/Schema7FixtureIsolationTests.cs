using KnownFirst.Services.DataSafety.Merge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Schema7FixtureIsolationTests
{
    [TestMethod]
    public async Task Schema7Fixture_CreateAsync_UsesDistinctSafetyCopyRoots()
    {
        await using var fixtureA = await Schema7Fixture.CreateAsync();
        await using var fixtureB = await Schema7Fixture.CreateAsync();

        var databaseDirA = Path.GetFullPath(Path.GetDirectoryName(fixtureA.DatabasePath)!);
        var databaseDirB = Path.GetFullPath(Path.GetDirectoryName(fixtureB.DatabasePath)!);

        var safetyCopyRootA = Path.GetFullPath(Path.Combine(databaseDirA, MergeSafetyCopyService.DirectoryName));
        var safetyCopyRootB = Path.GetFullPath(Path.Combine(databaseDirB, MergeSafetyCopyService.DirectoryName));

        Assert.AreNotEqual(
            databaseDirA,
            databaseDirB,
            StringComparer.OrdinalIgnoreCase,
            "Each Schema7Fixture must have a distinct parent directory.");

        Assert.AreNotEqual(
            safetyCopyRootA,
            safetyCopyRootB,
            StringComparer.OrdinalIgnoreCase,
            "Each Schema7Fixture must have a distinct merge-safety-copies root.");

        Assert.IsTrue(
            databaseDirA.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase),
            "Database directory A must reside under temp root.");

        Assert.IsTrue(
            databaseDirB.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase),
            "Database directory B must reside under temp root.");

        Assert.AreNotEqual(
            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            databaseDirA.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparer.OrdinalIgnoreCase,
            "Database directory A must not be the shared temp root itself.");

        Assert.AreNotEqual(
            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            databaseDirB.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparer.OrdinalIgnoreCase,
            "Database directory B must not be the shared temp root itself.");
    }

    [TestMethod]
    public async Task Schema7Fixture_DisposeAsync_RemovesOnlyOwnedFixtureRoot()
    {
        var fixtureA = await Schema7Fixture.CreateAsync();
        var fixtureB = await Schema7Fixture.CreateAsync();

        var rootDirA = Path.GetFullPath(Path.GetDirectoryName(fixtureA.DatabasePath)!);
        var rootDirB = Path.GetFullPath(Path.GetDirectoryName(fixtureB.DatabasePath)!);

        Assert.IsTrue(Directory.Exists(rootDirA), "Root directory A must exist initially.");
        Assert.IsTrue(Directory.Exists(rootDirB), "Root directory B must exist initially.");

        // Simulate nested fixture artifacts (such as merge-safety-copies)
        var nestedDirA = Path.Combine(rootDirA, MergeSafetyCopyService.DirectoryName);
        Directory.CreateDirectory(nestedDirA);
        await File.WriteAllTextAsync(Path.Combine(nestedDirA, "dummy.txt"), "fixture A content");

        var nestedDirB = Path.Combine(rootDirB, MergeSafetyCopyService.DirectoryName);
        Directory.CreateDirectory(nestedDirB);
        await File.WriteAllTextAsync(Path.Combine(nestedDirB, "dummy.txt"), "fixture B content");

        await fixtureA.DisposeAsync();

        Assert.IsFalse(Directory.Exists(rootDirA), "Disposing fixture A must delete its owned root directory.");
        Assert.IsTrue(Directory.Exists(rootDirB), "Disposing fixture A must not delete fixture B's root directory.");
        Assert.IsTrue(File.Exists(fixtureB.DatabasePath), "Fixture B's database must remain intact.");
        Assert.IsTrue(File.Exists(Path.Combine(nestedDirB, "dummy.txt")), "Fixture B's nested files must remain intact.");

        await fixtureB.DisposeAsync();

        Assert.IsFalse(Directory.Exists(rootDirB), "Disposing fixture B must delete its owned root directory.");
    }
}
