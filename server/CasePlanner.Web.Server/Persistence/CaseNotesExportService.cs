using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface ICaseNotesExportService
{
    string Provider { get; }
    Task<FileExportResult> ExportAsync(long caseId,CancellationToken token=default);
}

public sealed class ProviderNeutralCaseNotesExportService(IOperationalWorkspaceQuery workspace,IDocumentStorage documents):ICaseNotesExportService
{
    public string Provider=>workspace is SqlServerWorkspaceQuery?"SqlServer":"Sqlite";

    public async Task<FileExportResult> ExportAsync(long caseId,CancellationToken token=default)
    {
        var result=await workspace.GetWorkspaceAsync(caseId,null,token)??throw new InvalidOperationException("Case not found.");
        var fileName=$"{Clean(result.Case.CaseNumber)}_CaseNotes_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        var path=documents.CreatePath(caseId,fileName);
        var lines=new List<string>{$"{result.Case.CaseName} ({result.Case.CaseNumber})",$"Generated: {DateTime.Now:G}",""};
        if(result.CaseNotes.Count==0)lines.Add("No case notes yet.");
        else foreach(var note in result.CaseNotes){lines.Add(note.Title);lines.Add($"Created: {Display(note.CreatedAt)}");lines.Add($"Last Updated: {Display(note.UpdatedAt)}");lines.Add(note.Body);lines.Add("");}
        await documents.WriteLinesAsync(path,lines,token);
        return new(){Title=$"{result.Case.CaseNumber} Case Notes",OutputPath=path};
    }

    private static string Clean(string value)
    {
        var invalid=Path.GetInvalidFileNameChars();
        var cleaned=new string((value??"Case").Select(ch=>invalid.Contains(ch)?'_':ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned)?"Case":cleaned;
    }
    private static string Display(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return "Not recorded";
        return DateTime.TryParse(value,null,System.Globalization.DateTimeStyles.RoundtripKind,out var parsed)
            ?parsed.ToLocalTime().ToString("G")
            :value;
    }
}
