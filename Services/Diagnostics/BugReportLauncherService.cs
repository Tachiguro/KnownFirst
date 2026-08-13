using System.Text;
using KnownFirst.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;

namespace KnownFirst.Services.Diagnostics;

public sealed class BugReportLauncherService : IBugReportLauncherService
{
    public const string RecipientAddress = "Tachiguro+KnownFirst_BugReport@gmail.com";

    private readonly IBuildIdentityService _buildIdentityService;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<BugReportLauncherService> _logger;

    public BugReportLauncherService(
        IBuildIdentityService buildIdentityService,
        IStringLocalizer<SharedResource> localizer,
        ILogger<BugReportLauncherService> logger)
    {
        _buildIdentityService = buildIdentityService;
        _localizer = localizer;
        _logger = logger;
    }

    public string RecipientEmail => RecipientAddress;

    public async Task<bool> LaunchBugReportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var identity = _buildIdentityService.Identity;
            var subject = _localizer["BugReport_Subject", identity.Version].Value;
            var body = BuildBugReportBody(identity);

            if (Email.Default.IsComposeSupported)
            {
                var message = new EmailMessage
                {
                    Subject = subject,
                    Body = body,
                    BodyFormat = EmailBodyFormat.PlainText,
                    Recipients = [RecipientAddress]
                };

                await Email.Default.ComposeAsync(message);
                return true;
            }

            var escapedSubject = Uri.EscapeDataString(subject);
            var escapedBody = Uri.EscapeDataString(body);
            var mailtoUri = new Uri($"mailto:{RecipientAddress}?subject={escapedSubject}&body={escapedBody}");

            return await Launcher.Default.OpenAsync(mailtoUri);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The bug report email composer could not be launched.");
            return false;
        }
    }

    private string BuildBugReportBody(BuildIdentity identity)
    {
        var builder = new StringBuilder();
        builder.AppendLine(_localizer["BugReport_PromptWhatHappened"].Value);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine(_localizer["BugReport_PromptWhatExpected"].Value);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine(_localizer["BugReport_PromptReproductionSteps"].Value);
        builder.AppendLine("1. ");
        builder.AppendLine("2. ");
        builder.AppendLine("3. ");
        builder.AppendLine();
        builder.AppendLine(_localizer["BugReport_PromptOptionalScreenshots"].Value);
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine($"KnownFirst: {identity.Product}");
        builder.AppendLine($"Version: {identity.Version}");
        builder.AppendLine($"Build: {identity.BuildNumber}");
        builder.AppendLine($"Configuration: {identity.Configuration}");
        builder.AppendLine($"OS: {identity.OS}");
        builder.Append($"Device: {identity.Device}");
        return builder.ToString();
    }
}
