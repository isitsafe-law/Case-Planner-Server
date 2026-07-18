using CasePlanner.Web.Server.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CasePlanner.Web.Server.Tests;

public sealed class DocxGenerationTests
{
    [Fact]
    public void CreateDocxFromTextProducesOpenXmlDocument()
    {
        var bytes = DocumentGenerationEngine.CreateDocxFromText("INTERROGATORY NO. 1\nIdentify the property.");

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        Assert.NotNull(document.MainDocumentPart?.Document.Body);
        Assert.Contains("INTERROGATORY NO. 1", document.MainDocumentPart!.Document.Body!.InnerText);
    }

    [Fact]
    public void FillDocxTemplateReplacesTokensSplitAcrossRunsAndKeepsUnrelatedFormatting()
    {
        using var source = new MemoryStream();
        using (var document = WordprocessingDocument.Create(source, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var paragraph = new Paragraph(
                new Run(new RunProperties(new Bold()), new Text("Letterhead")),
                new Run(new Text(" {{Case")),
                new Run(new Text("Number}}")));
            main.Document = new Document(new Body(paragraph));
            main.Document.Save();
        }

        var merged = DocumentGenerationEngine.FillDocxTemplate(source.ToArray(),
            new Dictionary<string, string> { ["CaseNumber"] = "2026-001" }, out var missing);

        using var result = new MemoryStream(merged);
        using var opened = WordprocessingDocument.Open(result, false);
        var body = opened.MainDocumentPart!.Document.Body!;
        Assert.Empty(missing);
        Assert.Contains("Letterhead 2026-001", body.InnerText);
        Assert.NotNull(body.Descendants<RunProperties>().FirstOrDefault()?.Bold);
    }
}
