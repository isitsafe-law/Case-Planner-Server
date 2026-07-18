using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CasePlanner.Web.Server.Tests;

// Build-plan step 5: the Document Templates admin surface - upload (with linter validation),
// section/overlap/runtime-input configuration, version activation, and deletion.
public sealed class DocumentTemplateAdminTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private static byte[] BuildValidDocx(string bodyText = "Case No. {{CaseNumber}}.")
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(bodyText)))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] BuildUnbalancedDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("{{#Drainage}}")))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    [Fact]
    public async Task UploadingANewTemplateCreatesTemplateAndVersionOne()
    {
        var summary = await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", "a test", "Other", BuildValidDocx());

        Assert.Equal("test_template", summary.Template.TemplateKey);
        Assert.False(summary.Template.IsBuiltin);
        Assert.NotNull(summary.ActiveVersion);
        Assert.Equal(1, summary.ActiveVersion!.Version);
        Assert.Contains("CaseNumber", summary.ActiveVersion.Tokens);
        Assert.True(File.Exists(summary.ActiveVersion.StoragePath));
    }

    [Fact]
    public async Task UploadingAgainToTheSameKeyCreatesVersionTwoAndActivatesIt()
    {
        await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", null, "Other", BuildValidDocx());

        var second = await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", null, "Other", BuildValidDocx("v2 content"));

        Assert.Equal(2, second.ActiveVersion!.Version);
        Assert.Equal(2, second.Versions.Count);
        Assert.Single(second.Versions, v => v.IsActive);
    }

    [Fact]
    public async Task UnbalancedSectionMarkersBlockTheUpload()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.UploadDocumentTemplateAsync("bad_template", "Bad Template", null, "Other", BuildUnbalancedDocx()));

        Assert.Contains("Drainage", ex.Message);
    }

    [Fact]
    public async Task CannotUploadOverABuiltinTemplateKey()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.UploadDocumentTemplateAsync("interrogatories_platform", "Hijack Attempt", null, "Other", BuildValidDocx()));
    }

    [Fact]
    public async Task SavingConfigurationReplacesSectionsOverlapsAndRuntimeInputs()
    {
        await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", null, "Other", BuildValidDocx());

        var request = new DocumentTemplateConfigurationRequest
        {
            Sections =
            [
                new DocumentTemplateSectionRecord { SectionKey = "Access", Label = "Access", IssueTagName = "Access / Change of Access" },
                new DocumentTemplateSectionRecord { SectionKey = "Drainage", Label = "Drainage", IssueTagName = "Drainage" },
            ],
            Overlaps = [new DocumentSectionOverlapPair { SectionAKey = "Access", SectionBKey = "Drainage", Note = "both touch remainder use" }],
            RuntimeInputs = [new DocumentRuntimeInputRecord { FieldKey = "OpposingCounsel", Label = "Opposing Counsel", IsRequired = true }],
        };

        var summary = await _fixture.Repository.SaveDocumentTemplateConfigurationAsync("test_template", request);

        Assert.Equal(2, summary.Sections.Count);
        Assert.Single(summary.Overlaps);
        Assert.Single(summary.RuntimeInputs);
        Assert.Equal("OpposingCounsel", summary.RuntimeInputs[0].FieldKey);

        // Saving again with fewer sections should fully replace, not accumulate.
        var second = await _fixture.Repository.SaveDocumentTemplateConfigurationAsync("test_template", new DocumentTemplateConfigurationRequest
        {
            Sections = [new DocumentTemplateSectionRecord { SectionKey = "Access", Label = "Access" }],
        });
        Assert.Single(second.Sections);
        Assert.Empty(second.Overlaps);
        Assert.Empty(second.RuntimeInputs);
    }

    [Fact]
    public async Task OverlapReferencingASectionNotInTheRequestThrows()
    {
        await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", null, "Other", BuildValidDocx());

        var request = new DocumentTemplateConfigurationRequest
        {
            Sections = [new DocumentTemplateSectionRecord { SectionKey = "Access", Label = "Access" }],
            Overlaps = [new DocumentSectionOverlapPair { SectionAKey = "Access", SectionBKey = "DoesNotExist" }],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SaveDocumentTemplateConfigurationAsync("test_template", request));
    }

    [Fact]
    public async Task ActivatingAnOlderVersionMakesItActiveAgain()
    {
        await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", null, "Other", BuildValidDocx());
        await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", null, "Other", BuildValidDocx("v2"));

        var reactivated = await _fixture.Repository.ActivateDocumentTemplateVersionAsync("test_template", 1);

        Assert.Equal(1, reactivated.ActiveVersion!.Version);
        Assert.Single(reactivated.Versions, v => v.IsActive);
    }

    [Fact]
    public async Task DeletingABuiltinTemplateIsBlockedButCustomTemplatesCanBeDeleted()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.DeleteDocumentTemplateAsync("interrogatories_platform"));

        await _fixture.Repository.UploadDocumentTemplateAsync("test_template", "Test Template", null, "Other", BuildValidDocx());
        await _fixture.Repository.DeleteDocumentTemplateAsync("test_template");

        var all = await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync();
        Assert.DoesNotContain(all, t => t.Template.TemplateKey == "test_template");
    }

    [Fact]
    public async Task GetAllTemplatesIncludesTheSeedTemplate()
    {
        var all = await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync();

        Assert.Contains(all, t => t.Template.TemplateKey == "interrogatories_platform" && t.Template.IsBuiltin);
    }

    [Fact]
    public void SampleMergeFieldTemplateContainsRealFieldsAsRealTags()
    {
        var bytes = DocumentGenerationEngine.BuildSampleMergeFieldTemplateDocx();

        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;

        Assert.Contains("{{CaseNumber}}", text);
        Assert.Contains("{{County}}", text);
        Assert.Contains("{{AttorneyName}}", text);
    }
}
