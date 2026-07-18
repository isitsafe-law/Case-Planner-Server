namespace CasePlanner.Web.Server.Persistence;

public sealed record CutoverCheck(string Area,bool Matches,string Summary);
public sealed record CutoverReadiness(
    bool ReadyForSqlServerActivation,
    bool ReconciledDataMatches,
    string ActiveProvider,
    List<CutoverCheck> Checks,
    List<string> BlockingSurfaces,
    List<string> RequiredOperationalDependencies);

public sealed record DatabaseAdministrationCapabilities(
    string ActiveProvider,
    bool SqlServerPilotWritesEnabled,
    bool SqlServerRuntimeActivationSupported,
    string BackupOwner,
    bool ApplicationFileBackupAvailable,
    bool ApplicationRestoreAvailable,
    bool ApplicationResetAvailable,
    string ImportMode,
    List<string> Notes);

public sealed class CutoverReadinessService(
    IConfiguration configuration,
    CaseCatalogReconciliationService cases,
    WorkItemReconciliationService work,
    DiscoveryReconciliationService discovery,
    CaseWorkspaceReconciliationService workspace,
    LitigationReconciliationService litigation,
    ValuationPublicationReconciliationService valuation,
    ActivityDocumentReconciliationService documents,
    WorkTemplateReconciliationService workTemplates,
    WorkflowGenerationReconciliationService workflowGeneration,
    IssueGenerationReconciliationService issueGenerations,
    OrganizationDefaultsReconciliationService org,
    PublicationSummaryReconciliationService publicationSummaries,
    ReferenceLibraryReconciliationService referenceLibrary)
{
    public async Task<CutoverReadiness> CheckAsync(CancellationToken token=default)
    {
        var checks=new List<CutoverCheck>();
        var c=await cases.CompareAsync(token);checks.Add(new("Cases",c.Matches,$"{c.SqliteCount}/{c.SqlServerCount}"));
        var w=await work.CompareAsync(token);checks.Add(new("Deadlines and checklist",w.Matches,$"{w.SqliteDeadlines}/{w.SqlServerDeadlines} deadlines; {w.SqliteChecklist}/{w.SqlServerChecklist} checklist"));
        var d=await discovery.CompareAsync(token);checks.Add(new("Discovery tracking",d.Matches,$"{d.SqliteCount}/{d.SqlServerCount}"));
        var cw=await workspace.CompareAsync(token);checks.Add(new("Notes and hearings",cw.Matches,$"{cw.SqliteNotes}/{cw.SqlServerNotes} notes; {cw.SqliteHearings}/{cw.SqlServerHearings} hearings"));
        var l=await litigation.CompareAsync(token);checks.Add(new("Litigation workspace",l.Matches,$"{l.SqliteWitnesses}/{l.SqlServerWitnesses} witnesses; {l.SqliteExhibits}/{l.SqlServerExhibits} exhibits; {l.SqliteMotions}/{l.SqlServerMotions} motions"));
        var v=await valuation.CompareAsync(token);checks.Add(new("Valuation and publication",v.Matches,$"{v.SqlitePositions}/{v.SqlServerPositions} positions; {v.SqliteSales}/{v.SqlServerSales} sales; {v.SqlitePublications}/{v.SqlServerPublications} publications"));
        var a=await documents.CompareAsync(token);checks.Add(new("Activity and document metadata",a.Matches,$"{a.SqliteActivities}/{a.SqlServerActivities} activities; {a.SqliteDocuments}/{a.SqlServerDocuments} documents"));
        var wt=await workTemplates.CompareAsync(token);checks.Add(new("Operational templates",wt.Matches,$"{wt.SqliteChecklistTemplates}/{wt.SqlServerChecklistTemplates} checklist; {wt.SqliteDeadlineTemplates}/{wt.SqlServerDeadlineTemplates} deadline"));
        var wg=await workflowGeneration.CompareAsync(token);checks.Add(new("Workflow generation candidates",wg.Matches,$"{wg.CasesCompared} cases compared"));
        var ig=await issueGenerations.CompareAsync(token);checks.Add(new("Issue tags",ig.Matches,$"{ig.SqliteAssignments}/{ig.SqlServerAssignments} assignments"));
        var od=await org.CompareAsync(token);checks.Add(new("Organization defaults",od.Matches,$"{od.Mismatches.Count} mismatch(es)"));
        var ps=await publicationSummaries.CompareAsync(token);checks.Add(new("Publication summaries",ps.Matches,$"{ps.SqliteCount}/{ps.SqlServerCount}"));
        var rl=await referenceLibrary.CompareAsync(token);checks.Add(new("Reference library",rl.Matches,$"{rl.LocalCount}/{rl.SqlServerCount}"));
        var blockers=new List<string>
        {
            "Diagnostics and database-maintenance routes remain SQLite-local; production SQL Server diagnostics and backup/restore belong to IT/DBA procedures.",
            "SQL Excel import does not yet import the Discovery worksheet.",
            "SQLite file backup/restore, sample-data deletion, and database reset must be replaced by DBA-controlled SQL Server procedures.",
            "A read-only maintenance window and final post-snapshot reconciliation are required before changing the active provider."
        };
        return new(false,checks.All(x=>x.Matches),configuration["Database:ActiveProvider"]??"Sqlite",checks,blockers,
        [
            "IT-managed SQL Server backup/restore and point-in-time recovery",
            "Approved central document/template share with service-identity permissions",
            "Microsoft Entra production app registrations and assignment-role validation",
            "Restricted cutover migration credential and least-privileged runtime identity"
        ]);
    }
}
