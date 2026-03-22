using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using NcsScheduler.Models.Domain;

namespace NcsScheduler.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendInviteAsync(string toEmail, string toName, string inviteUrl)
    {
        var subject = "You've been invited to NCS Scheduler";
        var body = $"""
            <p>Hi {toName},</p>
            <p>You've been invited to join NCS Scheduler to manage your net controller schedule.</p>
            <p><a href="{inviteUrl}">Click here to set up your account</a></p>
            <p>This invitation expires in 7 days.</p>
            """;
        await SendAsync(toEmail, toName, subject, body);
    }

    public async Task SendSlotOpenedAsync(NetController coordinator, NetSession session, NetController unavailableController)
    {
        if (coordinator.Email is null) return;
        var subject = $"Open slot: {session.Net?.Name} on {session.SessionDate}";
        var body = $"""
            <p>{unavailableController.Callsign} is unavailable for the {session.Net?.Name} on {session.SessionDate:dddd, MMMM d, yyyy}.</p>
            <p>Please log in to NCS Scheduler to assign a substitute.</p>
            """;
        await SendAsync(coordinator.Email, coordinator.Name, subject, body);
    }

    public async Task SendVolunteerNotificationAsync(NetController coordinator, NetSession session, NetController volunteer)
    {
        if (coordinator.Email is null) return;
        var subject = $"Volunteer: {session.Net?.Name} on {session.SessionDate}";
        var body = $"""
            <p>{volunteer.Callsign} has volunteered to run the {session.Net?.Name} on {session.SessionDate:dddd, MMMM d, yyyy}.</p>
            <p>Please log in to NCS Scheduler to confirm or assign a different substitute.</p>
            """;
        await SendAsync(coordinator.Email, coordinator.Name, subject, body);
    }

    public async Task SendAssignmentConfirmationAsync(NetController controller, NetSession session)
    {
        if (controller.Email is null) return;
        var subject = $"Assignment confirmed: {session.Net?.Name} on {session.SessionDate}";
        var body = $"""
            <p>Hi {controller.Name},</p>
            <p>You have been confirmed as net controller for the {session.Net?.Name} on {session.SessionDate:dddd, MMMM d, yyyy} at {session.ScheduledTimeUtc:HH:mm}z.</p>
            """;
        await SendAsync(controller.Email, controller.Name, subject, body);
    }

    public async Task SendPasswordResetAsync(string toEmail, string toName, string resetUrl)
    {
        var subject = "Reset your NCS Scheduler password";
        var body = $"""
            <p>Hi {toName},</p>
            <p>A password reset was requested for your NCS Scheduler account.</p>
            <p><a href="{resetUrl}">Click here to reset your password</a></p>
            <p>This link expires in 24 hours. If you did not request a reset, you can ignore this email.</p>
            """;
        await SendAsync(toEmail, toName, subject, body);
    }

    public async Task SendBackupRequestAsync(IEnumerable<NetController> recipients, NetSession session, NetController requestingNcs)
    {
        var subject = $"Backup requested: {session.Net?.Name} on {session.SessionDate:MMMM d, yyyy}";
        var body = $"""
            <p>{requestingNcs.Callsign} may need a backup for the {session.Net?.Name} on {session.SessionDate:dddd, MMMM d, yyyy} at {session.ScheduledTimeUtc:HH:mm}z.</p>
            <p>If you are available to stand by as a backup NCS, please log in to NCS Scheduler and click <strong>Stand By as Backup</strong> on your dashboard. You and {requestingNcs.Callsign} will coordinate directly.</p>
            """;
        foreach (var recipient in recipients)
        {
            if (recipient.Email is not null)
                await SendAsync(recipient.Email, recipient.Name, subject, body);
        }
    }

    private async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost) || string.IsNullOrWhiteSpace(_settings.FromAddress))
        {
            _logger.LogWarning("Email not configured — skipping send to {Email}: {Subject}", toEmail, subject);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            var secureOption = _settings.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureOption);

            if (!string.IsNullOrWhiteSpace(_settings.Username))
                await client.AuthenticateAsync(_settings.Username, _settings.Password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}: {Subject}", toEmail, subject);
        }
    }
}
