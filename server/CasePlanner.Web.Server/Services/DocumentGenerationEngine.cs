using System.Globalization;
using System.Text.RegularExpressions;
using CasePlanner.Web.Server.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CasePlanner.Web.Server.Services;

// Pure token substitution for the fill-in-the-blank document templates. No DB access —
// callers assemble the CaseRecord/OrgDefaults/manual inputs and pass them in.
public static partial class DocumentGenerationEngine
{
    public static byte[] CreateDocxFromText(string text)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            foreach (var line in (text ?? string.Empty).Split("\r\n", StringSplitOptions.None))
                body.AppendChild(new Paragraph(new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static readonly IReadOnlyList<TemplateTagInfo> AutomaticTags =
    [
        new() { Key = "County", Label = "County", Category = "Case", Description = "Case county." },
        new() { Key = "CaseNumber", Label = "Case Number", Category = "Case", Description = "Case number from the case header." },
        new() { Key = "JobNumber", Label = "Job Number", Category = "Case", Description = "Job number from the case record." },
        new() { Key = "Tract", Label = "Tract", Category = "Case", Description = "Tract identifier." },
        new() { Key = "ProjectName", Label = "Project Name", Category = "Case", Description = "Project name from the case record." },
        new() { Key = "DefendantNames", Label = "Defendant / Landowner Names", Category = "Case", Description = "Landowner if present, otherwise owner." },
        new() { Key = "DepositAmount", Label = "Deposit Amount", Category = "Case", Description = "Initial deposit amount." },
        new() { Key = "FilingDate", Label = "Filing Date", Category = "Case", Description = "Formatted filing date." },
        new() { Key = "DateOpened", Label = "Date Opened", Category = "Case Lifecycle", Description = "Date the matter was opened." },
        new() { Key = "DateClosed", Label = "Date Closed", Category = "Case Lifecycle", Description = "Date the matter was formally closed." },
        new() { Key = "CaseAgeDays", Label = "Case Age (Days)", Category = "Case Lifecycle", Description = "Current age for open cases, or duration for closed cases." },
        new() { Key = "CaseDurationDays", Label = "Case Duration (Days)", Category = "Case Lifecycle", Description = "Date Closed minus Date Opened." },
        new() { Key = "WholePropertyAcres", Label = "Whole Property Acres", Category = "Case", Description = "Whole property acreage." },
        new() { Key = "AcquisitionAcres", Label = "Acquisition Acres", Category = "Case", Description = "Acquisition acreage." },
        new() { Key = "TaxAmount", Label = "Tax Amount", Category = "Case", Description = "Tax amount owed from the case record." },
        new() { Key = "AttorneyName", Label = "Attorney Name", Category = "Organization", Description = "Attorney name from document defaults." },
        new() { Key = "BarNumber", Label = "Bar Number", Category = "Organization", Description = "Attorney bar number from document defaults." },
        new() { Key = "AttorneyPhone", Label = "Attorney Phone", Category = "Organization", Description = "Attorney phone from document defaults." },
        new() { Key = "AttorneyEmail", Label = "Attorney Email", Category = "Organization", Description = "Attorney email from document defaults." },
        new() { Key = "OrgAddressLine1", Label = "Address Line 1", Category = "Organization", Description = "Organization address line 1." },
        new() { Key = "OrgAddressLine2", Label = "Address Line 2", Category = "Organization", Description = "Organization address line 2." },
        new() { Key = "DivisionHeadName", Label = "Division Head Name", Category = "Organization", Description = "Division head name from document defaults." },
        new() { Key = "RowSectionHeadName", Label = "ROW Section Head Name", Category = "Organization", Description = "Right of Way section head name from document defaults." },
        new() { Key = "ChiefLegalCounselName", Label = "Chief Legal Counsel Name", Category = "Organization", Description = "Chief legal counsel name from document defaults." }
    ];

    public static Dictionary<string, string> BuildTokens(CaseRecord c, OrgDefaults org, Dictionary<string, string> manualInputs, IEnumerable<DocumentTemplateField>? manualFieldDefs = null)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["County"] = c.County ?? "",
            ["CaseNumber"] = c.CaseNumber ?? "",
            ["JobNumber"] = c.JobNumber ?? "",
            ["Tract"] = c.Tract ?? "",
            ["ProjectName"] = c.ProjectName ?? "",
            ["DefendantNames"] = !string.IsNullOrWhiteSpace(c.Landowner) ? c.Landowner! : (c.Owner ?? ""),
            ["DepositAmount"] = c.DepositAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? "",
            ["FilingDate"] = FormatReadableDate(c.FilingDate),
            ["DateOpened"] = FormatReadableDate(c.DateOpened),
            ["DateClosed"] = FormatReadableDate(c.ClosedDate),
            ["CaseAgeDays"] = LifecycleDays(c.DateOpened, c.ClosedDate)?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["CaseDurationDays"] = LifecycleDurationDays(c.DateOpened, c.ClosedDate)?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["WholePropertyAcres"] = c.WholePropertyAcres?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            ["AcquisitionAcres"] = c.AcquisitionAcres?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            ["TaxAmount"] = c.TaxOwedAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? "",

            ["AttorneyName"] = org.AttorneyName,
            ["BarNumber"] = org.BarNumber,
            ["AttorneyPhone"] = org.Phone,
            ["AttorneyEmail"] = org.Email,
            ["OrgAddressLine1"] = org.AddressLine1,
            ["OrgAddressLine2"] = org.AddressLine2,
            ["DivisionHeadName"] = org.DivisionHeadName,
            ["RowSectionHeadName"] = org.RowSectionHeadName,
            ["ChiefLegalCounselName"] = org.ChiefLegalCounselName
        };

        var dateFieldKeys = new HashSet<string>(
            (manualFieldDefs ?? []).Where(field => field.Type == "date").Select(field => field.Key),
            StringComparer.Ordinal);
        foreach (var (key, value) in manualInputs)
        {
            tokens[key] = dateFieldKeys.Contains(key) ? FormatReadableDate(value) : (value ?? "");
        }

        return tokens;
    }

    // Build-plan step 5 (Merge Field Catalog): a downloadable .docx listing every known field as a
    // real {{field}} tag, grouped by category, so an attorney can open it in Word and see exactly
    // what a genuine merge looks like rather than reading a table on a settings screen.
    public static byte[] BuildSampleMergeFieldTemplateDocx()
    {
        var tags = GetAllTemplateTags();
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                new Paragraph(new Run(new RunProperties(new Bold()), new Text("Available Merge Fields"))),
                new Paragraph());
            string? currentCategory = null;
            foreach (var tag in tags)
            {
                if (tag.Category != currentCategory)
                {
                    currentCategory = tag.Category;
                    body.AppendChild(new Paragraph(new Run(
                        new RunProperties(new Bold(), new Underline { Val = UnderlineValues.Single }),
                        new Text(currentCategory))));
                }

                body.AppendChild(new Paragraph(new Run(new Text($"{tag.Label}: {{{{{tag.Key}}}}}"))));
            }

            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    // Build-plan step 7 (cleanup): this used to also enumerate DocumentTemplateCatalog's fixed
    // manual-field lists for the 5 old built-in kinds under a "Manual Input" category. Every
    // template's manual/runtime fields are now declared per-template in document_runtime_inputs
    // (build-plan step 5) and surfaced contextually in the generation checklist, so there's no
    // longer a single global list of "manual fields" to fold in here - just the case/org fields
    // every template can already pull from automatically.
    public static IReadOnlyList<TemplateTagInfo> GetAllTemplateTags() =>
        AutomaticTags
            .GroupBy(tag => tag.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(tag => tag.Category)
            .ThenBy(tag => tag.Label)
            .ToList();

    public static List<string> ExtractTokens(string templateText) =>
        TokenPattern.Matches(templateText)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    private static readonly Regex TokenPattern = TokenRegex();

    public static string FillTemplate(string templateText, Dictionary<string, string> tokens, out List<string> missingTokens)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var result = TokenPattern.Replace(templateText, match =>
        {
            var name = match.Groups[1].Value;
            if (tokens.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            missing.Add(name);
            return $"[MISSING: {name}]";
        });

        missingTokens = missing.ToList();
        return result;
    }

    private static string FormatReadableDate(string? isoDate) =>
        DateOnly.TryParse(isoDate, out var d) ? d.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) : "";

    public static int? LifecycleDays(string? opened, string? closed)
    {
        if (!DateOnly.TryParse(opened, out var start)) return null;
        var end = DateOnly.TryParse(closed, out var closedDate) ? closedDate : DateOnly.FromDateTime(DateTime.Today);
        return end < start ? null : end.DayNumber - start.DayNumber;
    }

    public static int? LifecycleDurationDays(string? opened, string? closed) =>
        DateOnly.TryParse(closed, out _) ? LifecycleDays(opened, closed) : null;

    // Concatenates every Text run in body + headers/footers and reuses ExtractTokens on the
    // combined string - a letterhead's {{CaseNumber}} in a header counts the same as one in the body.
    public static List<string> ExtractTokensFromDocx(byte[] docxBytes)
    {
        using var stream = new MemoryStream(docxBytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        return ExtractTokens(string.Concat(AllTextParts(doc).SelectMany(part => part.Descendants<Text>()).Select(t => t.Text)));
    }

    public static string ExtractEditableTextFromDocx(byte[] docxBytes)
    {
        using var stream = new MemoryStream(docxBytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var paragraphs = AllTextParts(doc).SelectMany(part => part.Descendants<Paragraph>())
            .Select(paragraph => string.Concat(paragraph.Descendants<Text>().Select(text => text.Text)).Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    // Merges tokens directly into a .docx's XML, preserving surrounding formatting. Word commonly
    // splits a single typed "{{Token}}" across multiple <w:t> runs (autocorrect, spell-check,
    // mid-typing formatting changes), so substitution can't just target one Text node at a time -
    // per paragraph, the full run text is concatenated, substituted as one string, then written
    // back into the first run with the rest of that paragraph's runs blanked. This collapses any
    // run-level formatting differences *within* a substituted token's span, but everything outside
    // a token (and every paragraph with no tokens at all) keeps its original formatting untouched.
    public static byte[] FillDocxTemplate(byte[] templateBytes, Dictionary<string, string> tokens, out List<string> missingTokens)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        stream.Write(templateBytes, 0, templateBytes.Length);
        stream.Position = 0;
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            foreach (var part in AllTextParts(doc))
            {
                foreach (var paragraph in part.Descendants<Paragraph>().ToList())
                {
                    MergeTokensInParagraph(paragraph, tokens, missing);
                }
            }

            doc.MainDocumentPart!.Document.Save();
            if (doc.MainDocumentPart.HeaderParts is not null)
            {
                foreach (var header in doc.MainDocumentPart.HeaderParts) header.Header.Save();
            }

            if (doc.MainDocumentPart.FooterParts is not null)
            {
                foreach (var footer in doc.MainDocumentPart.FooterParts) footer.Footer.Save();
            }
        }

        missingTokens = missing.ToList();
        return stream.ToArray();
    }

    private static void MergeTokensInParagraph(Paragraph paragraph, Dictionary<string, string> tokens, SortedSet<string> missing)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count == 0)
        {
            return;
        }

        var combined = string.Concat(texts.Select(t => t.Text));
        if (!combined.Contains("{{", StringComparison.Ordinal))
        {
            return;
        }

        var replaced = TokenPattern.Replace(combined, match =>
        {
            var name = match.Groups[1].Value;
            if (tokens.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            missing.Add(name);
            return $"[MISSING: {name}]";
        });

        if (replaced == combined)
        {
            return;
        }

        texts[0].Text = replaced;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
        {
            texts[i].Text = "";
        }
    }

    // Body + every header/footer part - anywhere text (and therefore a token) can live in a docx.
    private static IEnumerable<OpenXmlPartRootElement> AllTextParts(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart ?? throw new InvalidOperationException("Document has no main part.");
        yield return main.Document;
        if (main.HeaderParts is not null)
        {
            foreach (var header in main.HeaderParts) yield return header.Header;
        }

        if (main.FooterParts is not null)
        {
            foreach (var footer in main.FooterParts) yield return footer.Footer;
        }
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenRegex();
}
