using System.Globalization;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record WorkTemplateMismatch(string Kind, long Id, string Field, string? SqliteValue, string? SqlServerValue);
public sealed record WorkTemplateReconciliation(bool Matches, int SqliteChecklistTemplates, int SqlServerChecklistTemplates, int SqliteDeadlineTemplates, int SqlServerDeadlineTemplates, List<WorkTemplateMismatch> Mismatches);

public sealed class WorkTemplateReconciliationService(CasePlannerRepository sqlite, SqlServerWorkTemplateStore sql)
{
    public async Task<WorkTemplateReconciliation> CompareAsync(CancellationToken token = default)
    {
        var ac = await sqlite.GetChecklistTemplatesAsync();
        var bc = await sql.GetChecklistAsync(token);
        var ad = await sqlite.GetDeadlineTemplatesAsync();
        var bd = await sql.GetDeadlinesAsync(token);
        var mismatches = new List<WorkTemplateMismatch>();
        CompareSets(ac.ToDictionary(x => x.Id), bc.ToDictionary(x => x.Id), "ChecklistTemplate", mismatches, CompareChecklist);
        CompareSets(ad.ToDictionary(x => x.Id), bd.ToDictionary(x => x.Id), "DeadlineTemplate", mismatches, CompareDeadline);
        return new(mismatches.Count == 0, ac.Count, bc.Count, ad.Count, bd.Count, mismatches.Take(100).ToList());

        void CompareChecklist(ChecklistTemplateRecord a, ChecklistTemplateRecord b, long id)
        {
            C("ChecklistTemplate", id, "Name", a.Name, b.Name); C("ChecklistTemplate", id, "TriggerType", a.TriggerType, b.TriggerType);
            C("ChecklistTemplate", id, "Stage", a.Stage, b.Stage); C("ChecklistTemplate", id, "IssueTagName", a.IssueTagName, b.IssueTagName);
            C("ChecklistTemplate", id, "Track", a.Track, b.Track); C("ChecklistTemplate", id, "Active", V(a.Active), V(b.Active));
            CompareSets(a.Items.ToDictionary(x => x.Id), b.Items.ToDictionary(x => x.Id), "ChecklistTemplateItem", mismatches, (x, y, itemId) =>
            {
                C("ChecklistTemplateItem", itemId, "TemplateId", V(x.TemplateId), V(y.TemplateId)); C("ChecklistTemplateItem", itemId, "Task", x.Task, y.Task);
                C("ChecklistTemplateItem", itemId, "Phase", x.Phase, y.Phase); C("ChecklistTemplateItem", itemId, "SortOrder", V(x.SortOrder), V(y.SortOrder));
                C("ChecklistTemplateItem", itemId, "DueOffsetDays", V(x.DueOffsetDays), V(y.DueOffsetDays));
            });
        }
        void CompareDeadline(DeadlineTemplateRecord a, DeadlineTemplateRecord b, long id)
        {
            C("DeadlineTemplate", id, "Name", a.Name, b.Name); C("DeadlineTemplate", id, "TriggerField", a.TriggerField, b.TriggerField);
            C("DeadlineTemplate", id, "OffsetDays", V(a.OffsetDays), V(b.OffsetDays)); C("DeadlineTemplate", id, "Title", a.Title, b.Title);
            C("DeadlineTemplate", id, "Severity", a.Severity, b.Severity); C("DeadlineTemplate", id, "Track", a.Track, b.Track);
            C("DeadlineTemplate", id, "Active", V(a.Active), V(b.Active));
        }
        void C(string kind, long id, string field, string? a, string? b) { if (!string.Equals(a, b, StringComparison.Ordinal)) mismatches.Add(new(kind, id, field, a, b)); }
    }

    private static void CompareSets<T>(Dictionary<long, T> a, Dictionary<long, T> b, string kind, List<WorkTemplateMismatch> result, Action<T, T, long> compare)
    {
        foreach (var id in a.Keys.Except(b.Keys)) result.Add(new(kind, id, "Record", "Present", "Missing"));
        foreach (var id in b.Keys.Except(a.Keys)) result.Add(new(kind, id, "Record", "Missing", "Present"));
        foreach (var id in a.Keys.Intersect(b.Keys)) compare(a[id], b[id], id);
    }
    private static string? V(object? value) => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
}
