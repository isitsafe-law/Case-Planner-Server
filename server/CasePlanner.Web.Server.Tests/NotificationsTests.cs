using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 4a (notifications core). Two triggers: task assigned (fully testable
// here - only needs checklist_items.assigned_user_id, present on both providers) and task completed
// -> notify the case's assigned attorneys (depends on case_assignments, which is SQL-Server-only;
// see SqlServerChecklistStore.SaveAsync and SqlServerCaseAssignmentRepository.GetCaseRoleUserIdsAsync
// for the SQL-Server half - review/compile-only here, no live SQL Server in this sandbox). The
// shared insert (CreateNotificationsAsync) is covered directly, bypassing case_assignments entirely,
// so that logic gets real SQLite coverage regardless of which trigger is calling it.
public class NotificationsTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Case",
            CaseNumber = "24-CV-100",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
        });

    // ---- trigger 1: task assigned ----

    [Fact]
    public async Task SaveChecklistItem_NewAssignment_CreatesTaskAssignedNotification()
    {
        var c = await CreateCaseAsync();
        var userId = Guid.NewGuid().ToString();

        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Serve interrogatories",
            Status = "Not Started",
            AssignedUserId = userId,
        });

        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(userId);
        var notification = Assert.Single(feed.Items);
        Assert.Equal("TaskAssigned", notification.NotificationType);
        Assert.Equal(c.Id, notification.CaseId);
        Assert.Contains("Serve interrogatories", notification.Body);
        Assert.Contains("24-CV-100", notification.Body);
        Assert.False(notification.IsRead);
        Assert.Equal(1, feed.UnreadCount);
        Assert.NotEqual(0, saved.Id);
    }

    [Fact]
    public async Task SaveChecklistItem_ReassigningToADifferentUser_NotifiesOnlyTheNewAssignee()
    {
        var c = await CreateCaseAsync();
        var firstUser = Guid.NewGuid().ToString();
        var secondUser = Guid.NewGuid().ToString();

        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = firstUser,
        });

        await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            Id = saved.Id,
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = secondUser,
        });

        var firstUserFeed = await _fixture.Repository.GetNotificationsForRecipientAsync(firstUser);
        var secondUserFeed = await _fixture.Repository.GetNotificationsForRecipientAsync(secondUser);
        Assert.Single(firstUserFeed.Items);
        Assert.Single(secondUserFeed.Items);
    }

    [Fact]
    public async Task SaveChecklistItem_NoOpResaveOfSameAssignee_DoesNotCreateASecondNotification()
    {
        var c = await CreateCaseAsync();
        var userId = Guid.NewGuid().ToString();

        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = userId,
            Notes = "first save",
        });

        await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            Id = saved.Id,
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = userId,
            Notes = "second save, same assignee",
        });

        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(userId);
        Assert.Single(feed.Items);
    }

    [Fact]
    public async Task SaveChecklistItem_ClearingAssignment_CreatesNoNotification()
    {
        var c = await CreateCaseAsync();
        var userId = Guid.NewGuid().ToString();
        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = userId,
        });

        await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            Id = saved.Id,
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = null,
        });

        // Only the original assignment notification exists - clearing the assignee is not itself
        // a notify-worthy event.
        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(userId);
        Assert.Single(feed.Items);
    }

    // ---- trigger 2: task completed -> notify assigned attorneys (SQL-Server-only recipient
    // resolution; SQLite has no case_assignments, so this must be a documented no-op, not an error) ----

    [Fact]
    public async Task SaveChecklistItem_MarkingComplete_CreatesNoNotificationsOnSqlite()
    {
        var c = await CreateCaseAsync();
        var assigneeId = Guid.NewGuid().ToString();
        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Serve interrogatories",
            Status = "Not Started",
            AssignedUserId = assigneeId,
        });

        await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            Id = saved.Id,
            CaseId = c.Id,
            Phase = "General",
            Task = "Serve interrogatories",
            Status = "Done",
            AssignedUserId = assigneeId,
        });

        // SQLite has no case_assignments/case_role table to resolve "this case's attorneys" against
        // (that's SQL-Server-only - see SqlServerCaseAssignmentRepository.GetCaseRoleUserIdsAsync),
        // so the task-completed trigger correctly resolves zero recipients here. The only
        // notification that exists is the earlier TaskAssigned one.
        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(assigneeId);
        var only = Assert.Single(feed.Items);
        Assert.Equal("TaskAssigned", only.NotificationType);
    }

    // ---- shared insert method, exercised directly (bypassing any case_assignments resolution) ----

    [Fact]
    public async Task CreateNotificationsAsync_GivenExplicitRecipientIds_CreatesOneNotificationPerRecipient()
    {
        var c = await CreateCaseAsync();
        var recipientA = Guid.NewGuid().ToString();
        var recipientB = Guid.NewGuid().ToString();

        await _fixture.Repository.CreateNotificationsAsync(
            [recipientA, recipientB], "TaskCompleted", c.Id, "Task completed", "Task 'Serve interrogatories' completed on 24-CV-100.");

        var feedA = await _fixture.Repository.GetNotificationsForRecipientAsync(recipientA);
        var feedB = await _fixture.Repository.GetNotificationsForRecipientAsync(recipientB);
        Assert.Single(feedA.Items, x => x.NotificationType == "TaskCompleted" && x.CaseId == c.Id);
        Assert.Single(feedB.Items, x => x.NotificationType == "TaskCompleted" && x.CaseId == c.Id);
    }

    [Fact]
    public async Task CreateNotificationsAsync_EmptyRecipientList_CreatesNothingAndDoesNotThrow()
    {
        await _fixture.Repository.CreateNotificationsAsync([], "TaskCompleted", null, "Task completed", "body");
        // No assertion target exists without a recipient id - this is just confirming it's a no-op,
        // not an exception, matching the "no case_assignments -> no recipients -> no error" contract.
    }

    // ---- list / unread count / mark-read / mark-all-read round trip ----

    [Fact]
    public async Task Notifications_ListAndUnreadCount_ReflectReadState()
    {
        var c = await CreateCaseAsync();
        var recipient = Guid.NewGuid().ToString();
        await _fixture.Repository.CreateNotificationsAsync([recipient], "TaskAssigned", c.Id, "Task assigned", "one");
        await _fixture.Repository.CreateNotificationsAsync([recipient], "TaskAssigned", c.Id, "Task assigned", "two");

        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(recipient);
        Assert.Equal(2, feed.Items.Count);
        Assert.Equal(2, feed.UnreadCount);
        // Most-recent-first.
        Assert.Equal("two", feed.Items[0].Body);
    }

    [Fact]
    public async Task MarkNotificationReadAsync_MarksOnlyThatNotificationAndOnlyForItsOwner()
    {
        var recipient = Guid.NewGuid().ToString();
        var otherUser = Guid.NewGuid().ToString();
        await _fixture.Repository.CreateNotificationsAsync([recipient], "TaskAssigned", null, "Task assigned", "one");
        await _fixture.Repository.CreateNotificationsAsync([recipient], "TaskAssigned", null, "Task assigned", "two");
        var feed = await _fixture.Repository.GetNotificationsForRecipientAsync(recipient);
        var toMark = feed.Items.Single(x => x.Body == "one");

        // Another user cannot mark someone else's notification read.
        var wrongOwnerResult = await _fixture.Repository.MarkNotificationReadAsync(toMark.Id, otherUser);
        Assert.False(wrongOwnerResult);

        var result = await _fixture.Repository.MarkNotificationReadAsync(toMark.Id, recipient);
        Assert.True(result);

        var reloaded = await _fixture.Repository.GetNotificationsForRecipientAsync(recipient);
        Assert.Equal(1, reloaded.UnreadCount);
        Assert.True(reloaded.Items.Single(x => x.Body == "one").IsRead);
        Assert.False(reloaded.Items.Single(x => x.Body == "two").IsRead);
    }

    [Fact]
    public async Task MarkAllNotificationsReadAsync_MarksEveryUnreadNotificationForThatRecipientOnly()
    {
        var recipient = Guid.NewGuid().ToString();
        var otherUser = Guid.NewGuid().ToString();
        await _fixture.Repository.CreateNotificationsAsync([recipient], "TaskAssigned", null, "Task assigned", "one");
        await _fixture.Repository.CreateNotificationsAsync([recipient], "TaskAssigned", null, "Task assigned", "two");
        await _fixture.Repository.CreateNotificationsAsync([otherUser], "TaskAssigned", null, "Task assigned", "unrelated");

        await _fixture.Repository.MarkAllNotificationsReadAsync(recipient);

        var reloaded = await _fixture.Repository.GetNotificationsForRecipientAsync(recipient);
        Assert.Equal(0, reloaded.UnreadCount);
        Assert.All(reloaded.Items, x => Assert.True(x.IsRead));

        var otherFeed = await _fixture.Repository.GetNotificationsForRecipientAsync(otherUser);
        Assert.Equal(1, otherFeed.UnreadCount);
    }
}
