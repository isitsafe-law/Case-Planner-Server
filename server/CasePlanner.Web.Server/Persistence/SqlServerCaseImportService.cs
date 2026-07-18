using ClosedXML.Excel;
using CasePlanner.Web.Server.Models;
using Microsoft.VisualBasic.FileIO;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerCaseImportService(SqlServerCaseCatalogReader cases)
{
    public async Task<ImportResult> ImportCsvAsync(Stream stream,CancellationToken token=default)
    {
        var result=new ImportResult();var existing=await cases.GetCasesAsync(new(IncludeClosed:true),token);
        using var parser=new TextFieldParser(stream){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};parser.SetDelimiters(",");
        if(parser.EndOfData)return result;var headers=parser.ReadFields()??[];var map=headers.Select((h,i)=>(h:h.Trim(),i)).ToDictionary(x=>x.h,x=>x.i,StringComparer.OrdinalIgnoreCase);
        while(!parser.EndOfData)
        {
            var row=parser.ReadFields();if(row is null)continue;result.RowsRead++;
            try
            {
                var caseNumber=Field(row,map,"Case Number");var job=Field(row,map,"Job Number");var tract=Field(row,map,"Tract");
                if(string.IsNullOrWhiteSpace(caseNumber)&&string.IsNullOrWhiteSpace(job)&&string.IsNullOrWhiteSpace(tract)){result.Skipped++;result.Errors.Add($"Row {result.RowsRead}: missing identifier.");continue;}
                var current=Match(existing,caseNumber,job,tract);var status=Field(row,map,"Status");
                var model=current??new CaseRecord();
                model.CaseNumber=caseNumber;model.CaseName=Field(row,map,"Case Name");model.JobNumber=job;model.Tract=tract;model.County=Field(row,map,"County");
                model.Status=current is null?(status is "Closed" or "Complete"?status:"Triage"):(current.Status=="Triage"?"Triage":status);
                model.FilingDate=Date(Field(row,map,"Filing Date"));model.DateOfTaking=Date(Field(row,map,"Date of Taking"));model.TrialDate=Date(Field(row,map,"Trial Date"));
                model.NextAction=Blank(Field(row,map,"Next Action"));model.NextActionDue=Date(Field(row,map,"Next Action Due"));model.DepositAmount=Money(Field(row,map,"Deposit Amount"));
                model.Owner=Blank(Field(row,map,"Owner"));model.Landowner=Blank(Field(row,map,"Landowner"));model.PublicationServiceNotes=Blank(Field(row,map,"Notes"));
                model.ServiceRequired=Bool(Field(row,map,"Service Required"),true);model.ServicePerfected=Bool(Field(row,map,"Service Perfected"));
                model.ServicePerfectedDate=Date(Field(row,map,"Service Perfected Date"));model.ServiceDeadlineBasisDate=Date(Field(row,map,"Service Deadline Basis Date"));
                model.ServiceDeadline120=Date(Field(row,map,"Service Deadline 120"));model.ServiceMethod=Blank(Field(row,map,"Service Method"));
                model.ServiceStatus=Blank(Field(row,map,"Service Status"));model.ServiceNotes=Blank(Field(row,map,"Service Notes"));
                var saved=await cases.SaveCaseAsync(model,token);if(current is null){result.Created++;existing.Add(saved);}else result.Updated++;
            }
            catch(Exception ex){result.Skipped++;result.Errors.Add($"Row {result.RowsRead}: {ex.Message}");}
        }
        return result;
    }

    public async Task<ImportResult> ImportXlsxAsync(Stream stream,CancellationToken token=default)
    {
        using var workbook=new XLWorkbook(stream);var result=new ImportResult();var existing=await cases.GetCasesAsync(new(IncludeClosed:true),token);
        foreach(var sheetName in new[]{"Open","Closed"})
        {
            if(!workbook.TryGetWorksheet(sheetName,out var sheet)){result.Info.Add($"Sheet '{sheetName}' not found; skipped.");continue;}
            var header=sheet.FirstRowUsed();if(header is null)continue;var map=header.CellsUsed().ToDictionary(x=>x.GetString().Trim(),x=>x.Address.ColumnNumber,StringComparer.OrdinalIgnoreCase);
            foreach(var row in sheet.RowsUsed().Skip(1))
            {
                var caseNumber=Cell(row,map,"CASE NO.");var job=Cell(row,map,"JOB");var tract=Cell(row,map,"TRACT NO.");var name=Cell(row,map,"CASE NAME");
                if(string.IsNullOrWhiteSpace(caseNumber)&&string.IsNullOrWhiteSpace(job)&&string.IsNullOrWhiteSpace(name))continue;result.RowsRead++;
                try
                {
                    var current=Match(existing,caseNumber,job,tract);var model=current??new CaseRecord();var closed=sheetName=="Closed";
                    model.CaseNumber=caseNumber;model.CaseName=string.IsNullOrWhiteSpace(name)?caseNumber:name;model.JobNumber=job;model.Tract=tract;model.County=Cell(row,map,"COUNTY");
                    model.Status=closed?"Closed":current is null||current.Status=="Triage"?"Triage":"Active";model.FilingDate=CellDate(row,map,"DATE FILED");
                    model.DateOfTaking=CellDate(row,map,"DATE OF TAKING");model.TrialDate=CellDate(row,map,"TRIAL DATE");model.DateOpened=CellDate(row,map,"DATE OPENED");model.DepositAmount=CellMoney(row,map,"DEPOSIT");
                    model.PublicationServiceNotes=Blank(Cell(row,map,"NOTES"));model.AssignedAttorney=Blank(Cell(row,map,"ATTY"));model.OpposingCounsel=Blank(Cell(row,map,"ATTORNEY"));
                    model.Appraiser=Blank(Cell(row,map,"APPR"));model.TaxesOwed=Blank(Cell(row,map,"TAXES OWED?"));model.FundsWithdrawn=Blank(Cell(row,map,"FUNDS W/D?"));
                    model.DiscoveryCompleted=Blank(Cell(row,map,"DISCOVERY COMPLETED?"));model.UpdatedAppraisal=Blank(Cell(row,map,"UPDATED APPRAISAL?"));
                    model.ClosedDate=closed?CellDate(row,map,"CLOSED DATE"):null;model.ServiceRequired=true;
                    var saved=await cases.SaveCaseAsync(model,token);if(current is null){result.Created++;existing.Add(saved);}else result.Updated++;
                }
                catch(Exception ex){result.Skipped++;result.Errors.Add($"{sheetName} row {row.RowNumber()}: {ex.Message}");}
            }
        }
        if(workbook.TryGetWorksheet("Discovery",out _))result.Info.Add("Discovery sheet was detected but not imported by the SQL case-catalog pilot; use the discovery reconciliation/import cutover procedure before activation.");
        return result;
    }

    private static CaseRecord? Match(IEnumerable<CaseRecord> rows,string caseNumber,string job,string tract)=>rows.FirstOrDefault(x=>
        (!string.IsNullOrWhiteSpace(caseNumber)&&string.Equals(x.CaseNumber,caseNumber,StringComparison.OrdinalIgnoreCase))||
        (!string.IsNullOrWhiteSpace(job)&&!string.IsNullOrWhiteSpace(tract)&&
         string.Equals(x.JobNumber,job,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Tract,tract,StringComparison.OrdinalIgnoreCase)));
    private static string Field(string[] row,Dictionary<string,int> map,string key)=>map.TryGetValue(key,out var i)&&i<row.Length?row[i].Trim():"";
    private static string Cell(IXLRow row,Dictionary<string,int> map,string key)=>map.TryGetValue(key,out var c)?row.Cell(c).GetFormattedString().Trim():"";
    private static string? CellDate(IXLRow row,Dictionary<string,int> map,string key){if(!map.TryGetValue(key,out var c))return null;var cell=row.Cell(c);if(cell.TryGetValue<DateTime>(out var dt))return dt.Date==new DateTime(1900,1,1)?null:dt.ToString("yyyy-MM-dd");return Date(cell.GetFormattedString());}
    private static decimal? CellMoney(IXLRow row,Dictionary<string,int> map,string key){if(!map.TryGetValue(key,out var c))return null;var cell=row.Cell(c);return cell.TryGetValue<decimal>(out var value)?value:Money(cell.GetFormattedString());}
    private static string? Date(string? value)=>DateOnly.TryParse(value,out var d)&&d!=new DateOnly(1900,1,1)?d.ToString("yyyy-MM-dd"):null;
    private static string? Blank(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static decimal? Money(string? value)=>decimal.TryParse((value??"").Replace("$","").Replace(",",""),out var result)?result:null;
    private static bool Bool(string? value,bool fallback=false)=>string.IsNullOrWhiteSpace(value)?fallback:value.Trim().Equals("yes",StringComparison.OrdinalIgnoreCase)||value.Trim().Equals("true",StringComparison.OrdinalIgnoreCase)||value.Trim()=="1";
}
