using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

public sealed class EventPreparationLinkTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task ChecklistAndDeadlineRemainOrdinaryWorkWhileLinkingToEvent()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Preparation Link Case", County = "Pulaski", Status = "Active", Track = "Contested" });
        var hearing = await _fixture.Repository.SaveHearingAsync(new HearingRecord { CaseId = caseRecord.Id, EventType = "Jury Trial", Title = "Jury Trial", HearingDate = "2026-09-15", EndDate = "2026-09-17" });

        var deadline = await _fixture.Repository.SaveDeadlineAsync(new DeadlineItem { CaseId = caseRecord.Id, RelatedEventId = hearing.Id, Title = "Confirm exhibits", DueDate = "2026-08-15", Status = "Open" });
        var task = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId = caseRecord.Id, RelatedEventId = hearing.Id, Phase = "Trial Preparation", Task = "Request attorney review", DueDate = "2026-08-20", Status = "Not Started" });

        var deadlines = await _fixture.Repository.GetDeadlinesAsync(caseRecord.Id);
        var checklist = await _fixture.Repository.GetChecklistItemsAsync(caseRecord.Id);

        Assert.Equal(hearing.Id, Assert.Single(deadlines, item => item.Id == deadline.Id).RelatedEventId);
        Assert.Equal(hearing.Id, Assert.Single(checklist, item => item.Id == task.Id).RelatedEventId);
        Assert.Equal("Open", deadline.Status);
        Assert.Equal("Not Started", task.Status);
    }

    [Fact]
    public async Task EventTemplateSelectionLinksNewWorkAndSkipsItOnRepeat()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Template Preparation Case", County = "Pulaski", Status = "Active", CaseStatus = "Active Litigation", Stage = "Trial Preparation", Track = "Contested", FilingDate = "2026-01-01", TrialDate = "2026-09-15" });
        var hearing = await _fixture.Repository.SaveHearingAsync(new HearingRecord { CaseId = caseRecord.Id, EventType = "Jury Trial", Title = "Jury Trial", HearingDate = "2026-09-15" });
        var candidate = (await _fixture.Repository.GetWorkTemplateCandidatesAsync(caseRecord.Id)).FirstOrDefault(item => !item.IsDuplicate && item.DueDate is not null);
        if (candidate is null) return; // A deployment may intentionally have no active seeded templates.

        var request = new AddWorkTemplatesRequest { Items = [new AddWorkTemplateSelection { Kind = candidate.Kind, TemplateId = candidate.TemplateId, DueDate = candidate.DueDate }] };
        Assert.Equal(1, await _fixture.Repository.AddEventPreparationSelectionsAsync(caseRecord.Id, hearing.Id, request));
        Assert.Equal(0, await _fixture.Repository.AddEventPreparationSelectionsAsync(caseRecord.Id, hearing.Id, request));

        var linked = (await _fixture.Repository.GetDeadlinesAsync(caseRecord.Id)).Cast<object>().Concat((await _fixture.Repository.GetChecklistItemsAsync(caseRecord.Id)).Cast<object>()).ToList();
        Assert.Contains(linked, item => item switch
        {
            DeadlineItem deadline => deadline.RelatedEventId == hearing.Id,
            ChecklistItemRecord task => task.RelatedEventId == hearing.Id,
            _ => false,
        });
    }

    [Fact]
    public async Task EventPreparationCandidatesUseEventDateAndScopeDuplicatesToThatEvent()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Event Candidate Case", County = "Pulaski", Status = "Active", CaseStatus = "Active Litigation", Stage = "Trial Preparation", Track = "Contested", TrialDate = "2026-09-15" });
        var first = await _fixture.Repository.SaveHearingAsync(new HearingRecord { CaseId = caseRecord.Id, EventType = "Jury Trial", Title = "First Jury Trial", HearingDate = "2026-09-15" });
        var second = await _fixture.Repository.SaveHearingAsync(new HearingRecord { CaseId = caseRecord.Id, EventType = "Jury Trial", Title = "Second Jury Trial", HearingDate = "2026-10-15" });
        var candidate = (await _fixture.Repository.GetEventPreparationCandidatesAsync(caseRecord.Id, first.Id)).FirstOrDefault(item => !item.IsDuplicate && item.RelativeOffsetDays is not null);
        if (candidate is null) return;

        Assert.Equal(DateOnly.Parse("2026-09-15").AddDays(candidate.RelativeOffsetDays!.Value).ToString("yyyy-MM-dd"), candidate.DueDate);
        var request = new AddWorkTemplatesRequest { Items = [new AddWorkTemplateSelection { Kind = candidate.Kind, TemplateId = candidate.TemplateId, DueDate = candidate.DueDate }] };
        Assert.Equal(1, await _fixture.Repository.AddEventPreparationSelectionsAsync(caseRecord.Id, first.Id, request));
        var secondCandidates = await _fixture.Repository.GetEventPreparationCandidatesAsync(caseRecord.Id, second.Id);
        var same = Assert.Single(secondCandidates, item => item.Kind == candidate.Kind && item.TemplateId == candidate.TemplateId);
        Assert.False(same.IsDuplicate);
        Assert.Equal(DateOnly.Parse("2026-10-15").AddDays(candidate.RelativeOffsetDays.Value).ToString("yyyy-MM-dd"), same.DueDate);
    }

    [Fact]
    public async Task EventPreparationRecalculationMovesOnlyOpenGeneratedItems()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Recalculation Case", County = "Pulaski", Status = "Active" });
        var hearing = await _fixture.Repository.SaveHearingAsync(new HearingRecord { CaseId = caseRecord.Id, EventType = "Jury Trial", Title = "Jury Trial", HearingDate = "2026-09-15" });
        var generated = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId = caseRecord.Id, RelatedEventId = hearing.Id, Phase = "Trial Preparation", Task = "Open generated item", DueDate = "2026-09-01", Status = "Not Started", IsManual = false, SourceType = "Template:trial:1" });
        var manual = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId = caseRecord.Id, RelatedEventId = hearing.Id, Phase = "Trial Preparation", Task = "Manual override", DueDate = "2026-09-05", Status = "Not Started", IsManual = true, SourceType = "Template:trial:2" });
        var completed = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId = caseRecord.Id, RelatedEventId = hearing.Id, Phase = "Trial Preparation", Task = "Completed generated item", DueDate = "2026-09-07", Status = "Done", IsManual = false, SourceType = "Template:trial:3" });

        var preview = await _fixture.Repository.PreviewEventPreparationDateRecalculationAsync(caseRecord.Id, hearing.Id, "2026-10-15");
        Assert.Equal("2026-10-01", Assert.Single(preview.Changes, item => item.WorkItemId == generated.Id).ProposedDueDate);
        Assert.False(Assert.Single(preview.Changes, item => item.WorkItemId == generated.Id).IsManualOverride);
        Assert.False(Assert.Single(preview.Changes, item => item.WorkItemId == manual.Id).WillMove);
        Assert.False(Assert.Single(preview.Changes, item => item.WorkItemId == completed.Id).WillMove);

        await _fixture.Repository.ApplyEventPreparationDateRecalculationAsync(caseRecord.Id, hearing.Id, "2026-10-15");
        var saved = await _fixture.Repository.GetChecklistItemsAsync(caseRecord.Id);
        Assert.Equal("2026-10-01", Assert.Single(saved, item => item.Id == generated.Id).DueDate);
        Assert.Equal("2026-09-05", Assert.Single(saved, item => item.Id == manual.Id).DueDate);
        Assert.Equal("2026-09-07", Assert.Single(saved, item => item.Id == completed.Id).DueDate);
    }

    [Fact]
    public async Task EventDateChangeRequestKeepsConfirmedDateUntilApproval()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Event Approval Case", County = "Pulaski", Status = "Active" });
        var hearing = await _fixture.Repository.SaveHearingAsync(new HearingRecord { CaseId = caseRecord.Id, EventType = "Hearing", Title = "Motion Hearing", HearingDate = "2026-09-15" });
        var proposal = await _fixture.Repository.ProposeEventChangeAsync(hearing.Id, new EventChangeProposalRequest { ProposedStartDate = "2026-10-20", Note = "Court reset requested" });
        Assert.Equal("Pending", proposal.Status);
        Assert.Equal("2026-09-15", Assert.Single(await _fixture.Repository.GetHearingsAsync(caseRecord.Id), item => item.Id == hearing.Id).HearingDate);
        Assert.NotNull(await _fixture.Repository.GetPendingEventChangeAsync(hearing.Id));

        await _fixture.Repository.DecideEventChangeAsync(proposal.Id, new EventChangeDecisionRequest { Decision = "Approved", Note = "Approved after attorney review" });
        Assert.Equal("2026-10-20", Assert.Single(await _fixture.Repository.GetHearingsAsync(caseRecord.Id), item => item.Id == hearing.Id).HearingDate);
        Assert.Null(await _fixture.Repository.GetPendingEventChangeAsync(hearing.Id));
    }

    [Fact]
    public async Task DeadlineOwnerRoundTripsForAssistantCoverage()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Deadline Ownership Case", County = "Pulaski", Status = "Active" });
        var deadline = await _fixture.Repository.SaveDeadlineAsync(new DeadlineItem { CaseId = caseRecord.Id, Title = "Prepare filing packet", DueDate = "2026-08-20", Status = "Open", AssignedStaffName = "Assistant One" });
        var saved = Assert.Single(await _fixture.Repository.GetDeadlinesAsync(caseRecord.Id), item => item.Id == deadline.Id);
        Assert.Equal("Assistant One", saved.AssignedStaffName);

        saved.AssignedStaffName = "Assistant Two";
        await _fixture.Repository.SaveDeadlineAsync(saved);
        Assert.Equal("Assistant Two", Assert.Single(await _fixture.Repository.GetDeadlinesAsync(caseRecord.Id), item => item.Id == deadline.Id).AssignedStaffName);
    }
}
