using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface ICaseImportService
{
    string Provider { get; }
    Task<ImportResult> ImportCsvAsync(Stream stream,CancellationToken token=default);
    Task<ImportResult> ImportXlsxAsync(Stream stream,CancellationToken token=default);
}

public sealed class SqliteCaseImportService(CasePlannerRepository repository):ICaseImportService
{
    public string Provider=>"Sqlite";
    public Task<ImportResult> ImportCsvAsync(Stream stream,CancellationToken token=default)=>repository.ImportCasesCsvAsync(stream);
    public Task<ImportResult> ImportXlsxAsync(Stream stream,CancellationToken token=default)=>repository.ImportCasesXlsxAsync(stream);
}

public sealed class SqlServerCaseImportAdapter(SqlServerCaseImportService service):ICaseImportService
{
    public string Provider=>"SqlServer";
    public Task<ImportResult> ImportCsvAsync(Stream stream,CancellationToken token=default)=>service.ImportCsvAsync(stream,token);
    public Task<ImportResult> ImportXlsxAsync(Stream stream,CancellationToken token=default)=>service.ImportXlsxAsync(stream,token);
}
