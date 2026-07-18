using CasePlanner.Web.Server.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CasePlanner.Web.Server.Tests;

public sealed class DocxTemplateLinterTests
{
    private static byte[] BuildDocx(params OpenXmlElement[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph Text(string text) => new(new Run(new DocumentFormat.OpenXml.Wordprocessing.Text(text)));

    [Fact]
    public void CleanBalancedTemplateWithKnownFieldsHasNoIssues()
    {
        var bytes = BuildDocx(
            Text("Case No. {{CaseNumber}}."),
            Text("{{#Drainage}}"),
            Text("Describe the drainage."),
            Text("{{/Drainage}}"));

        var issues = DocxTemplateLinter.Validate(bytes, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CaseNumber" });

        Assert.Empty(issues);
    }

    [Fact]
    public void UnclosedSectionIsReported()
    {
        var bytes = BuildDocx(Text("{{#Drainage}}"), Text("Describe the drainage."));

        var issues = DocxTemplateLinter.Validate(bytes);

        Assert.Contains(issues, i => i.Contains("Drainage") && i.Contains("never closed"));
    }

    [Fact]
    public void StrayCloseWithNoMatchingOpenIsReported()
    {
        var bytes = BuildDocx(Text("{{/Drainage}}"));

        var issues = DocxTemplateLinter.Validate(bytes);

        Assert.Contains(issues, i => i.Contains("Drainage") && i.Contains("never opened"));
    }

    [Fact]
    public void MismatchedCloseNameIsReported()
    {
        var bytes = BuildDocx(Text("{{#Drainage}}"), Text("body"), Text("{{/Access}}"));

        var issues = DocxTemplateLinter.Validate(bytes);

        Assert.Contains(issues, i => i.Contains("Access") && i.Contains("doesn't match"));
    }

    [Fact]
    public void NestedSectionsAreReported()
    {
        var bytes = BuildDocx(
            Text("{{#Drainage}}"),
            Text("{{#Access}}"),
            Text("body"),
            Text("{{/Access}}"),
            Text("{{/Drainage}}"));

        var issues = DocxTemplateLinter.Validate(bytes);

        Assert.Contains(issues, i => i.Contains("Access") && i.Contains("nested"));
    }

    [Fact]
    public void UnknownFieldIsReportedWhenCatalogProvided()
    {
        var bytes = BuildDocx(Text("Case No. {{CaseNubmer}}."));

        var issues = DocxTemplateLinter.Validate(bytes, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CaseNumber" });

        Assert.Contains(issues, i => i.Contains("CaseNubmer") && i.Contains("unknown field"));
    }

    [Fact]
    public void UnknownFieldIsNotReportedWhenNoCatalogGiven()
    {
        var bytes = BuildDocx(Text("Case No. {{AnythingAtAll}}."));

        var issues = DocxTemplateLinter.Validate(bytes, knownFields: null);

        Assert.Empty(issues);
    }

    [Fact]
    public void StrayUnmatchedBraceIsReported()
    {
        // Simulates a token whose "{{"/"}}" landed in different paragraphs (e.g. Word inserted a
        // paragraph break mid-token) - each paragraph on its own has an unmatched brace.
        var bytes = BuildDocx(Text("Case No. {{Case"), Text("Number}}."));

        var issues = DocxTemplateLinter.Validate(bytes);

        Assert.Contains(issues, i => i.Contains("broken merge tag"));
    }
}
