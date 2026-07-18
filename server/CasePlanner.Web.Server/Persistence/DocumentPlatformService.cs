using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Build-plan step 4 (unified case UI): the provider-neutral boundary for the new document
// platform, matching every other feature in this app (Sqlite delegates to CasePlannerRepository;
// SQL Server is a separate implementation selected by the same Provider flag in Program.cs).
public interface IDocumentPlatformService
{
    Task<DocumentGenerationChecklist?> GetChecklistAsync(long caseId, string templateKey, CancellationToken token = default);
    Task<DocumentGenerationResult> GenerateAsync(long caseId, string templateKey, DocumentGenerationRequest request, CancellationToken token = default);
    Task<DocumentGenerationRecord?> GetGenerationByIdAsync(long id, CancellationToken token = default);

    // Build-plan step 5 (unified Settings UI): Document Templates admin.
    Task<List<DocumentTemplateAdminSummary>> GetAllTemplatesAsync(CancellationToken token = default);
    Task<DocumentTemplateAdminSummary> UploadTemplateAsync(string templateKey, string title, string? description, string category, byte[] fileBytes, CancellationToken token = default);
    Task<DocumentTemplateAdminSummary> SaveConfigurationAsync(string templateKey, DocumentTemplateConfigurationRequest request, CancellationToken token = default);
    Task<DocumentTemplateAdminSummary> ActivateVersionAsync(string templateKey, int version, CancellationToken token = default);
    Task DeleteTemplateAsync(string templateKey, CancellationToken token = default);
}

public sealed class SqliteDocumentPlatformService(CasePlannerRepository repository) : IDocumentPlatformService
{
    public Task<DocumentGenerationChecklist?> GetChecklistAsync(long caseId, string templateKey, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetDocumentGenerationChecklistAsync(caseId, templateKey);
    }

    public Task<DocumentGenerationResult> GenerateAsync(long caseId, string templateKey, DocumentGenerationRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GenerateDocumentPlatformDocumentAsync(caseId, templateKey, request.SelectedSectionKeys, request.RuntimeInputValues, request.OutputFileName);
    }

    public Task<DocumentGenerationRecord?> GetGenerationByIdAsync(long id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetDocumentGenerationByIdAsync(id);
    }

    public Task<List<DocumentTemplateAdminSummary>> GetAllTemplatesAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetAllDocumentTemplatesForAdminAsync();
    }

    public Task<DocumentTemplateAdminSummary> UploadTemplateAsync(string templateKey, string title, string? description, string category, byte[] fileBytes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.UploadDocumentTemplateAsync(templateKey, title, description, category, fileBytes);
    }

    public Task<DocumentTemplateAdminSummary> SaveConfigurationAsync(string templateKey, DocumentTemplateConfigurationRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.SaveDocumentTemplateConfigurationAsync(templateKey, request);
    }

    public Task<DocumentTemplateAdminSummary> ActivateVersionAsync(string templateKey, int version, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.ActivateDocumentTemplateVersionAsync(templateKey, version);
    }

    public Task DeleteTemplateAsync(string templateKey, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.DeleteDocumentTemplateAsync(templateKey);
    }
}

// Not implemented yet - deliberately, not an oversight. The document-platform schema
// (023_document_platform.sql) exists on SQL Server, but this environment has no SQL Server
// sandbox to develop or test a real ADO.NET implementation against, and shipping one unverified
// would risk exactly the kind of silently-wrong behavior this whole rebuild exists to avoid.
// Fails loudly here rather than quietly reading/writing nothing.
public sealed class SqlServerDocumentPlatformService : IDocumentPlatformService
{
    private const string Message = "The unified document platform's SQL Server implementation is not built yet - see build-plan step 4 in docs/document-system-audit-and-plan.";

    public Task<DocumentGenerationChecklist?> GetChecklistAsync(long caseId, string templateKey, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task<DocumentGenerationResult> GenerateAsync(long caseId, string templateKey, DocumentGenerationRequest request, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task<DocumentGenerationRecord?> GetGenerationByIdAsync(long id, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task<List<DocumentTemplateAdminSummary>> GetAllTemplatesAsync(CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task<DocumentTemplateAdminSummary> UploadTemplateAsync(string templateKey, string title, string? description, string category, byte[] fileBytes, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task<DocumentTemplateAdminSummary> SaveConfigurationAsync(string templateKey, DocumentTemplateConfigurationRequest request, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task<DocumentTemplateAdminSummary> ActivateVersionAsync(string templateKey, int version, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task DeleteTemplateAsync(string templateKey, CancellationToken token = default) =>
        throw new NotSupportedException(Message);
}
