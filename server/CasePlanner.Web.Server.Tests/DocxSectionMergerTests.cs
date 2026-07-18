using CasePlanner.Web.Server.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CasePlanner.Web.Server.Tests;

public sealed class DocxSectionMergerTests
{
    // A stand-in "Interrogatories" thin slice: a caption line testing the all-caps tag-case
    // transform, a base interrogatory with a real SEQ field, a {{#Drainage}} section wrapping a
    // second SEQ-numbered interrogatory, a base interrogatory after the section (proving field-
    // code numbering survives on both sides of a resolved section), a run-split merge token with
    // surrounding bold/italic formatting, and a lowercase-tag transform check.
    private static byte[] BuildFixture()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                new Paragraph(new Run(new RunProperties(new Bold()),
                    new Text("IN THE CIRCUIT COURT OF {{COUNTY}} COUNTY, ARKANSAS"))),
                SeqInterrogatory("State the name of the landowner: {{DefendantNames}}."),
                SectionMarker("#Drainage"),
                SeqInterrogatory("Describe any change in drainage."),
                SectionMarker("/Drainage"),
                SeqInterrogatory("List each appraisal obtained for the property."),
                new Paragraph(
                    new Run(new RunProperties(new Bold()), new Text("Case No. ")),
                    new Run(new Text("{{Ca")),
                    new Run(new Text("se")),
                    new Run(new Text("Number}}")),
                    new Run(new RunProperties(new Italic()), new Text("."))),
                new Paragraph(new Run(new Text("county: {{county}}."))));
            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph SeqInterrogatory(string questionText) => new(
        new Run(new Text("INTERROGATORY NO. ")),
        new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
        new Run(new FieldCode(" SEQ RFP ") { Space = SpaceProcessingModeValues.Preserve }),
        new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
        new Run(new Text("1")),
        new Run(new FieldChar { FieldCharType = FieldCharValues.End }),
        new Run(new Text($": {questionText}")));

    private static Paragraph SectionMarker(string marker) => new(new Run(new Text($"{{{{{marker}}}}}")));

    private static Dictionary<string, string> BaseFields() => new()
    {
        ["County"] = "Pulaski",
        ["CaseNumber"] = "2026-001",
        ["DefendantNames"] = "Smith Family Trust",
    };

    [Fact]
    public void SectionIncluded_KeepsContentAndAllFieldCodesAndAppliesTagCaseTransforms()
    {
        var context = MergeContextBuilder.Build(BaseFields(), ["Drainage"]);
        var merged = DocxSectionMerger.Render(BuildFixture(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        Assert.Empty(missing);
        Assert.Contains("PULASKI COUNTY, ARKANSAS", body.InnerText);
        Assert.Contains("county: pulaski.", body.InnerText);
        Assert.Contains("Case No. 2026-001.", body.InnerText);
        Assert.Contains("Describe any change in drainage.", body.InnerText);
        Assert.Equal(9, body.Descendants<FieldChar>().Count());
        Assert.Equal(3, body.Descendants<FieldCode>().Count());
        Assert.DoesNotContain("{{", body.InnerText);

        // Only the runs a match actually overlaps get rewritten - the bold "Case No. " run and
        // the italic "." run sit entirely outside the {{CaseNumber}} match (even though the
        // token itself is split across three separate runs) and must come through completely
        // untouched, formatting included.
        var boldRun = body.Descendants<Run>().First(r => r.InnerText == "Case No. ");
        var italicRun = body.Descendants<Run>().First(r => r.InnerText == ".");
        Assert.NotNull(boldRun.RunProperties?.Bold);
        Assert.NotNull(italicRun.RunProperties?.Italic);
        Assert.Null(boldRun.RunProperties?.Italic);
        Assert.Null(italicRun.RunProperties?.Bold);
    }

    [Fact]
    public void SectionExcluded_RemovesContentAndItsFieldCodesCleanly()
    {
        var context = MergeContextBuilder.Build(BaseFields(), []);
        var merged = DocxSectionMerger.Render(BuildFixture(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        Assert.Empty(missing);
        Assert.DoesNotContain("Describe any change in drainage.", body.InnerText);
        Assert.DoesNotContain("{{", body.InnerText);
        Assert.Contains("State the name of the landowner: Smith Family Trust.", body.InnerText);
        Assert.Contains("List each appraisal obtained for the property.", body.InnerText);
        Assert.Equal(6, body.Descendants<FieldChar>().Count());
        Assert.Equal(2, body.Descendants<FieldCode>().Count());
    }

    [Fact]
    public void SplicesOnlyTheRunsAMatchOverlaps_LeadingAndTrailingTextInTheSameRunSurvive()
    {
        // "{{FooBar}}" is split so the match both starts mid-run-A (after leading text "Before ")
        // and ends mid-run-B (before trailing text " middle "), with a third, wholly untouched
        // italic run after it - the exact shape that used to get flattened into the first run.
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var paragraph = new Paragraph(
                new Run(new RunProperties(new Bold()), new Text("Before {{Foo")),
                new Run(new Text("Bar}} middle ")),
                new Run(new RunProperties(new Italic()), new Text("After")));
            main.Document = new Document(new Body(paragraph));
            main.Document.Save();
        }

        var context = MergeContextBuilder.Build(new Dictionary<string, string> { ["FooBar"] = "X" }, []);
        var merged = DocxSectionMerger.Render(stream.ToArray(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        var runs = doc.MainDocumentPart!.Document.Body!.Descendants<Run>().ToList();

        Assert.Empty(missing);
        Assert.Equal("Before X", runs[0].InnerText);
        Assert.Equal(" middle ", runs[1].InnerText);
        Assert.Equal("After", runs[2].InnerText);
        Assert.NotNull(runs[0].RunProperties?.Bold);
        Assert.Null(runs[1].RunProperties?.Bold);
        Assert.NotNull(runs[2].RunProperties?.Italic);
        Assert.Equal("Before X middle After", doc.MainDocumentPart.Document.Body!.InnerText);
    }

    [Fact]
    public void MissingFieldRendersAsMissingMarkerRatherThanBlank()
    {
        var fields = BaseFields();
        fields.Remove("DefendantNames");
        var context = MergeContextBuilder.Build(fields, ["Drainage"]);

        var merged = DocxSectionMerger.Render(BuildFixture(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        Assert.Contains("DefendantNames", missing);
        Assert.Contains("[MISSING: DefendantNames]", doc.MainDocumentPart!.Document.Body!.InnerText);
    }

    [Fact]
    public void UnclosedSectionThrowsRatherThanSilentlyMisrenderingTheDocument()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(SectionMarker("#Drainage"), new Paragraph(new Run(new Text("orphaned")))));
            main.Document.Save();
        }

        var context = MergeContextBuilder.Build(BaseFields(), ["Drainage"]);

        var ex = Assert.Throws<InvalidOperationException>(() => DocxSectionMerger.Render(stream.ToArray(), context, out _));
        Assert.Contains("Drainage", ex.Message);
    }

    [Fact]
    public void StrayCloseMarkerWithNoMatchingOpenThrows()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(SectionMarker("/Drainage")));
            main.Document.Save();
        }

        var context = MergeContextBuilder.Build(BaseFields(), ["Drainage"]);

        Assert.Throws<InvalidOperationException>(() => DocxSectionMerger.Render(stream.ToArray(), context, out _));
    }

    [Fact]
    public void MultipleIndependentSectionsInOneTemplateEachResolveOnTheirOwn()
    {
        // "All 22 tags as named sections" isn't a new capability by itself - the engine is
        // already data-driven by name - but it needs proving that several different sections in
        // one template don't interfere with each other's keep/drop decision or field codes.
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                SeqInterrogatory("Base question."),
                SectionMarker("#Drainage"),
                SeqInterrogatory("Drainage question."),
                SectionMarker("/Drainage"),
                SectionMarker("#Access"),
                SeqInterrogatory("Access question."),
                SectionMarker("/Access"),
                SectionMarker("#Minerals"),
                SeqInterrogatory("Minerals question."),
                SectionMarker("/Minerals"));
            main.Document = new Document(body);
            main.Document.Save();
        }

        // Drainage and Minerals fire, Access doesn't.
        var context = MergeContextBuilder.Build(BaseFields(), ["Drainage", "Minerals"]);
        var merged = DocxSectionMerger.Render(stream.ToArray(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        var body2 = doc.MainDocumentPart!.Document.Body!;

        Assert.Empty(missing);
        Assert.Contains("Drainage question.", body2.InnerText);
        Assert.Contains("Minerals question.", body2.InnerText);
        Assert.DoesNotContain("Access question.", body2.InnerText);
        Assert.DoesNotContain("{{", body2.InnerText);
        // Base + Drainage + Minerals kept = 3 field-code paragraphs; Access's was removed.
        Assert.Equal(9, body2.Descendants<FieldChar>().Count());
        Assert.Equal(3, body2.Descendants<FieldCode>().Count());
    }

    [Fact]
    public void LoopRepeatsContentOncePerItemWithPerItemFieldsAndSurvivingFieldCodes()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                SeqInterrogatory("State your name and address."),
                SectionMarker("#Landowners"),
                SeqInterrogatory("Identify the ownership interest held by {{Name}}."),
                SectionMarker("/Landowners"),
                SeqInterrogatory("List each appraisal obtained for the property."));
            main.Document = new Document(body);
            main.Document.Save();
        }

        var loops = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["Landowners"] =
            [
                new Dictionary<string, string> { ["Name"] = "John Smith" },
                new Dictionary<string, string> { ["Name"] = "Mary Smith" },
            ],
        };
        var context = MergeContextBuilder.Build(BaseFields(), [], loops);
        var merged = DocxSectionMerger.Render(stream.ToArray(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        var body2 = doc.MainDocumentPart!.Document.Body!;

        Assert.Empty(missing);
        Assert.Contains("Identify the ownership interest held by John Smith.", body2.InnerText);
        Assert.Contains("Identify the ownership interest held by Mary Smith.", body2.InnerText);
        Assert.DoesNotContain("{{", body2.InnerText);
        Assert.Contains("State your name and address.", body2.InnerText);
        Assert.Contains("List each appraisal obtained for the property.", body2.InnerText);
        // 1 base + 2 repeated loop iterations + 1 base = 4 field-code paragraphs.
        Assert.Equal(12, body2.Descendants<FieldChar>().Count());
        Assert.Equal(4, body2.Descendants<FieldCode>().Count());

        // Order preserved: John before Mary, both between the two base questions.
        var order = new[] { "State your name", "John Smith", "Mary Smith", "List each appraisal" }
            .Select(marker => body2.InnerText.IndexOf(marker, StringComparison.Ordinal))
            .ToList();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order, order.OrderBy(x => x));
    }

    [Fact]
    public void LoopWithZeroItemsRemovesContentEntirely()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                SeqInterrogatory("Base question."),
                SectionMarker("#Landowners"),
                SeqInterrogatory("Identify the ownership interest held by {{Name}}."),
                SectionMarker("/Landowners"));
            main.Document = new Document(body);
            main.Document.Save();
        }

        var loops = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["Landowners"] = [],
        };
        var context = MergeContextBuilder.Build(BaseFields(), [], loops);
        var merged = DocxSectionMerger.Render(stream.ToArray(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        var body2 = doc.MainDocumentPart!.Document.Body!;

        Assert.Empty(missing);
        Assert.DoesNotContain("ownership interest", body2.InnerText);
        Assert.Equal(3, body2.Descendants<FieldChar>().Count());
        Assert.Single(body2.Descendants<FieldCode>());
    }

    [Fact]
    public void MissingFieldInsideOneLoopIterationDoesNotAffectOthers()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                SectionMarker("#Landowners"),
                new Paragraph(new Run(new Text("Owner: {{Name}}."))),
                SectionMarker("/Landowners"));
            main.Document = new Document(body);
            main.Document.Save();
        }

        var loops = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["Landowners"] =
            [
                new Dictionary<string, string> { ["Name"] = "John Smith" },
                new Dictionary<string, string>(), // missing Name for this iteration only
            ],
        };
        var context = MergeContextBuilder.Build(BaseFields(), [], loops);
        var merged = DocxSectionMerger.Render(stream.ToArray(), context, out var missing);

        using var opened = new MemoryStream(merged);
        using var doc = WordprocessingDocument.Open(opened, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;

        Assert.Contains("Name", missing);
        Assert.Contains("Owner: John Smith.", text);
        Assert.Contains("Owner: [MISSING: Name].", text);
    }
}
