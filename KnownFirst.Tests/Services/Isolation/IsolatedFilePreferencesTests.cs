using KnownFirst.Services.Isolation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnownFirst.Tests.Services.Isolation;

[TestClass]
public class IsolatedFilePreferencesTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "kf-isolated-prefs-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void Get_ReturnsDefault_WhenKeyIsMissing()
    {
        var preferences = new IsolatedFilePreferences(_root);

        Assert.AreEqual("fallback", preferences.Get("missing-key", "fallback"));
        Assert.AreEqual(42, preferences.Get("missing-key", 42));
        Assert.IsFalse(preferences.Get("missing-key", false));
    }

    [TestMethod]
    public void SetThenGet_RoundTripsStringBoolAndInt()
    {
        var preferences = new IsolatedFilePreferences(_root);

        preferences.Set("string-key", "value");
        preferences.Set("bool-key", true);
        preferences.Set("int-key", 7);

        Assert.AreEqual("value", preferences.Get("string-key", string.Empty));
        Assert.IsTrue(preferences.Get("bool-key", false));
        Assert.AreEqual(7, preferences.Get("int-key", 0));
    }

    [TestMethod]
    public void ContainsKey_ReflectsCurrentState()
    {
        var preferences = new IsolatedFilePreferences(_root);

        Assert.IsFalse(preferences.ContainsKey("k"));
        preferences.Set("k", "v");
        Assert.IsTrue(preferences.ContainsKey("k"));
    }

    [TestMethod]
    public void Remove_DeletesOnlyTheGivenKey()
    {
        var preferences = new IsolatedFilePreferences(_root);
        preferences.Set("keep", "a");
        preferences.Set("drop", "b");

        preferences.Remove("drop");

        Assert.IsTrue(preferences.ContainsKey("keep"));
        Assert.IsFalse(preferences.ContainsKey("drop"));
    }

    [TestMethod]
    public void Clear_RemovesAllKeys()
    {
        var preferences = new IsolatedFilePreferences(_root);
        preferences.Set("a", "1");
        preferences.Set("b", "2");

        preferences.Clear();

        Assert.IsFalse(preferences.ContainsKey("a"));
        Assert.IsFalse(preferences.ContainsKey("b"));
    }

    [TestMethod]
    public void Values_PersistAcrossInstances_ForTheSameRootDirectory()
    {
        var first = new IsolatedFilePreferences(_root);
        first.Set("theme_preference", 2);

        var second = new IsolatedFilePreferences(_root);

        Assert.AreEqual(2, second.Get("theme_preference", 0));
    }

    [TestMethod]
    public void Get_ReturnsDefault_WhenStoredFileIsCorrupt()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "preferences.json"), "{ not valid json");

        var preferences = new IsolatedFilePreferences(_root);

        Assert.AreEqual("fallback", preferences.Get("any-key", "fallback"));
    }

    [TestMethod]
    public void DifferentRoots_DoNotShareValues()
    {
        var otherRoot = Path.Combine(Path.GetTempPath(), "kf-isolated-prefs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new IsolatedFilePreferences(_root);
            first.Set("shared-key", "root-a");

            var second = new IsolatedFilePreferences(otherRoot);

            Assert.AreEqual("default", second.Get("shared-key", "default"));
        }
        finally
        {
            if (Directory.Exists(otherRoot))
            {
                Directory.Delete(otherRoot, recursive: true);
            }
        }
    }
}
