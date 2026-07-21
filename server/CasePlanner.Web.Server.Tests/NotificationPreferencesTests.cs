using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 4c (per-user notification preferences). Fully testable on SQLite - unlike
// Part A's admin-union pieces, notification_preferences has no case_assignments dependency at all,
// it's a pure per-user-id row. Two things get real coverage here: the dual-provider round-trip
// (get-defaults / upsert / re-upsert) and the gating mechanism actually being wired into notification
// creation, using the one trigger that's fully SQLite-testable (TaskAssigned).
public class NotificationPreferencesTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Case",
            CaseNumber = "24-CV-200",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
        });

    // ---- round trip ----

    [Fact]
    public async Task GetNotificationPreferences_NoSavedRow_ReturnsAllDefaultsTrue()
    {
        var userId = Guid.NewGuid().ToString();

        var preferences = await _fixture.Repository.GetNotificationPreferencesAsync(userId);

        Assert.Equal(userId, preferences.UserId);
        Assert.True(preferences.TaskAssignedInApp);
        Assert.True(preferences.TaskAssignedEmail);
        Assert.True(preferences.TaskCompletedInApp);
        Assert.True(preferences.TaskCompletedEmail);
        Assert.True(preferences.DeadlineReminderInApp);
        Assert.True(preferences.DeadlineReminderEmail);
    }

    [Fact]
    public async Task UpsertNotificationPreferences_ThenGet_RoundTripsAllSixFields()
    {
        var userId = Guid.NewGuid().ToString();

        await _fixture.Repository.UpsertNotificationPreferencesAsync(new NotificationPreferencesRecord
        {
            UserId = userId,
            TaskAssignedInApp = false,
            TaskAssignedEmail = true,
            TaskCompletedInApp = true,
            TaskCompletedEmail = false,
            DeadlineReminderInApp = false,
            DeadlineReminderEmail = false,
        });

        var reloaded = await _fixture.Repository.GetNotificationPreferencesAsync(userId);

        Assert.False(reloaded.TaskAssignedInApp);
        Assert.True(reloaded.TaskAssignedEmail);
        Assert.True(reloaded.TaskCompletedInApp);
        Assert.False(reloaded.TaskCompletedEmail);
        Assert.False(reloaded.DeadlineReminderInApp);
        Assert.False(reloaded.DeadlineReminderEmail);
    }

    [Fact]
    public async Task UpsertNotificationPreferences_SavedTwice_ReplacesPriorValuesRatherThanMerging()
    {
        var userId = Guid.NewGuid().ToString();

        await _fixture.Repository.UpsertNotificationPreferencesAsync(new NotificationPreferencesRecord
        {
            UserId = userId,
            TaskAssignedInApp = false,
            TaskAssignedEmail = false,
            TaskCompletedInApp = false,
            TaskCompletedEmail = false,
            DeadlineReminderInApp = false,
            DeadlineReminderEmail = false,
        });

        // Second save flips everything back on - a naive "merge" implementation (e.g. only updating
        // columns that changed, or OR-ing booleans together) would fail this.
        await _fixture.Repository.UpsertNotificationPreferencesAsync(new NotificationPreferencesRecord
        {
            UserId = userId,
            TaskAssignedInApp = true,
            TaskAssignedEmail = true,
            TaskCompletedInApp = true,
            TaskCompletedEmail = true,
            DeadlineReminderInApp = true,
            DeadlineReminderEmail = true,
        });

        var reloaded = await _fixture.Repository.GetNotificationPreferencesAsync(userId);
        Assert.True(reloaded.TaskAssignedInApp);
        Assert.True(reloaded.TaskAssignedEmail);
        Assert.True(reloaded.TaskCompletedInApp);
        Assert.True(reloaded.TaskCompletedEmail);
        Assert.True(reloaded.DeadlineReminderInApp);
        Assert.True(reloaded.DeadlineReminderEmail);
    }

    // ---- gating: proves the mechanism is actually wired in, not just present as dead code ----

    [Fact]
    public async Task SaveChecklistItem_AssigneeHasTaskAssignedInAppDisabled_CreatesNoNotification()
    {
        var c = await CreateCaseAsync();
        var userId = Guid.NewGuid().ToString();

        await _fixture.Repository.UpsertNotificationPreferencesAsync(new NotificationPreferencesRecord
        {
            UserId = userId,
            TaskAssignedInApp = false,
        });

        await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Serve interrogatories",
            Status = "Not Started",
            AssignedUserId = userId,
        });

        // Contrast with NotificationsTests.SaveChecklistItem_NewAssignment_CreatesTaskAssignedNotification,
        // where the same save (with default preferences) creates exactly one notification - this
        // proves the gating check in InsertNotificationsAsync is actually consulted, not dead code.
        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(userId);
        Assert.Empty(feed.Items);
        Assert.Equal(0, feed.UnreadCount);
    }

    [Fact]
    public async Task SaveChecklistItem_AssigneeHasDefaultPreferences_StillCreatesTaskAssignedNotification()
    {
        var c = await CreateCaseAsync();
        var userId = Guid.NewGuid().ToString();

        await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Serve interrogatories",
            Status = "Not Started",
            AssignedUserId = userId,
        });

        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(userId);
        Assert.Single(feed.Items);
    }

    [Fact]
    public async Task CreateNotificationsAsync_RecipientHasThatTypeDisabled_SkipsOnlyThatRecipient()
    {
        var c = await CreateCaseAsync();
        var blockedRecipient = Guid.NewGuid().ToString();
        var normalRecipient = Guid.NewGuid().ToString();
        await _fixture.Repository.UpsertNotificationPreferencesAsync(new NotificationPreferencesRecord
        {
            UserId = blockedRecipient,
            TaskCompletedInApp = false,
        });

        await _fixture.Repository.CreateNotificationsAsync(
            [blockedRecipient, normalRecipient], "TaskCompleted", c.Id, "Task completed", "body");

        var blockedFeed = await _fixture.Repository.GetNotificationsForRecipientAsync(blockedRecipient);
        var normalFeed = await _fixture.Repository.GetNotificationsForRecipientAsync(normalRecipient);
        Assert.Empty(blockedFeed.Items);
        Assert.Single(normalFeed.Items);
    }
}
