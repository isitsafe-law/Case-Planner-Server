using CasePlanner.Web.Server.Models;
using System.Globalization;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

namespace CasePlanner.Web.Server.Services;

// Builds a .xlsx that mirrors the office's real "Risk Analysis" workbook cell-for-cell: the same
// header block, the same 11-column ledger at the same fixed row numbers (14/15/17/18/20/21/23/24)
// with live Excel formulas (not baked values) reproducing RiskAnalysisEngine's math, and the same
// "OLD OFFERS" log below the narrative. Any change to a formula in RiskAnalysisEngine must be
// mirrored here, and vice versa - the two are independent re-implementations of the same math,
// one for the live web ledger and one for the exported spreadsheet.
public static class RiskAnalysisExcelExportService
{
    private const string CurrencyFormat = "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)";
    private const string DateFormat = "M/d/yyyy";

    private static readonly string[] HourlyFeeOptions =
        ["20000", "25000", "30000", "35000", "40000", "45000", "50000", "55000", "60000", "65000", "70000", "75000", "80000", "85000", "90000"];

    public static byte[] BuildWorkbook(CaseRecord c, RiskAnalysisResult analysis, IReadOnlyList<RiskAnalysisOfferLogEntry> offerLog)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Risk Analysis");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        // GetRiskAnalysisAsync returns primary rows plus their computed "Split" rows in the same
        // flat list - only the primary (non-split) rows carry the raw inputs this export needs,
        // since the 3 split rows here are always rebuilt from formulas, not from saved data.
        var rowsByKey = analysis.Rows.Where(r => !r.IsSplit).ToDictionary(r => r.RowKey, r => r);

        WriteHeaderBlock(ws, c, analysis);
        WriteDepositRows(ws, c);
        WriteTableHeader(ws);

        WritePrimaryRow(ws, 14, "LANDOWNER'S OPINION OF VALUE", rowsByKey.GetValueOrDefault("LandownerOpinionOfValue"), null, analysis);
        WritePrimaryRow(ws, 15, "LANDOWNER'S APPRAISAL", rowsByKey.GetValueOrDefault("LandownerAppraisal"), null, analysis);
        WritePrimaryRow(ws, 17, null, rowsByKey.GetValueOrDefault("AshcFirstOffer"), ["LANDOWNER FIRST OFFER", "ASHC FIRST OFFER"], analysis);
        WriteSplitRow(ws, 18, 17, analysis);
        WritePrimaryRow(ws, 20, null, rowsByKey.GetValueOrDefault("AshcCounteroffer"), ["LANDOWNER'S COUNTEROFFER", "ASHC COUNTEROFFER"], analysis);
        WriteSplitRow(ws, 21, 20, analysis);
        WritePrimaryRow(ws, 23, null, rowsByKey.GetValueOrDefault("LandownerCounteroffer"), ["LANDOWNER'S COUNTEROFFER", "ASHC COUNTEROFFER"], analysis);
        WriteSplitRow(ws, 24, 23, analysis);

        WriteNarrative(ws, analysis.Narrative);
        WriteOldOffers(ws, offerLog);
        ApplySummaryAlignment(ws);
        ApplyRowBanding(ws);
        ApplyColumnWidths(ws);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return SetFullCalculationOnLoad(stream.ToArray());
    }

    private static void ApplySummaryAlignment(IXLWorksheet ws)
    {
        var summary = ws.Range("A1:D8");
        summary.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        summary.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void ApplyRowBanding(IXLWorksheet ws)
    {
        var rows = new[] { 14, 15, 17, 18, 20, 21, 23, 24 };
        for (var index = 0; index < rows.Length; index++)
        {
            if (index % 2 == 1)
            {
                ws.Range(rows[index], 1, rows[index], 11).Style.Fill.BackgroundColor = XLColor.AliceBlue;
            }
        }
    }

    private static void WriteHeaderBlock(IXLWorksheet ws, CaseRecord c, RiskAnalysisResult analysis)
    {
        void Label(string addr, string text)
        {
            var cell = ws.Cell(addr);
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 12;
        }

        Label("A1", "NAME:");
        ws.Cell("B1").Value = c.CaseName;
        ws.Cell("B1").Style.Font.FontSize = 18;

        Label("A2", "CASE NUMBER:");
        ws.Cell("B2").Value = c.CaseNumber;
        Label("C2", "WHOLE PROPERTY SIZE:");
        if (c.WholePropertyAcres is { } wpa) ws.Cell("D2").Value = wpa;

        Label("A3", "COUNTY:");
        ws.Cell("B3").Value = c.County;
        Label("C3", "ACQUISITION SIZE:");
        if (c.AcquisitionAcres is { } aa) ws.Cell("D3").Value = aa;

        Label("A4", "FILE DATE:");
        SetDateCell(ws.Cell("B4"), c.FilingDate);
        Label("C4", "ATTORNEY(S):");
        ws.Cell("D4").Value = c.AssignedAttorney;

        Label("A5", "JOB NUMBER:");
        ws.Cell("B5").Value = c.JobNumber;
        Label("C5", "APPRAISER (LO):");
        ws.Cell("D5").Value = c.LandownerAppraiserName;

        Label("A6", "TODAY'S DATE:");
        SetDateCell(ws.Cell("B6"), analysis.AnalysisDate);
        Label("C6", "APPRAISER (ASHC):");
        ws.Cell("D6").Value = c.Appraiser;

        Label("A7", "DAYS SINCE FILING:");
        ws.Cell("B7").FormulaA1 = "IF(OR(B4=0,B6=0),\"-\",(DATEDIF(B4,B6,\"D\")))";
        Label("C7", "ADD'L DEPOSIT DATE:");
        SetDateCell(ws.Cell("D7"), c.AdditionalDepositDate);

        Label("A8", "TRACT:");
        ws.Cell("B8").Value = c.Tract;
    }

    private static void WriteDepositRows(IXLWorksheet ws, CaseRecord c)
    {
        ws.Cell("A10").Value = "INITIAL DEPOSIT ";
        if (c.DepositAmount is { } dep) SetCurrencyCell(ws.Cell("B10"), dep);

        ws.Cell("A11").Value = "ADD'L DEPOSIT(S)";
        if (c.AdditionalDepositAmount is { } addDep) SetCurrencyCell(ws.Cell("B11"), addDep);

        ws.Cell("A12").Value = "TOTAL DEPOSITED";
        var totalCell = ws.Cell("B12");
        totalCell.FormulaA1 = "SUM(B10:B11)";
        ApplyCurrencyFormat(totalCell);
    }

    private static void WriteTableHeader(IXLWorksheet ws)
    {
        string[] headers =
        [
            "SOURCE", "JUST COMPENSATION", "AMOUNT ABOVE INITIAL DEPOSIT",
            "APPROX. INTEREST ON OVERAGE AT 6% PER ANNUM", "SUBTOTAL W/O COSTS & FEES ADDED",
            "LANDOWNER APPRAISAL FEES AND COSTS", "ASHC COSTS (DEPOSITIONS, APPRAISER EXPERT FEES)",
            "POTENTIAL HOURLY ATTORNEY'S FEES AWARD RISK", "POTENTIAL CONTINGENCY FEE  (30% OF SUBTOTAL)",
            "TOTAL POTENTIAL RISK USING HOURLY COSTS AND FEES", "TOTAL POTENTIAL RISK USING CONTINGENCY FEES"
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(9, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws.Row(9).Height = 57.6;
    }

    // hourlyFeeOptions non-null marks this as one of the 3 dropdown-driven offer/counteroffer
    // slots; fixedLabel non-null marks a static row (Opinion of Value / Appraisal).
    private static void WritePrimaryRow(IXLWorksheet ws, int row, string? fixedLabel, RiskAnalysisRowResult? input, string[]? dropdownOptions, RiskAnalysisResult analysis)
    {
        var labelCell = ws.Cell(row, 1);
        labelCell.Value = fixedLabel ?? input?.Label ?? (dropdownOptions is not null ? dropdownOptions[0] : "");
        labelCell.Style.Alignment.WrapText = true;
        if (dropdownOptions is not null)
        {
            labelCell.CreateDataValidation().List(ListFormula(dropdownOptions));
        }

        if (input?.JustCompensation is { } jc)
        {
            SetCurrencyCell(ws.Cell(row, 2), jc);
        }
        else
        {
            ApplyCurrencyFormat(ws.Cell(row, 2));
        }

        var cCell = ws.Cell(row, 3);
        cCell.FormulaA1 = $"IF(OR(B{row}=\"\",B{row}=0),0,SUM(B{row}-$B$10))";
        ApplyCurrencyFormat(cCell);

        var dCell = ws.Cell(row, 4);
        var rate = analysis.InterestRate.ToString(CultureInfo.InvariantCulture);
        var contingency = analysis.ContingencyFeePercent.ToString(CultureInfo.InvariantCulture);
        dCell.FormulaA1 = $"IF(AND(OR($B$4=\"\",$B$6=\"\"),B{row}>0),\"Add File and/or Today's Date\"," +
            $"(MAX(B{row}-$B$10,0)*{rate}*(MAX(MIN(IF(AND($B$11<>\"\",$D$7<>\"\"),$D$7,$B$6),$B$6)-$B$4,0))/365)" +
            $"+(IF(AND($B$11<>\"\",$D$7<>\"\"),MAX(B{row}-$B$10-$B$11,0)*{rate}*(MAX($B$6-$D$7,0))/365,0)))";
        ApplyCurrencyFormat(dCell);

        var eCell = ws.Cell(row, 5);
        eCell.FormulaA1 = $"IF(OR(B{row}=\"\",D{row}=\"Add File and/or Today's Date\"),0,SUM(B{row}+D{row}))";
        ApplyCurrencyFormat(eCell);

        var fCell = ws.Cell(row, 6);
        fCell.Value = input?.LandownerFeesCosts ?? 0m;
        ApplyCurrencyFormat(fCell);

        var gCell = ws.Cell(row, 7);
        gCell.Value = input?.AshcCosts ?? 0m;
        ApplyCurrencyFormat(gCell);

        var hCell = ws.Cell(row, 8);
        hCell.Value = input?.HourlyFeesRisk ?? 40000m;
        ApplyCurrencyFormat(hCell);
        hCell.CreateDataValidation().List(ListFormula(HourlyFeeOptions));

        var iCell = ws.Cell(row, 9);
        iCell.FormulaA1 = $"IF(B{row}>0,E{row}*{contingency},0)";
        ApplyCurrencyFormat(iCell);

        var jCell = ws.Cell(row, 10);
        jCell.FormulaA1 = $"IF(OR(B{row}=0,B{row}=\"\"),0,IF(AND(B{row}>0,((B{row}-$B$12)/$B$12>=0.2)),SUM(E{row}+F{row}+G{row}+H{row}),\"Below 20% Threshold\"))";
        ApplyCurrencyFormat(jCell);

        var kCell = ws.Cell(row, 11);
        kCell.FormulaA1 = $"IF(B{row}>0,SUM(E{row}+G{row}+I{row}),0)";
        ApplyCurrencyFormat(kCell);
    }

    private static void WriteSplitRow(IXLWorksheet ws, int row, int parentRow, RiskAnalysisResult analysis)
    {
        ws.Cell(row, 1).Value = "SPLIT";

        var bCell = ws.Cell(row, 2);
        bCell.FormulaA1 = $"IF(B{parentRow}=\"\",0,SUM($B$12+B{parentRow})/2)";
        ApplyCurrencyFormat(bCell);

        var cCell = ws.Cell(row, 3);
        cCell.FormulaA1 = $"IF(OR(B{row}=\"\",B{row}=0),0,SUM(B{row}-$B$10))";
        ApplyCurrencyFormat(cCell);

        var dCell = ws.Cell(row, 4);
        var rate = analysis.InterestRate.ToString(CultureInfo.InvariantCulture);
        var contingency = analysis.ContingencyFeePercent.ToString(CultureInfo.InvariantCulture);
        dCell.FormulaA1 = $"(MAX(B{row}-$B$10,0)*{rate}*(MAX(MIN(IF(AND($B$11<>\"\",$D$7<>\"\"),$D$7,$B$6),$B$6)-$B$4,0))/365)" +
            $"+(IF(AND($B$11<>\"\",$D$7<>\"\"),MAX(B{row}-$B$10-$B$11,0)*{rate}*(MAX($B$6-$D$7,0))/365,0))";
        ApplyCurrencyFormat(dCell);

        var eCell = ws.Cell(row, 5);
        eCell.FormulaA1 = $"IF(B{row}>0,SUM(B{row}+D{row}),0)";
        ApplyCurrencyFormat(eCell);

        var fCell = ws.Cell(row, 6);
        fCell.FormulaA1 = $"F{parentRow}";
        ApplyCurrencyFormat(fCell);

        var gCell = ws.Cell(row, 7);
        gCell.FormulaA1 = $"G{parentRow}";
        ApplyCurrencyFormat(gCell);

        var hCell = ws.Cell(row, 8);
        hCell.FormulaA1 = $"H{parentRow}";
        ApplyCurrencyFormat(hCell);

        var iCell = ws.Cell(row, 9);
        iCell.FormulaA1 = $"IF(B{row}>0,E{row}*{contingency},0)";
        ApplyCurrencyFormat(iCell);

        var jCell = ws.Cell(row, 10);
        jCell.FormulaA1 = $"IF(OR(B{row}=0,B{row}=\"\"),0,IF(AND(B{row}>0,((B{row}-$B$12)/$B$12>=0.2)),SUM(E{row}+F{row}+G{row}+H{row}),\"Below 20% Threshold\"))";
        ApplyCurrencyFormat(jCell);

        var kCell = ws.Cell(row, 11);
        kCell.FormulaA1 = $"IF(B{row}>0,SUM(E{row}+G{row}+I{row}),0)";
        ApplyCurrencyFormat(kCell);
    }

    private static void WriteNarrative(IXLWorksheet ws, string? narrative)
    {
        ws.Range("A25:K25").Merge();
        var cell = ws.Cell("A25");
        cell.Value = string.IsNullOrWhiteSpace(narrative) ? "NARRATIVE" : narrative;
        cell.Style.Alignment.WrapText = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(25).Height = 86.4;
    }

    private static void WriteOldOffers(IXLWorksheet ws, IReadOnlyList<RiskAnalysisOfferLogEntry> offerLog)
    {
        ws.Cell("A27").Value = "OLD OFFERS (LIST BELOW)";
        ws.Cell("A27").Style.Font.Bold = true;

        ws.Cell("A28").Value = "Date";
        ws.Cell("B28").Value = "Party";
        ws.Cell("C28").Value = "Amount";
        foreach (var addr in new[] { "A28", "B28", "C28" })
        {
            ws.Cell(addr).Style.Font.Bold = true;
        }

        var row = 29;
        foreach (var entry in offerLog.OrderBy(e => e.OfferDate ?? ""))
        {
            SetDateCell(ws.Cell(row, 1), entry.OfferDate);
            ws.Cell(row, 2).Value = entry.Party;
            if (entry.Amount is { } amount) SetCurrencyCell(ws.Cell(row, 3), amount);
            row++;
        }
    }

    private static void ApplyColumnWidths(IXLWorksheet ws)
    {
        double[] widths = [22.66, 18.78, 23.33, 18.55, 13.44, 16.22, 13.55, 15.78, 17.0, 16.11, 16.44];
        for (var i = 0; i < widths.Length; i++)
        {
            ws.Column(i + 1).Width = widths[i];
        }
    }

    private static string ListFormula(IEnumerable<string> options) => $"\"{string.Join(",", options)}\"";

    private static void SetCurrencyCell(IXLCell cell, decimal value)
    {
        cell.Value = value;
        ApplyCurrencyFormat(cell);
    }

    private static void ApplyCurrencyFormat(IXLCell cell) => cell.Style.NumberFormat.Format = CurrencyFormat;

    private static void SetDateCell(IXLCell cell, string? isoDate)
    {
        if (DateTime.TryParse(isoDate, out var date))
        {
            cell.Value = date;
            cell.Style.DateFormat.Format = DateFormat;
        }
    }

    // ClosedXML writes formula strings with no cached value, so Excel shows blanks/zeros until a
    // manual recalculation unless the workbook is flagged to recalculate everything on open.
    private static byte[] SetFullCalculationOnLoad(byte[] xlsxBytes)
    {
        using var ms = new MemoryStream();
        ms.Write(xlsxBytes, 0, xlsxBytes.Length);
        ms.Position = 0;
        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var workbookPart = doc.WorkbookPart!;
            var calcProps = workbookPart.Workbook.CalculationProperties;
            if (calcProps is null)
            {
                calcProps = new DocumentFormat.OpenXml.Spreadsheet.CalculationProperties();
                workbookPart.Workbook.AppendChild(calcProps);
            }
            calcProps.FullCalculationOnLoad = true;
            workbookPart.Workbook.Save();
        }
        return ms.ToArray();
    }
}
