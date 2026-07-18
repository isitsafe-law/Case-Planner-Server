using CasePlanner.Web.Server.Services;
using CasePlanner.Web.Server.Security;

namespace CasePlanner.Web.Server.Tests;

// Builds a real CasePlannerRepository against a throwaway temp-directory SQLite database - the
// plan's "in-memory SQLite connection per test, matching the app's real ADO.NET/SQLite usage
// rather than mocking it away." PathService derives its root by walking two directories up from
// ContentRootPath (mirroring the real server/CasePlanner.Web.Server -> repo-root layout), so the
// fake ContentRootPath is nested two levels under the temp root to match.
//
// Deliberately NOT an xUnit ICollectionFixture (which would share one repository - and one
// SQLite file - across every test in a collection). GetAttorneyDashboardAsync's SummaryCounts
// scan the entire docket, so sharing state across tests would make count assertions
// order-dependent and flaky. Each test class implements IAsyncLifetime itself and creates its
// own fixture, giving every test a fresh, empty (well: 2 seeded demo cases) database.
public sealed class RepositoryTestFixture : IAsyncDisposable
{
    private readonly string _tempRoot;
    public CasePlannerRepository Repository { get; }
    // Matches PathService's DatabasePath derivation (root/data/case_planner_web.sqlite) - exposed
    // so tests can set up pre-migration state (e.g. legacy JSON shapes) directly against the same
    // throwaway temp file the repository uses, then re-run InitializeAsync to exercise a migration.
    public string DatabasePath { get; }
    public string DocumentTemplatesPath { get; }

    private RepositoryTestFixture(string tempRoot, CasePlannerRepository repository)
    {
        _tempRoot = tempRoot;
        Repository = repository;
        DatabasePath = Path.Combine(tempRoot, "data", "case_planner_web.sqlite");
        DocumentTemplatesPath = Path.Combine(tempRoot, "templates", "documents");
    }

    public static async Task<RepositoryTestFixture> CreateAsync(IApplicationActorContext? actor = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "cpw_tests_" + Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(tempRoot, "server", "CasePlanner.Web.Server");
        Directory.CreateDirectory(contentRoot);

        var env = new TestHostEnvironment(contentRoot);
        var pathService = new PathService(env);
        var repository = new CasePlannerRepository(pathService, actor);
        await repository.InitializeAsync();
        return new RepositoryTestFixture(tempRoot, repository);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup - a locked file on Windows shouldn't fail the test run.
        }

        return ValueTask.CompletedTask;
    }
}
