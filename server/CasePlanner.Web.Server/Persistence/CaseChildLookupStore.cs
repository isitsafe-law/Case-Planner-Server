using CasePlanner.Data;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public interface ICaseChildLookupStore
{
    string Provider { get; }
    Task<long?> GetCaseIdAsync(string kind,long id,CancellationToken token=default);
}

public sealed class SqliteCaseChildLookupStore(CasePlannerRepository repository):ICaseChildLookupStore
{
    public string Provider=>"Sqlite";
    public Task<long?> GetCaseIdAsync(string kind,long id,CancellationToken token=default)=>repository.GetChildCaseIdAsync(kind,id);
}

public sealed class SqlServerCaseChildLookupStore(IDatabaseConnectionFactory connections):ICaseChildLookupStore
{
    private static readonly IReadOnlyDictionary<string,string> Tables=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["case-note"]="case_notes",["hearing"]="hearings",["checklist"]="checklist_items",["deadline"]="deadlines",
        ["comparable-sale"]="comparable_sales",["witness"]="witnesses",["exhibit"]="exhibits",["trial-motion"]="trial_motions",
        ["discovery"]="discovery_tracking",["opposing-attorney"]="case_opposing_attorneys"
    };
    public string Provider=>"SqlServer";
    public async Task<long?> GetCaseIdAsync(string kind,long id,CancellationToken token=default)
    {
        if(!Tables.TryGetValue(kind,out var table))throw new ArgumentException($"Unsupported child record kind: {kind}.");
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();
        command.CommandText=$"SELECT case_id FROM dbo.{table} WHERE id=@id AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@id",id));
        var value=await command.ExecuteScalarAsync(token);
        return value is null?null:Convert.ToInt64(value);
    }
}
