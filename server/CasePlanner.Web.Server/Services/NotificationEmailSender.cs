using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace CasePlanner.Web.Server.Services;

// Multi-user rollout Phase 4b (email delivery). Built against the built-in System.Net.Mail
// SmtpClient/MailMessage - no new NuGet dependency, matching this project's dependency-averse
// convention (see WitnessNameMatcher.cs for the same discipline applied elsewhere). Deliberately a
// static helper rather than an injected interface/service: it's stateless config-in/email-out, and
// like the rest of this rollout's SMTP-dependent code it can only really be verified against a real
// relay, which doesn't exist in this sandbox - a DI seam here would be speculative, not testable.
public static class NotificationEmailSender
{
    /// <summary>Sends one email, swallowing and logging any failure. Callers (SqlServerNotificationStore)
    /// run this only after the in-app notification row is already committed, so an SMTP relay outage
    /// or misconfiguration must never throw back into - or roll back - the caller.</summary>
    public static async Task SendBestEffortAsync(
        NotificationsOptions.EmailOptions email,
        string toAddress,
        string toDisplayName,
        string subject,
        string? body,
        ILogger logger,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress) || string.IsNullOrWhiteSpace(email.Host) || string.IsNullOrWhiteSpace(email.FromAddress))
        {
            return;
        }

        try
        {
            using var client = new SmtpClient(email.Host, email.Port) { EnableSsl = email.EnableSsl };
            if (!string.IsNullOrWhiteSpace(email.Username))
            {
                client.Credentials = new NetworkCredential(email.Username, email.Password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(email.FromAddress, string.IsNullOrWhiteSpace(email.FromName) ? email.FromAddress : email.FromName),
                Subject = subject,
                Body = body ?? "",
            };
            message.To.Add(new MailAddress(toAddress, toDisplayName));

            await client.SendMailAsync(message, token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send notification email to {ToAddress}.", toAddress);
        }
    }
}
