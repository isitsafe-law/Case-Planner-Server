namespace CasePlanner.Web.Server.Services;

// Multi-user rollout Phase 4b (deadline reminders + email delivery). Mirrors EntraOptions
// (Security/EntraOptions.cs) exactly: a plain config-bound options class, disabled by default. The
// SMTP relay is a real external IT dependency - credentials that don't exist in this sandbox, same
// as Entra's client secret - so Email.Username/Password/Host stay blank here. In a real deployment,
// supply them via environment variables, user-secrets, or IT-provided config; never commit real
// SMTP credentials to appsettings.json.
public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; }

    // How many days before a case's TrialDate/ServiceDeadline120 the reminder notification fires.
    public int ReminderLeadDays { get; set; } = 7;

    public EmailOptions Email { get; set; } = new();

    public sealed class EmailOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "";
    }
}
