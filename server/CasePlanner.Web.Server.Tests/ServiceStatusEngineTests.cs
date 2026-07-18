using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class ServiceStatusEngineTests
{
    [Fact]
    public void Build_DerivesDeadlineAndUrgencyFromFilingDate()
    {
        var result=ServiceStatusEngine.Build(new CaseRecord
        {
            Id=1,CaseName="Filed Matter",Status="Active",CaseStatus="Active Litigation",
            FilingDate="2026-01-01",ServiceRequired=true
        },null,new DateOnly(2026,4,25));
        Assert.Equal("2026-05-01",result.ServiceDeadline120);
        Assert.True(result.ServiceDeadlineCalculated);
        Assert.Equal("urgent",result.WarningLevel);
        Assert.Equal(6,result.DaysRemaining);
    }

    [Fact]
    public void BuildQueue_PreservesCaseIdentityAndPublicationDetails()
    {
        var rows=ServiceStatusEngine.BuildQueue(
            [new CaseRecord{Id=7,CaseName="Queue Matter",CaseNumber="CV-7",Status="Active",CaseStatus="Active Litigation",ServiceRequired=true}],
            [new PublicationRecord{CaseId=7,PublicationName="Daily Record",SecondPublicationDate="2026-07-01",MarkedPerfected=true}],
            new DateOnly(2026,7,16));
        var row=Assert.Single(rows);
        Assert.Equal(7,row.CaseId);
        Assert.Equal("Queue Matter",row.CaseName);
    }
}
