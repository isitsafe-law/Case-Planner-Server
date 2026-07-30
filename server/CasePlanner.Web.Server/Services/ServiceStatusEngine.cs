using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

public static class ServiceStatusEngine
{
    private static readonly HashSet<string> StagesPastService = new(StringComparer.Ordinal)
    {
        "Discovery & Evaluation","Trial Track","Resolved"
    };

    public static ServiceStatusSummary Build(CaseRecord caseRecord,PublicationRecord? publication,DateOnly? asOf=null)
    {
        var today=asOf??DateOnly.FromDateTime(DateTime.Today);
        var basis=Date(caseRecord.ServiceDeadlineBasisDate)??Date(caseRecord.FilingDate);
        var manual=Date(caseRecord.ServiceDeadline120);
        var deadline=manual??basis?.AddDays(120);
        var result=new ServiceStatusSummary
        {
            ServiceRequired=caseRecord.ServiceRequired,ServicePerfected=caseRecord.ServicePerfected,
            ServicePerfectedDate=Normalize(caseRecord.ServicePerfectedDate),ServiceDeadline120=deadline?.ToString("yyyy-MM-dd"),
            ServiceDeadlineBasisDate=basis?.ToString("yyyy-MM-dd"),ServiceMethod=Blank(caseRecord.ServiceMethod),
            ServiceStatus=Blank(caseRecord.ServiceStatus),ServiceNotes=Blank(caseRecord.ServiceNotes),
            ServiceDeadlineCalculated=manual is null&&basis is not null,PublicationEntryExists=publication is not null,
            PublicationDate=publication?.SecondPublicationDate??publication?.FirstPublicationDate,
            Newspaper=Blank(publication?.PublicationName),
            PublicationNotes=Blank(caseRecord.PublicationServiceNotes)
        };
        if(!result.ServiceRequired)return Set(result,"none","Service is not required for this case.");
        if(caseRecord.Status=="Triage")return Set(result,"none","Case is in triage; complete intake to activate service tracking.");
        var closed=caseRecord.CaseStatus is "Resolved / Closed" or "Closed" or "Complete"||caseRecord.Status is "Closed" or "Complete";
        if(caseRecord.CaseStatus is "Pipeline" or "Triage")return Set(result,"none","Case is not filed; service tracking begins after filing.");
        var past=!string.IsNullOrEmpty(caseRecord.Stage)&&StagesPastService.Contains(caseRecord.Stage);
        if(result.ServicePerfected||closed||past)return Set(result,"resolved",result.ServicePerfected?$"Service perfected on {Display(result.ServicePerfectedDate)}.":closed?"Case is closed; service tracking no longer applies.":"Case has progressed past the Service stage.");
        if(deadline is null)return Set(result,"missing","Service deadline not set.");
        result.DaysSinceFiling = basis is { } filing ? Math.Max(0, today.DayNumber - filing.DayNumber) : null;
        result.DaysRemaining=deadline.Value.DayNumber-today.DayNumber;
        var days = result.DaysSinceFiling ?? 0;
        if (result.DaysRemaining <= 0) return Set(result, "overdue", result.DaysRemaining == 0 ? "Service deadline is today." : $"Service deadline passed {Math.Abs(result.DaysRemaining.Value)} day(s) ago.");
        if (days >= 115) return Set(result, "urgent", $"Service pending · {result.DaysRemaining} day(s) remaining before the service deadline.");
        if (days >= 105) return Set(result, "high", $"Service pending · {result.DaysRemaining} day(s) remaining before the service deadline.");
        if (days >= 90) return Set(result, "warning", $"Service remains pending · about {result.DaysRemaining} day(s) remain before the service deadline.");
        if (days >= 60) return Set(result, "checkin", "Service remains pending at the 60-day check-in point. Review current service efforts.");
        return Set(result, "normal", $"Service pending · {result.DaysRemaining} day(s) remaining.");
    }

    public static List<ServiceQueueItem> BuildQueue(IEnumerable<CaseRecord> cases,IEnumerable<PublicationRecord> publications,DateOnly? asOf=null)=>
        cases.Select(c=>{var p=publications.FirstOrDefault(x=>x.CaseId==c.Id);var s=Build(c,p,asOf);return new ServiceQueueItem
        {CaseId=c.Id,CaseName=c.CaseName,CaseNumber=c.CaseNumber,JobNumber=c.JobNumber,Tract=c.Tract,County=c.County,
        FilingDate=c.FilingDate,ServiceDeadlineBasisDate=s.ServiceDeadlineBasisDate,ServiceDeadline120=s.ServiceDeadline120,
        DaysRemaining=s.DaysRemaining,DaysSinceFiling=s.DaysSinceFiling,ServiceRequired=s.ServiceRequired,ServicePerfected=s.ServicePerfected,
        ServicePerfectedDate=s.ServicePerfectedDate,ServiceMethod=s.ServiceMethod,ServiceStatus=s.ServiceStatus,
        NotesPreview=s.ServiceNotes??s.PublicationNotes,WarningLevel=s.WarningLevel,WarningText=s.WarningText};}).ToList();

    private static ServiceStatusSummary Set(ServiceStatusSummary value,string level,string text){value.WarningLevel=level;value.WarningText=text;return value;}
    private static DateOnly? Date(string? value)=>DateOnly.TryParse(value,out var date)&&date!=new DateOnly(1900,1,1)?date:null;
    private static string? Normalize(string? value)=>Date(value)?.ToString("yyyy-MM-dd");
    private static string? Blank(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string Display(string? value)=>DateOnly.TryParse(value,out var date)?date.ToString("MMM d, yyyy"):"an unknown date";
}
