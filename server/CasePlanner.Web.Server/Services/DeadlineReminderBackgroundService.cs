using System.Data.Common;
using System.Text.RegularExpressions;
using CasePlanner.Data;
using CasePlanner.Web.Server.Persistence;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CasePlanner.Web.Server.Services;

// Multi-user rollout Phase 4b (deadline reminders). This is the project's first hosted/periodic job
// - there is zero existing precedent for a BackgroundService here, so this establishes the pattern
// intentionally simply rather than reaching for anything more elaborate. Registered in Program.cs
// only when the active DB provider is SqlServer AND NotificationsOptions.Enabled (mirrors the
// IDeadlineStore/INotificationStore provider switch already used there); it has nothing useful to do
// on SQLite, since case_assignments (the "all assigned staff" recipient list) has no SQLite
// equivalent, same as the rest of this rollout's SQL-Server-only pieces. Cannot be exercised against
// a live SQL Server in this sandbox - compile/review-only, like the rest of this project's
// SqlServer*-prefixed code; the one piece of its logic that gets real unit tests is
// DeadlineReminderScanner (see DeadlineReminderScannerTests).
public sealed class DeadlineReminderBackgroundService(
    IDatabaseConnectionFactory connections,
    SqlServerCaseAssignmentRepository assignments,
    SqlServerNotificationStore notifications,
    NotificationsOptions options,
    ILogger<DeadlineReminderBackgroundService> logger) : BackgroundService
{
    // A reminder is "N days out" from a date, not a live countdown, so there is no need to scan more
    // than a handful of times a day - every 6 hours means a case that just became due gets its
    // reminder within a few hours, comfortably same-day. GetDueReminders' own dedupe (see
    // DeadlineReminderScanner) makes re-scanning within the same day - or after a restart - harmless,
    // so the exact cadence isn't precision-critical; this is a reasonable, unfussy default.
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);

    private const string TrialDateTitle = "Trial date approaching";
    private const string ServiceDeadlineTitle = "Service deadline approaching";
    private static readonly Regex IsoDatePattern = new(@"\b(\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        do
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Deadline reminder scan failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ScanAsync(CancellationToken token)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);

        var cases = await LoadCaseDeadlinesAsync(connection, token);
        var alreadyNotified = await LoadAlreadyNotifiedAsync(connection, token);
        var due = DeadlineReminderScanner.GetDueReminders(cases, today, options.ReminderLeadDays, alreadyNotified);

        foreach (var reminder in due)
        {
            var recipients = await assignments.GetAllAssignedUserIdsAsync(reminder.CaseId, token);
            if (recipients.Count == 0)
            {
                continue;
            }

            var (title, body) = BuildContent(reminder, options.ReminderLeadDays);
            await notifications.CreateAsync(
                recipients.Select(r => r.ToString()).ToList(),
                "DeadlineReminder",
                reminder.CaseId,
                title,
                body,
                token);
        }
    }

    private static async Task<List<CaseDeadlineSnapshot>> LoadCaseDeadlinesAsync(DbConnection connection, CancellationToken token)
    {
        var result = new List<CaseDeadlineSnapshot>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,case_number,trial_date,service_deadline_120 FROM dbo.cases WHERE is_deleted=0";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new CaseDeadlineSnapshot(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                ParseDate(reader, 2),
                ParseDate(reader, 3)));
        }

        return result;
    }

    // Dedupe state: this rollout's notifications table has no dedicated "which exact deadline date
    // was this reminder for" column (see 034_notifications.sql - deliberately no schema addition for
    // Phase 4a/4b beyond what's needed), so the exact date is recovered from the body text this same
    // service writes via BuildContent below (which always embeds an ISO yyyy-MM-dd date), and the
    // deadline type from the exact, fixed title text used for each ("Trial date approaching" vs.
    // "Service deadline approaching"). This is deliberately simple and self-contained - safe because
    // this service is the only writer of DeadlineReminder notifications - rather than adding a new
    // column purely to support dedupe.
    private static async Task<HashSet<(long CaseId, string DeadlineType, DateOnly DeadlineDate)>> LoadAlreadyNotifiedAsync(DbConnection connection, CancellationToken token)
    {
        var result = new HashSet<(long, string, DateOnly)>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT case_id,title,body FROM dbo.notifications WHERE notification_type=@type AND case_id IS NOT NULL";
        command.Parameters.Add(new SqlParameter("@type", "DeadlineReminder"));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var caseId = reader.GetInt64(0);
            var title = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var body = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var deadlineType = title switch
            {
                TrialDateTitle => DeadlineReminderScanner.TrialDateType,
                ServiceDeadlineTitle => DeadlineReminderScanner.ServiceDeadline120Type,
                _ => null,
            };
            if (deadlineType is null)
            {
                continue;
            }

            var match = IsoDatePattern.Match(body);
            if (match.Success && DateOnly.TryParse(match.Groups[1].Value, out var deadlineDate))
            {
                result.Add((caseId, deadlineType, deadlineDate));
            }
        }

        return result;
    }

    private static (string Title, string Body) BuildContent(DueDeadlineReminder reminder, int leadDays)
    {
        var caseNumber = string.IsNullOrWhiteSpace(reminder.CaseNumber) ? $"case {reminder.CaseId}" : reminder.CaseNumber;
        var isoDate = reminder.DeadlineDate.ToString("yyyy-MM-dd");
        return reminder.DeadlineType switch
        {
            DeadlineReminderScanner.TrialDateType =>
                (TrialDateTitle, $"Trial date for {caseNumber} is {isoDate} ({leadDays} days out)."),
            DeadlineReminderScanner.ServiceDeadline120Type =>
                (ServiceDeadlineTitle, $"120-day service deadline for {caseNumber} is {isoDate} ({leadDays} days out)."),
            _ => throw new ArgumentOutOfRangeException(nameof(reminder), reminder.DeadlineType, "Unknown deadline type."),
        };
    }

    private static DateOnly? ParseDate(DbDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            return null;
        }

        var value = Convert.ToString(reader.GetValue(index));
        return DateOnly.TryParse(value, out var date) ? date : null;
    }
}
