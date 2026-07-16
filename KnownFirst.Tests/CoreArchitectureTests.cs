using KnownFirst.Core.Language;

namespace KnownFirst.Tests;

[TestClass]
public sealed class CoreArchitectureTests
{
    [TestMethod]
    public void CoreAssembly_DoesNotReferencePlatformFrameworks()
    {
        var forbiddenReferencePrefixes = new[]
        {
            "Microsoft.Maui",
            "Microsoft.AspNetCore.Components.WebView",
            "Microsoft.WindowsAppSDK",
            "Mono.Android"
        };
        var forbiddenReferences = typeof(LanguagePreferencePolicy).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(reference => forbiddenReferencePrefixes.Any(
                prefix => reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.IsEmpty(
            forbiddenReferences,
            $"Forbidden Core references: {string.Join(", ", forbiddenReferences)}");
    }
}
