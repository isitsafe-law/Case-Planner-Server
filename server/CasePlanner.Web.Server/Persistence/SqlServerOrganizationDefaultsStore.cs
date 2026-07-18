using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerOrganizationDefaultsStore(
    IDatabaseConnectionFactory connections,
    IHttpContextAccessor accessor,
    IApplicationActorContext actor) : SqlServerWorkItemStoreBase(connections,accessor)
{
    public async Task<OrgDefaults> GetAsync(CancellationToken token=default)
    {
        await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT id,attorney_name,bar_number,phone,email,address_line_1,address_line_2,
                   division_head_name,row_section_head_name,chief_legal_counsel_name,
                   updated_at,updated_by_display,row_version
            FROM dbo.organization_defaults WHERE id=1
            """;
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token))throw new InvalidOperationException("SQL Server organization defaults have not been initialized.");
        return Read(reader);
    }

    public async Task<OrgDefaults> SaveAsync(OrgDefaults model,CancellationToken token=default)
    {
        var now=DateTime.UtcNow.ToString("O");
        await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);await using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="""
            UPDATE dbo.organization_defaults SET attorney_name=@attorney,bar_number=@bar,phone=@phone,
                email=@email,address_line_1=@address1,address_line_2=@address2,
                division_head_name=@division,row_section_head_name=@section,
                chief_legal_counsel_name=@chief,updated_at=@now,updated_by_user_id=@actor,
                updated_by_display=@display
            OUTPUT INSERTED.id,INSERTED.attorney_name,INSERTED.bar_number,INSERTED.phone,INSERTED.email,
                   INSERTED.address_line_1,INSERTED.address_line_2,INSERTED.division_head_name,
                   INSERTED.row_section_head_name,INSERTED.chief_legal_counsel_name,
                   INSERTED.updated_at,INSERTED.updated_by_display,INSERTED.row_version
            WHERE id=1 AND row_version=@version
            """;
        command.Parameters.Add(new SqlParameter("@attorney",Clean(model.AttorneyName)));command.Parameters.Add(new SqlParameter("@bar",Clean(model.BarNumber)));
        command.Parameters.Add(new SqlParameter("@phone",Clean(model.Phone)));command.Parameters.Add(new SqlParameter("@email",Clean(model.Email)));
        command.Parameters.Add(new SqlParameter("@address1",Clean(model.AddressLine1)));command.Parameters.Add(new SqlParameter("@address2",Clean(model.AddressLine2)));
        command.Parameters.Add(new SqlParameter("@division",Clean(model.DivisionHeadName)));command.Parameters.Add(new SqlParameter("@section",Clean(model.RowSectionHeadName)));
        command.Parameters.Add(new SqlParameter("@chief",Clean(model.ChiefLegalCounselName)));command.Parameters.Add(new SqlParameter("@now",now));
        command.Parameters.Add(new SqlParameter("@actor",(object?)ActorUserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));
        command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"organization defaults",1)));
        OrgDefaults saved;await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Organization defaults",1);saved=Read(reader);}
        await using(var audit=connection.CreateCommand()){audit.Transaction=transaction;audit.CommandText="INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(NULL,@actor,'OrganizationDefaultsUpdated','OrganizationDefaults','1')";audit.Parameters.Add(new SqlParameter("@actor",(object?)ActorUserId??DBNull.Value));await audit.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);return saved;
    }

    private static OrgDefaults Read(DbDataReader reader)=>new()
    {
        Id=reader.GetInt64(0),AttorneyName=reader.GetString(1),BarNumber=reader.GetString(2),
        Phone=reader.GetString(3),Email=reader.GetString(4),AddressLine1=reader.GetString(5),
        AddressLine2=reader.GetString(6),DivisionHeadName=reader.GetString(7),
        RowSectionHeadName=reader.GetString(8),ChiefLegalCounselName=reader.GetString(9),
        UpdatedAt=reader.GetString(10),UpdatedBy=reader.IsDBNull(11)?null:reader.GetString(11),
        RowVersion=Convert.ToBase64String((byte[])reader.GetValue(12))
    };
    private static string Clean(string? value)=>value?.Trim()??"";
}
