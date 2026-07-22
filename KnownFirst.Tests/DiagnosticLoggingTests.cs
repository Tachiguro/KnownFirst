using KnownFirst.Services.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace KnownFirst.Tests;

[TestClass]
public sealed class DiagnosticLoggingTests
{
    private string _temporaryDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "KnownFirst.Tests",
            "diagnostics",
            Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
        catch
        {
            // A failed test cleanup must not hide the test result.
        }
    }

    [TestMethod]
    public void Write_CreatesStructuredLogWithMetadataAndExceptionChain()
    {
        using var provider = new RollingFileLoggerProvider(CreateOptions());
        var logger = provider.CreateLogger("KnownFirst.Tests.ControlledFailure");
        var exception = CaptureControlledException();

        logger.LogError(
            new EventId(42, "ControlledFailure"),
            exception,
            "A controlled diagnostic failure occurred. OperationId = {OperationId}",
            17);
        provider.Flush();

        var path = AssertSingleLogFile();
        using var document = JsonDocument.Parse(ReadAllTextShared(path).Trim());
        var root = document.RootElement;
        Assert.AreEqual("Error", root.GetProperty("level").GetString());
        Assert.AreEqual("KnownFirst.Tests.ControlledFailure", root.GetProperty("category").GetString());
        Assert.AreEqual(42, root.GetProperty("eventId").GetProperty("id").GetInt32());
        Assert.AreEqual("ControlledFailure", root.GetProperty("eventId").GetProperty("name").GetString());
        Assert.AreEqual("0.1.0-test", root.GetProperty("applicationVersion").GetString());
        Assert.AreEqual("Debug", root.GetProperty("buildConfiguration").GetString());
        Assert.AreEqual("net10.0", root.GetProperty("targetFramework").GetString());
        Assert.AreEqual("test-session", root.GetProperty("sessionId").GetString());
        Assert.AreEqual("17", root.GetProperty("properties").GetProperty("OperationId").GetString());
        Assert.AreEqual(
            typeof(InvalidOperationException).FullName,
            root.GetProperty("exception").GetProperty("type").GetString());
        Assert.AreEqual(
            typeof(ArgumentException).FullName,
            root.GetProperty("exception").GetProperty("innerException").GetProperty("type").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            root.GetProperty("exception").GetProperty("stackTrace").GetString()));
        Assert.IsTrue(DateTimeOffset.TryParse(root.GetProperty("timestamp").GetString(), out _));
    }

    [TestMethod]
    public void Write_RedactsSensitiveStructuredValuesAndHeaders()
    {
        const string privateDocument = "Complete private imported document that must stay local.";
        const string password = "diagnostic-password-value";
        const string bearerToken = "diagnostic-bearer-token";
        using var provider = new RollingFileLoggerProvider(CreateOptions());
        var logger = provider.CreateLogger("KnownFirst.Tests.Redaction");

        logger.LogInformation(
            "Import metadata {ImportedText}; password={Password}; authorization=Bearer " + bearerToken,
            privateDocument,
            password);
        provider.Flush();

        var content = ReadAllTextShared(AssertSingleLogFile());
        Assert.DoesNotContain(privateDocument, content);
        Assert.DoesNotContain(password, content);
        Assert.DoesNotContain(bearerToken, content);
        Assert.Contains("[REDACTED]", content);
    }

    [TestMethod]
    public void Write_RollsFilesAndEnforcesRetainedFileLimit()
    {
        var options = CreateOptions(maximumFileBytes: 512, retainedFileCount: 3);
        using (var provider = new RollingFileLoggerProvider(options))
        {
            var logger = provider.CreateLogger("KnownFirst.Tests.Rolling");
            for (var index = 0; index < 100; index++)
            {
                logger.LogInformation(
                    "Rolling diagnostic entry. Index = {Index}, padding = {Padding}",
                    index,
                    new string('x', 180));
            }

            provider.Flush();
        }

        var files = Directory.GetFiles(_temporaryDirectory, "knownfirst-*.jsonl");
        Assert.IsGreaterThanOrEqualTo(2, files.Length, "The test should create more than one rolled file.");
        Assert.IsLessThanOrEqualTo(3, files.Length, "The retained file limit must be enforced.");
    }

    [TestMethod]
    public void Construction_RemovesFilesOlderThanRetentionAge()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var expiredPath = Path.Combine(
            _temporaryDirectory,
            "knownfirst-20000101-expired-000.jsonl");
        File.WriteAllText(expiredPath, "expired");
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-30));
        var options = CreateOptions(maximumAge: TimeSpan.FromDays(14));

        using var provider = new RollingFileLoggerProvider(options);
        provider.Flush();

        Assert.IsFalse(File.Exists(expiredPath));
        Assert.IsGreaterThanOrEqualTo(
            1,
            Directory.GetFiles(_temporaryDirectory, "knownfirst-*.jsonl").Length);
    }

    [TestMethod]
    public void LoggingFailure_DoesNotCrashApplicationCode()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_temporaryDirectory)!);
        File.WriteAllText(_temporaryDirectory, "This file intentionally blocks directory creation.");
        var options = CreateOptions();

        using var provider = new RollingFileLoggerProvider(options);
        var logger = provider.CreateLogger("KnownFirst.Tests.UnavailablePath");
        logger.LogError(CaptureControlledException(), "This write is expected to fail internally.");
        provider.Flush();

        var diagnostics = new AppDiagnosticsService(provider);
        var summary = diagnostics.GetSummary();
        Assert.AreEqual(_temporaryDirectory, summary.LogDirectory);
        Assert.AreEqual(0, summary.RetainedFileCount);
    }

    [TestMethod]
    public void DiagnosticsService_ExposesDirectorySessionSummaryAndSafeFlush()
    {
        using var provider = new RollingFileLoggerProvider(CreateOptions());
        var logger = provider.CreateLogger("KnownFirst.Tests.Summary");
        logger.LogInformation("Summary test event.");
        var diagnostics = new AppDiagnosticsService(provider);

        diagnostics.Flush();
        var summary = diagnostics.GetSummary();

        Assert.AreEqual(_temporaryDirectory, diagnostics.LogDirectory);
        Assert.AreEqual("test-session", diagnostics.SessionId);
        Assert.AreEqual("test-session", summary.SessionId);
        Assert.AreEqual(1, summary.RetainedFileCount);
        Assert.IsGreaterThan(0L, summary.RetainedBytes);
        Assert.IsFalse(string.IsNullOrWhiteSpace(summary.CurrentLogFile));
    }

    [TestMethod]
    public void Startup_RegistersPersistentLoggingAndFatalFailureHooks()
    {
        var mauiProgram = LoadUiArtifact("MauiProgram.cs");
        var app = LoadUiArtifact("App.xaml.cs");
        var mainPage = LoadUiArtifact("MainPage.xaml.cs");
        var windowsApp = LoadUiArtifact("WindowsApp.xaml.cs");
        var localizationHost = LoadUiArtifact("LocalizationHost.razor");
        var errorBoundary = LoadUiArtifact("LoggingErrorBoundary.razor");

        Assert.Contains("new RollingFileLoggerProvider", mauiProgram);
        Assert.Contains("AddSingleton<IAppDiagnosticsService, AppDiagnosticsService>", mauiProgram);
        Assert.Contains("AddSingleton<RuntimeExceptionMonitor>", mauiProgram);
        Assert.Contains("builder.Logging.AddProvider(fileLoggerProvider)", mauiProgram);
        Assert.Contains("GetRequiredService<RuntimeExceptionMonitor>().Start()", mauiProgram);
        Assert.Contains("DiagnosticEventIds.Shutdown", app);
        Assert.Contains("DiagnosticEventIds.StartupCompleted", mainPage);
        Assert.Contains("DiagnosticEventIds.WinUiUnhandledException", windowsApp);
        Assert.Contains("<LoggingErrorBoundary>", localizationHost);
        Assert.Contains("DiagnosticEventIds.BlazorUnhandledException", errorBoundary);
    }

    private DiagnosticLogOptions CreateOptions(
        long maximumFileBytes = DiagnosticLogOptions.DefaultMaximumFileBytes,
        int retainedFileCount = DiagnosticLogOptions.DefaultRetainedFileCount,
        TimeSpan? maximumAge = null) => new()
    {
        DirectoryPath = _temporaryDirectory,
        ApplicationVersion = "0.1.0-test",
        BuildConfiguration = "Debug",
        TargetFramework = "net10.0",
        Platform = "Test",
        OperatingSystemVersion = "Test OS",
        MinimumLevel = LogLevel.Trace,
        MaximumFileBytes = maximumFileBytes,
        RetainedFileCount = retainedFileCount,
        MaximumAge = maximumAge ?? TimeSpan.FromDays(21),
        SessionId = "test-session"
    };

    private string AssertSingleLogFile()
    {
        var files = Directory.GetFiles(_temporaryDirectory, "knownfirst-*.jsonl");
        Assert.HasCount(1, files);
        return files[0];
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadUiArtifact(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", fileName));

    private static Exception CaptureControlledException()
    {
        try
        {
            throw new InvalidOperationException(
                "Controlled outer failure.",
                new ArgumentException("Controlled inner failure."));
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
