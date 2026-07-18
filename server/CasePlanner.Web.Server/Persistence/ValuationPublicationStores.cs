using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IValuationPositionStore
{
    string Provider { get; }
    Task<List<ValuationPositionRecord>> GetAsync(long? caseId,CancellationToken token=default);
    Task<ValuationPositionRecord> SaveAsync(ValuationPositionRecord model,CancellationToken token=default);
}
public interface IComparableSaleStore
{
    string Provider { get; }
    Task<List<ComparableSaleRecord>> GetAsync(long? caseId,CancellationToken token=default);
    Task<ComparableSaleRecord> SaveAsync(ComparableSaleRecord model,CancellationToken token=default);
    Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default);
}
public interface IPublicationEntryStore
{
    string Provider { get; }
    Task<List<PublicationEntryRecord>> GetAsync(long? caseId,CancellationToken token=default);
    Task<PublicationEntryRecord> SaveAsync(PublicationEntryRecord model,CancellationToken token=default);
    Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default);
}
public interface IPublicationSummaryStore
{
    string Provider { get; }
    Task<List<PublicationRecord>> GetAsync(long? caseId,CancellationToken token=default);
    Task<PublicationRecord?> GetAsync(long caseId,CancellationToken token=default);
    Task<PublicationRecord> SaveAsync(PublicationRecord model,CancellationToken token=default);
}

public sealed class SqliteValuationPositionStore(CasePlannerRepository repository):IValuationPositionStore
{
    public string Provider=>"Sqlite"; public Task<List<ValuationPositionRecord>> GetAsync(long? caseId,CancellationToken token=default)=>repository.GetValuationPositionsAsync(caseId); public Task<ValuationPositionRecord> SaveAsync(ValuationPositionRecord model,CancellationToken token=default)=>repository.SaveValuationPositionAsync(model);
}
public sealed class SqliteComparableSaleStore(CasePlannerRepository repository):IComparableSaleStore
{
    public string Provider=>"Sqlite"; public Task<List<ComparableSaleRecord>> GetAsync(long? caseId,CancellationToken token=default)=>repository.GetComparableSalesAsync(caseId); public Task<ComparableSaleRecord> SaveAsync(ComparableSaleRecord model,CancellationToken token=default)=>repository.SaveComparableSaleAsync(model); public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>repository.DeleteComparableSaleAsync(id);
}
public sealed class SqlitePublicationEntryStore(CasePlannerRepository repository):IPublicationEntryStore
{
    public string Provider=>"Sqlite"; public Task<List<PublicationEntryRecord>> GetAsync(long? caseId,CancellationToken token=default)=>repository.GetPublicationEntriesAsync(caseId); public Task<PublicationEntryRecord> SaveAsync(PublicationEntryRecord model,CancellationToken token=default)=>repository.SavePublicationEntryAsync(model); public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>repository.DeletePublicationEntryAsync(id);
}
public sealed class SqlitePublicationSummaryStore(CasePlannerRepository repository):IPublicationSummaryStore
{
    public string Provider=>"Sqlite";
    public Task<List<PublicationRecord>> GetAsync(long? caseId,CancellationToken token=default)=>repository.GetPublicationRecordsAsync(caseId);
    public Task<PublicationRecord?> GetAsync(long caseId,CancellationToken token=default)=>repository.GetPublicationRecordAsync(caseId);
    public Task<PublicationRecord> SaveAsync(PublicationRecord model,CancellationToken token=default)=>repository.SavePublicationRecordAsync(model);
}
