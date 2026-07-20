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

    // Builds a docx whose body opens/closes a well-formed {{#Key}}...{{/Key}} block for each key
    // given, in that order - for exercising auto-section-registration on upload.
    private static byte[] BuildDocxWithSections(params string[] sectionKeys)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(new Paragraph(new Run(new Text("Case No. {{CaseNumber}}."))));
            foreach (var key in sectionKeys)
            {
                body.Append(new Paragraph(new Run(new Text($"{{{{#{key}}}}}"))));
                body.Append(new Paragraph(new Run(new Text($"Some {key} content."))));
                body.Append(new Paragraph(new Run(new Text($"{{{{/{key}}}}}"))));
            }

            main.Document = new Document(body);
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
    public async Task UploadingWithSectionBlocksAutoCreatesSectionRowsWithHumanizedLabels()
    {
        var summary = await _fixture.Repository.UploadDocumentTemplateAsync(
            "section_test", "Section Test", null, "Other", BuildDocxWithSections("FullTaking", "partial_taking_damages"));

        Assert.Equal(2, summary.Sections.Count);

        var full = summary.Sections.Single(s => s.SectionKey == "FullTaking");
        Assert.Equal("Full Taking", full.Label);
        Assert.Null(full.IssueTagName);

        var partial = summary.Sections.Single(s => s.SectionKey == "partial_taking_damages");
        Assert.Equal("Partial Taking Damages", partial.Label);
    }

    [Fact]
    public async Task UploadingNewVersionCarriesForwardSurvivingSectionConfigAndAddsNewKey()
    {
        await _fixture.Repository.UploadDocumentTemplateAsync("carry_test", "Carry Test", null, "Other", BuildDocxWithSections("Access", "Drainage"));

        await _fixture.Repository.SaveDocumentTemplateConfigurationAsync("carry_test", new DocumentTemplateConfigurationRequest
        {
            Sections =
            [
                new DocumentTemplateSectionRecord { SectionKey = "Access", Label = "Access to Property", Description = "Access desc", IssueTagName = "Access / Change of Access", SortOrder = 0 },
                new DocumentTemplateSectionRecord { SectionKey = "Drainage", Label = "Drainage Issues", IssueTagName = "Drainage", SortOrder = 1 },
            ],
            Overlaps = [new DocumentSectionOverlapPair { SectionAKey = "Access", SectionBKey = "Drainage", Note = "both touch remainder use" }],
        });

        // New version drops the Drainage block and adds a brand-new one.
        var second = await _fixture.Repository.UploadDocumentTemplateAsync("carry_test", "Carry Test", null, "Other", BuildDocxWithSections("Access", "NewNoise"));

        Assert.Equal(2, second.Sections.Count);

        var access = second.Sections.Single(s => s.SectionKey == "Access");
        Assert.Equal("Access to Property", access.Label);
        Assert.Equal("Access desc", access.Description);
        Assert.Equal("Access / Change of Access", access.IssueTagName);

        var newNoise = second.Sections.Single(s => s.SectionKey == "NewNoise");
        Assert.Equal("New Noise", newNoise.Label);
        Assert.Null(newNoise.IssueTagName);

        Assert.DoesNotContain(second.Sections, s => s.SectionKey == "Drainage");
        // The overlap named a section (Drainage) that no longer exists in the new version, so it
        // can't carry forward either.
        Assert.Empty(second.Overlaps);
    }

    [Fact]
    public async Task BlankTemplateKeyGeneratesAUniqueSlugFromTitle()
    {
        var first = await _fixture.Repository.UploadDocumentTemplateAsync("", "My Cool Template!", null, "Other", BuildValidDocx());
        Assert.Equal("my_cool_template", first.Template.TemplateKey);

        var second = await _fixture.Repository.UploadDocumentTemplateAsync("", "My Cool Template!", null, "Other", BuildValidDocx("v2 content"));
        Assert.Equal("my_cool_template_2", second.Template.TemplateKey);
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
