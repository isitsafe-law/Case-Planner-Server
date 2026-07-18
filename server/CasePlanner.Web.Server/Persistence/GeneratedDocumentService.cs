using System.Globalization;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IGeneratedDocumentService
{
    Task<List<DocumentExportRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<DocumentExportRecord?> GetByIdAsync(long id, CancellationToken token = default);
    Task<long?> GetCaseIdAsync(long id, CancellationToken token = default);
    Task<string?> GetContentAsync(long id, CancellationToken token = default);
    Task<DocumentExportRecord> GenerateBasicAsync(long caseId, string kind, CancellationToken token = default);
    Task<DocumentExportRecord> SaveQaAsync(long id, DocumentExportRecord model, CancellationToken token = default);
}

public interface IBinaryGeneratedDocumentService
{
    Task<DocumentExportRecord> SaveAsync(long caseId, SaveGeneratedDocumentRequest request, byte[] content, string extension = ".docx", CancellationToken token = default);
}

public static class BasicDocumentComposer
{
    public static string BuildText(CaseWorkspaceResponse w, string title)
    {
        static string Date(string? value) => DateOnly.TryParse(value, out var d) ? d.ToString("MM/dd/yyyy") : "Not set";
        var service = w.ServiceStatus;
        var deadline = service.ServiceDeadline120 is null ? "Not set" : service.DaysRemaining is < 0
            ? $"{service.ServiceDeadline120} ({Math.Abs(service.DaysRemaining.Value)} days overdue)"
            : service.DaysRemaining is { } days ? $"{service.ServiceDeadline120} ({days} days remaining)" : service.ServiceDeadline120;
        return $"{title}{Environment.NewLine}Case: {w.Case.CaseName} ({w.Case.CaseNumber}){Environment.NewLine}Job / Tract: {w.Case.JobNumber} / {w.Case.Tract}{Environment.NewLine}County / Status: {w.Case.County} / {w.Case.Status}{Environment.NewLine}Trial Date: {Date(w.Case.TrialDate)}{Environment.NewLine}Next Action: {w.Case.NextAction ?? "Not set"}{Environment.NewLine}Next Action Due: {Date(w.Case.NextActionDue)}{Environment.NewLine}Deposit Amount: {(w.Case.DepositAmount?.ToString("C", CultureInfo.CurrentCulture) ?? "Not set")}{Environment.NewLine}{Environment.NewLine}Service Status{Environment.NewLine}- Service perfected: {(service.ServicePerfected ? "Yes" : "No")}{Environment.NewLine}- 120-day service deadline: {deadline}{Environment.NewLine}{Environment.NewLine}Deadlines{Environment.NewLine}{string.Join(Environment.NewLine, w.Deadlines.Select(x => $"- {x.Title} [{x.Status}] due {Date(x.DueDate)}"))}{Environment.NewLine}{Environment.NewLine}Checklist{Environment.NewLine}{string.Join(Environment.NewLine, w.ChecklistItems.Select(x => $"- {x.Phase}: {x.Task} [{x.Status}] due {Date(x.DueDate)}"))}{Environment.NewLine}{Environment.NewLine}Discovery{Environment.NewLine}{string.Join(Environment.NewLine, w.DiscoveryItems.Select(x => $"- {x.Direction} {x.DiscoveryType} [{x.Status}] due {Date(x.DueDate)}"))}{Environment.NewLine}{Environment.NewLine}Issue Tags{Environment.NewLine}{string.Join(Environment.NewLine, w.CaseIssueTags.Select(x => $"- {x.TagName}"))}";
    }
}

public sealed class SqliteGeneratedDocumentService(CasePlannerRepository repository) : IGeneratedDocumentService
{
    public Task<List<DocumentExportRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetDocumentExportsAsync(caseId);
    }
    public Task<DocumentExportRecord?> GetByIdAsync(long id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetDocumentExportByIdAsync(id);
    }
    public Task<long?> GetCaseIdAsync(long id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetChildCaseIdAsync("document-export",id);
    }
    public Task<string?> GetContentAsync(long id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetDocumentExportContentAsync(id);
    }
    public Task<DocumentExportRecord> GenerateBasicAsync(long caseId,string kind,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GenerateDocumentAsync(caseId,kind);
    }
    public Task<DocumentExportRecord> SaveQaAsync(long id,DocumentExportRecord model,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();model.Id=id;return repository.SaveDocumentQaAsync(model);
    }
}

public sealed class SqliteBinaryGeneratedDocumentService(CasePlannerRepository repository) : IBinaryGeneratedDocumentService
{
    public Task<DocumentExportRecord> SaveAsync(long caseId, SaveGeneratedDocumentRequest request, byte[] content, string extension = ".docx", CancellationToken token = default) =>
        repository.SaveGeneratedBinaryDocumentAsync(caseId, request, content, extension, token);
}

public sealed class SqlServerGeneratedDocumentService(
    SqlServerDocumentExportStore store,
    SqlServerDocumentPilotService generator,
    IOperationalWorkspaceQuery workspaces,
    IDocumentStorage documents) : IGeneratedDocumentService
{
    public Task<List<DocumentExportRecord>> GetAsync(long? caseId,CancellationToken token=default)=>store.GetAsync(caseId,token);
    public async Task<DocumentExportRecord?> GetByIdAsync(long id,CancellationToken token=default)=>(await store.GetAsync(null,token)).FirstOrDefault(x=>x.Id==id);
    public async Task<long?> GetCaseIdAsync(long id,CancellationToken token=default)=>(await GetByIdAsync(id,token))?.CaseId;
    public async Task<string?> GetContentAsync(long id,CancellationToken token=default)
    {
        var record=await GetByIdAsync(id,token);if(record is null)return null;
        return record.ContentText??await documents.ReadTextAsync(record.OutputPath,token);
    }
    public async Task<DocumentExportRecord> GenerateBasicAsync(long caseId,string kind,CancellationToken token=default)
    {
        var workspace=await workspaces.GetWorkspaceAsync(caseId,null,token)??throw new InvalidOperationException("Case not found.");
        var title=kind=="summary"?"Case Summary":"Case Review Memo";
        return await generator.GenerateTextAsync(caseId,new(){Kind=title,Title=title,Text=BasicDocumentComposer.BuildText(workspace,title),IsFinalized=true},token);
    }
    public Task<DocumentExportRecord> SaveQaAsync(long id,DocumentExportRecord model,CancellationToken token=default)=>store.SaveQaAsync(id,model.QaStatus,model.QaNotes,model.RowVersion,token);

}

public sealed class SqlServerBinaryGeneratedDocumentService(SqlServerDocumentExportStore store, IDocumentStorage documents, IHttpContextAccessor httpContext) : IBinaryGeneratedDocumentService
{
    public async Task<DocumentExportRecord> SaveAsync(long caseId, SaveGeneratedDocumentRequest request, byte[] content, string extension = ".docx", CancellationToken token = default)
    {
        var safe = string.Join("_", request.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = "GeneratedDocument";
        var path = documents.CreatePath(caseId, $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{safe}{extension}");
        await documents.WriteBytesAsync(path, content, token);
        try
        {
            return await store.SaveGeneratedAsync(new DocumentExportRecord
            {
                CaseId = caseId, DocumentType = request.Kind, DocumentTitle = request.Title, OutputPath = path,
                ContentText = null, BaseTemplateVersion = request.BaseTemplateVersion, IssueTagVersions = request.IssueTagVersions,
                MergeFieldValues = request.MergeFieldValues, IsDraft = request.IsDraft, IsFinalized = request.IsFinalized,
                CreatedByDisplay = httpContext.HttpContext?.User?.Identity?.Name
            }, token);
        }
        catch { await documents.DeleteIfExistsAsync(path, token); throw; }
    }
}
