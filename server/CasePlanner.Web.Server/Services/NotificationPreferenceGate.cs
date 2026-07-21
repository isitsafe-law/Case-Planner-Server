using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

// Multi-user rollout Phase 4c (per-user notification preferences). The one piece of "which of the
// six booleans applies to this notification type" logic, shared by both providers' insert paths
// (CasePlannerRepository.InsertNotificationsAsync on SQLite, SqlServerNotificationStore.CreateAsync
// on SQL Server) so the TaskAssigned/TaskCompleted/DeadlineReminder -> column mapping exists in
// exactly one place rather than two parallel switch statements. Unknown notification types fail
// open (both methods return true) - this gate should never silently block a notification type it
// doesn't recognize.
public static class NotificationPreferenceGate
{
    public static bool IsInAppEnabled(NotificationPreferencesRecord preferences, string notificationType) => notificationType switch
    {
        "TaskAssigned" => preferences.TaskAssignedInApp,
        "TaskCompleted" => preferences.TaskCompletedInApp,
        "DeadlineReminder" => preferences.DeadlineReminderInApp,
        _ => true,
    };

    public static bool IsEmailEnabled(NotificationPreferencesRecord preferences, string notificationType) => notificationType switch
    {
        "TaskAssigned" => preferences.TaskAssignedEmail,
        "TaskCompleted" => preferences.TaskCompletedEmail,
        "DeadlineReminder" => preferences.DeadlineReminderEmail,
        _ => true,
    };
}
