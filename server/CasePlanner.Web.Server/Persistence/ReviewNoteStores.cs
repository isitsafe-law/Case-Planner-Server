using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Pre-filing sign-off/Settlement Authority final implementation, item 2: an unstructured review-note
// log, deliberately separate in shape from IPreFilingMilestoneStore - see ReviewNoteRecord's doc
// comment (DomainModels.cs) for the full rationale. Same provider-switched shape as
// ICircuitClerkStore/ISettlementAuthorityRequestStore: a plain interface, implemented once per
// provider, selected in Program.cs's DI registration by active database provider. Append-only, like
// IPipelineHolderApprovalStore - read/insert only, no update, no delete, since a review note is never
// edited or retracted once entered.
public interface IReviewNoteStore
{
    string Provider { get; }
    Task<List<ReviewNoteRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<ReviewNoteRecord> CreateAsync(long caseId, CreateReviewNoteRequest request, CancellationToken token = default);
}

public sealed class SqliteReviewNoteStore(CasePlannerRepository repository) : IReviewNoteStore
{
    public string Provider => "Sqlite";
    public Task<List<ReviewNoteRecord>> GetAsync(long? caseId, CancellationToken token = default) =>
        repository.GetReviewNotesAsync(caseId);
    public Task<ReviewNoteRecord> CreateAsync(long caseId, CreateReviewNoteRequest request, CancellationToken token = default) =>
        repository.CreateReviewNoteAsync(caseId, request);
}
