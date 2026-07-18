using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record OrganizationDefaultsMismatch(string Field,string? SqliteValue,string? SqlServerValue);
public sealed record OrganizationDefaultsReconciliation(bool Matches,List<OrganizationDefaultsMismatch>Mismatches);

public sealed class OrganizationDefaultsReconciliationService(CasePlannerRepository sqlite,SqlServerOrganizationDefaultsStore sql)
{
    public async Task<OrganizationDefaultsReconciliation> CompareAsync(CancellationToken token=default)
    {
        var a=await sqlite.GetOrgDefaultsAsync();var b=await sql.GetAsync(token);var result=new List<OrganizationDefaultsMismatch>();
        C(nameof(a.AttorneyName),a.AttorneyName,b.AttorneyName);C(nameof(a.BarNumber),a.BarNumber,b.BarNumber);
        C(nameof(a.Phone),a.Phone,b.Phone);C(nameof(a.Email),a.Email,b.Email);
        C(nameof(a.AddressLine1),a.AddressLine1,b.AddressLine1);C(nameof(a.AddressLine2),a.AddressLine2,b.AddressLine2);
        C(nameof(a.DivisionHeadName),a.DivisionHeadName,b.DivisionHeadName);
        C(nameof(a.RowSectionHeadName),a.RowSectionHeadName,b.RowSectionHeadName);
        C(nameof(a.ChiefLegalCounselName),a.ChiefLegalCounselName,b.ChiefLegalCounselName);
        return new(result.Count==0,result);
        void C(string field,string? x,string? y){if(!string.Equals(x??"",y??"",StringComparison.Ordinal))result.Add(new(field,x,y));}
    }
}
