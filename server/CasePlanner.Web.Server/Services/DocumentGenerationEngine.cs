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
    // Templates created before the current dotted naming convention used compact or
    // human-readable names. Keep those templates usable without duplicating catalog rows.
    internal static readonly IReadOnlyDictionary<string, string> LegacyTokenAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CaseCaption"] = "Case.Caption",
            ["Case Caption"] = "Case.Caption",
            ["CaseCaptionWithNumber"] = "Case.CaptionWithNumber",
            ["Case Caption With Number"] = "Case.CaptionWithNumber",
            ["CaseStatus"] = "Case.Status",
            ["Case Status"] = "Case.Status",
            ["DateOfTaking"] = "Case.DateOfTaking",
            ["Date of Taking"] = "Case.DateOfTaking",
            ["FullCaseStyle"] = "Case.FullStyle",
            ["Full Case Style"] = "Case.FullStyle",
            ["ShortCaseStyle"] = "Case.ShortStyle",
            ["Short Case Style"] = "Case.ShortStyle",
            ["Opposing Counsel"] = "OpposingCounsel",
            ["PropertyDescription"] = "LegalDescription",
            ["Legal Description"] = "LegalDescription",
            ["Case.LegalDescription"] = "LegalDescription",
        };

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
        new() { Key = "Case.Status", Label = "Case Status", Category = "Case", Description = "Current consolidated case status." },
        new() { Key = "Case.FullStyle", Label = "Full Case Style", Category = "Case", Description = "Stored full style or a basic Arkansas State Highway Commission style." },
        new() { Key = "Case.ShortStyle", Label = "Short Case Style", Category = "Case", Description = "Short working style for the matter." },
        new() { Key = "Case.Caption", Label = "Case Caption", Category = "Case", Description = "Basic caption text without case number." },
        new() { Key = "Case.CaptionWithNumber", Label = "Case Caption with Number", Category = "Case", Description = "Basic caption text with case number." },
        new() { Key = "JobNumber", Label = "Job Number", Category = "Case", Description = "Job number from the case record." },
        new() { Key = "Tract", Label = "Tract", Category = "Case", Description = "Tract identifier." },
        new() { Key = "ProjectName", Label = "Project Name", Category = "Case", Description = "Project name from the case record." },
        new() { Key = "DefendantNames", Label = "Defendant / Landowner Names", Category = "Case", Description = "Landowner if present, otherwise owner." },
        new() { Key = "Case.CourtHeading", Label = "Court Heading", Category = "Case", Description = "Court heading assembled from the case county." },
        new() { Key = "Case.CaseNumberLine", Label = "Case Number Line", Category = "Case", Description = "CASE NO. line for a formatted caption." },
        new() { Key = "Case.DefendantLines", Label = "Defendant / Party Lines", Category = "Case", Description = "Canonical parties separated by line breaks for a formatted Word header." },
        new() { Key = "DepositAmount", Label = "Deposit Amount", Category = "Case", Description = "Initial deposit amount." },
        new() { Key = "FilingDate", Label = "Filing Date", Category = "Case", Description = "Formatted filing date." },
        new() { Key = "Case.DateOfTaking", Label = "Date of Taking", Category = "Case", Description = "Formatted date of taking." },
        new() { Key = "LegalDescription", Label = "Full Legal Description", Category = "Case", Description = "Full legal/property description stored on the case." },
        new() { Key = "Case.TrialDate", Label = "Jury Trial Date", Category = "Events and Deadlines", Description = "Controlling jury trial start date." },
        new() { Key = "Case.TrialEndDate", Label = "Jury Trial End Date", Category = "Events and Deadlines", Description = "Optional jury trial end date." },
        new() { Key = "Workflow.NextAction", Label = "Next Action", Category = "Events and Deadlines", Description = "Current next action." },
        new() { Key = "Workflow.FollowUpDate", Label = "Follow-up Date", Category = "Events and Deadlines", Description = "Current follow-up/review date." },
        new() { Key = "DateOpened", Label = "Date Opened", Category = "Case Lifecycle", Description = "Date the matter was opened." },
        new() { Key = "DateClosed", Label = "Date Closed", Category = "Case Lifecycle", Description = "Date the matter was formally closed." },
        new() { Key = "CaseAgeDays", Label = "Case Age (Days)", Category = "Case Lifecycle", Description = "Current age for open cases, or duration for closed cases." },
        new() { Key = "CaseDurationDays", Label = "Case Duration (Days)", Category = "Case Lifecycle", Description = "Date Closed minus Date Opened." },
        new() { Key = "WholePropertyAcres", Label = "Whole Property Acres", Category = "Case", Description = "Whole property acreage." },
        new() { Key = "AcquisitionAcres", Label = "Acquisition Acres", Category = "Case", Description = "Acquisition acreage." },
        new() { Key = "TaxAmount", Label = "Tax Amount", Category = "Case", Description = "Tax amount owed from the case record." },
        new() { Key = "Project.FapNumber", Label = "FAP Number", Category = "Project and Tract", Description = "Federal Aid Project number." },
        new() { Key = "Project.ParcelNumber", Label = "Parcel Number", Category = "Project and Tract", Description = "Parcel identifier." },
        new() { Key = "Court.Judge", Label = "Judge", Category = "Court", Description = "Assigned judge." },
        new() { Key = "Court.Division", Label = "Court Division", Category = "Court", Description = "Court division." },
        new() { Key = "AssignedAttorney", Label = "Assigned Attorney", Category = "Counsel", Description = "Attorney assigned to the case." },
        new() { Key = "OpposingCounsel", Label = "Opposing Counsel", Category = "Counsel", Description = "Legacy primary opposing counsel field." },
        new() { Key = "OpposingCounselContact", Label = "Opposing Counsel Contact", Category = "Counsel", Description = "Opposing counsel contact block." },
        new() { Key = "Service.Status", Label = "Service Status", Category = "Service and Publication", Description = "Current service status." },
        new() { Key = "Service.PerfectedDate", Label = "Service Perfected Date", Category = "Service and Publication", Description = "Formatted service perfected date." },
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

    public static Dictionary<string, string> BuildTokens(CaseRecord c, OrgDefaults org, Dictionary<string, string> manualInputs, IEnumerable<DocumentTemplateField>? manualFieldDefs = null, IEnumerable<CaseDefendantRecord>? canonicalDefendants = null, IEnumerable<OpposingAttorneyRecord>? opposingAttorneys = null)
    {
        var canonicalNames = canonicalDefendants?
            .Where(defendant => !string.IsNullOrWhiteSpace(defendant.Name))
            .OrderBy(defendant => defendant.SortOrder)
            .ThenBy(defendant => defendant.Id)
            .Select(defendant => defendant.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var defendantNames = canonicalNames.Count > 0
            ? string.Join("; ", canonicalNames)
            : (!string.IsNullOrWhiteSpace(c.Landowner) ? c.Landowner! : (c.Owner ?? ""));
        var fullStyle = string.IsNullOrWhiteSpace(c.CaseStyle)
            ? (string.IsNullOrWhiteSpace(defendantNames) ? "" : $"Arkansas State Highway Commission v. {defendantNames}")
            : c.CaseStyle!;
        var shortStyle = !string.IsNullOrWhiteSpace(defendantNames) ? defendantNames : fullStyle;
        var opposingCounsel = opposingAttorneys?
            .Where(attorney => !string.IsNullOrWhiteSpace(attorney.Name))
            .OrderBy(attorney => attorney.SortOrder)
            .ThenBy(attorney => attorney.Id)
            .Select(attorney => attorney.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var opposingCounselValue = opposingCounsel.Count > 0 ? string.Join("; ", opposingCounsel) : (c.OpposingCounsel ?? "");
        var pastedDefendantLines = ExtractPastedDefendantLines(c.CaseStyle);
        var formattedDefendantLines = pastedDefendantLines.Count > 0
            ? pastedDefendantLines
            : (canonicalNames.Count > 0 ? canonicalNames : (string.IsNullOrWhiteSpace(defendantNames) ? [] : [defendantNames]));
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["County"] = c.County ?? "",
            ["CaseNumber"] = c.CaseNumber ?? "",
            ["JobNumber"] = c.JobNumber ?? "",
            ["Tract"] = c.Tract ?? "",
            ["ProjectName"] = c.ProjectName ?? "",
            ["DefendantNames"] = defendantNames,
            ["Case.CourtHeading"] = string.IsNullOrWhiteSpace(c.County) ? "IN THE CIRCUIT COURT OF ARKANSAS" : $"IN THE CIRCUIT COURT OF {c.County.ToUpperInvariant()} COUNTY, ARKANSAS",
            ["Case.CaseNumberLine"] = string.IsNullOrWhiteSpace(c.CaseNumber) ? "CASE NO." : $"CASE NO. {c.CaseNumber}",
            ["Case.DefendantLines"] = string.Join("\n", formattedDefendantLines) + "\n\tDEFENDANTS",
            ["Case.Status"] = c.CaseStatus ?? "",
            ["Case.FullStyle"] = fullStyle,
            ["Case.ShortStyle"] = shortStyle,
            ["Case.Caption"] = fullStyle,
            ["Case.CaptionWithNumber"] = string.IsNullOrWhiteSpace(c.CaseNumber) ? fullStyle : $"{fullStyle} · Case No. {c.CaseNumber}",
            ["DepositAmount"] = c.DepositAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? "",
            ["FilingDate"] = FormatReadableDate(c.FilingDate),
            ["Case.DateOfTaking"] = FormatReadableDate(c.DateOfTaking),
            ["LegalDescription"] = c.PropertyDescription ?? "",
            ["Case.LegalDescription"] = c.PropertyDescription ?? "",
            ["Case.TrialDate"] = FormatReadableDate(c.TrialDate),
            ["Case.TrialEndDate"] = FormatReadableDate(c.TrialEndDate),
            ["Workflow.NextAction"] = c.NextAction ?? "",
            ["Workflow.FollowUpDate"] = FormatReadableDate(c.NextActionDue),
            ["DateOpened"] = FormatReadableDate(c.DateOpened),
            ["DateClosed"] = FormatReadableDate(c.ClosedDate),
            ["CaseAgeDays"] = LifecycleDays(c.DateOpened, c.ClosedDate)?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["CaseDurationDays"] = LifecycleDurationDays(c.DateOpened, c.ClosedDate)?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["WholePropertyAcres"] = c.WholePropertyAcres?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            ["AcquisitionAcres"] = c.AcquisitionAcres?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            ["TaxAmount"] = c.TaxOwedAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? "",
            ["Project.FapNumber"] = c.FapNumber ?? "",
            ["Project.ParcelNumber"] = c.ParcelNumber ?? "",
            ["Court.Judge"] = c.Judge ?? "",
            ["Court.Division"] = c.Division ?? "",
            ["AssignedAttorney"] = c.AssignedAttorney ?? "",
            ["OpposingCounsel"] = opposingCounselValue,
            ["OpposingCounselContact"] = c.OpposingCounselContact ?? "",
            ["Service.Status"] = c.ServiceStatus ?? "",
            ["Service.PerfectedDate"] = FormatReadableDate(c.ServicePerfectedDate),

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

        foreach (var (alias, canonical) in LegacyTokenAliases)
        {
            if (tokens.TryGetValue(canonical, out var value)) tokens[alias] = value;
        }

        return tokens;
    }

    private static List<string> ExtractPastedDefendantLines(string? caseStyle)
    {
        if (string.IsNullOrWhiteSpace(caseStyle)) return [];
        var lines = caseStyle.Replace("\r\n", "\n").Split('\n').ToList();
        var designation = lines.FindIndex(line => string.Equals(line.Trim(), "DEFENDANTS", StringComparison.OrdinalIgnoreCase));
        if (designation < 1) return [];
        var caseNumber = lines.FindIndex(line => line.Contains("CASE NO.", StringComparison.OrdinalIgnoreCase));
        var start = caseNumber >= 0 ? caseNumber + 1 : Math.Max(0, designation - 1);
        return lines.Skip(start).Take(designation - start)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
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
            .Select(match => match.Groups[1].Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    public static MergeTagAudit AuditTemplateTags(
        IEnumerable<string> discoveredTags,
        IReadOnlyDictionary<string, string> values,
        IEnumerable<string>? additionalKnownTags = null)
    {
        var discovered = discoveredTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToList();
        var known = GetAllTemplateTags().Select(tag => tag.Key)
            .Concat(additionalKnownTags ?? [])
            .Concat(LegacyTokenAliases.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new MergeTagAudit
        {
            DiscoveredTags = discovered,
            KnownTags = discovered.Where(known.Contains).ToList(),
            UnknownTags = discovered.Where(tag => !known.Contains(tag)).ToList(),
            BlankValues = discovered
                .Where(tag => known.Contains(tag) && string.IsNullOrWhiteSpace(FindTokenValue(values, tag)))
                .ToList(),
        };
    }

    private static readonly Regex TokenPattern = TokenRegex();

    public static string FillTemplate(string templateText, Dictionary<string, string> tokens, out List<string> missingTokens)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var result = TokenPattern.Replace(templateText, match =>
        {
            var name = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(FindTokenValue(tokens, name)))
            {
                return FindTokenValue(tokens, name)!;
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
            var name = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(FindTokenValue(tokens, name)))
            {
                return FindTokenValue(tokens, name)!;
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

    private static string? FindTokenValue(IReadOnlyDictionary<string, string> tokens, string name)
    {
        name = name.Trim();
        if (tokens.TryGetValue(name, out var exact)) return exact;
        var insensitive = tokens.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(insensitive.Key)) return insensitive.Value;
        if (LegacyTokenAliases.TryGetValue(name, out var canonical))
        {
            if (tokens.TryGetValue(canonical, out var aliased)) return aliased;
            return tokens.FirstOrDefault(pair => string.Equals(pair.Key, canonical, StringComparison.OrdinalIgnoreCase)).Value;
        }

        return null;
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

    [GeneratedRegex(@"\{\{([A-Za-z0-9_. -]+)\}\}")]
    private static partial Regex TokenRegex();
}
