using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 5 (reporting) prerequisite: Staff Directory. A fixed list of real
// attorney/legal-assistant names for case metadata and reporting - deliberately separate from the
// dormant Entra-provisioned app_users roster, zero auth/identity dependency, works fully on
// SQLite today (same shape as CaseDispositionFieldsTests' District field). This file only
// exercises the SQLite path; the SQL Server migration (038_staff_directory.sql) has been reviewed
// for consistency with its siblings but not exercised against a live SQL Server here, same
// limitation already noted for every other migration in this repo.
public sealed class StaffDirectoryTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task FreshDatabaseIsSeededWithTheNineAttorneysInOrder()
    {
        var attorneys = await _fixture.Repository.GetAttorneysAsync();

        Assert.Equal(9, attorneys.Count);
        Assert.Equal(
            new[] { "Michelle Davenport", "Angela Dodson", "Helen Newberry", "Stephen Lowman", "Cody Eenigenburg", "Iván Martínez", "Katie Meister", "Michael Bynum", "Bailey Gambill" },
            attorneys.OrderBy(a => a.SortOrder).Select(a => a.Name));
        Assert.All(attorneys, a => Assert.True(a.IsActive));

        Assert.Equal("Chief Counsel", attorneys.Single(a => a.Name == "Michelle Davenport").Title);
        Assert.Equal("Deputy Chief Counsel", attorneys.Single(a => a.Name == "Angela Dodson").Title);
        Assert.Null(attorneys.Single(a => a.Name == "Helen Newberry").Title);

        // Accented characters must round-trip through SQLite exactly, no ASCII-folding.
        Assert.Contains(attorneys, a => a.Name == "Iván Martínez");
    }

    [Fact]
    public async Task FreshDatabaseIsSeededWithTheThreeLegalAssistantsAndCorrectTies()
    {
        var attorneys = await _fixture.Repository.GetAttorneysAsync();
        var legalAssistants = await _fixture.Repository.GetLegalAssistantsAsync();

        Assert.Equal(3, legalAssistants.Count);

        var tyler = legalAssistants.Single(la => la.Name == "Tyler Story");
        Assert.Equal(
            new[] { "Stephen Lowman", "Cody Eenigenburg" }.OrderBy(n => n),
            tyler.AttorneyNames.OrderBy(n => n));

        var evelyn = legalAssistants.Single(la => la.Name == "Evelyn Allison");
        Assert.Equal(
            new[] { "Michael Bynum", "Helen Newberry", "Bailey Gambill" }.OrderBy(n => n),
            evelyn.AttorneyNames.OrderBy(n => n));

        var donna = legalAssistants.Single(la => la.Name == "Donna Ramsey");
        Assert.Equal(
            new[] { "Iván Martínez", "Katie Meister" }.OrderBy(n => n),
            donna.AttorneyNames.OrderBy(n => n));

        // Every AttorneyId on each legal assistant must match a real seeded attorney id.
        foreach (var la in legalAssistants)
        {
            foreach (var attorneyId in la.AttorneyIds)
            {
                Assert.Contains(attorneys, a => a.Id == attorneyId);
            }
        }

        Assert.DoesNotContain(legalAssistants, la => la.Name is "Michelle Davenport" or "Angela Dodson");
    }

    [Fact]
    public async Task ReInitializingDoesNotDuplicateSeedData()
    {
        await _fixture.Repository.InitializeAsync();
        await _fixture.Repository.InitializeAsync();

        var attorneys = await _fixture.Repository.GetAttorneysAsync();
        var legalAssistants = await _fixture.Repository.GetLegalAssistantsAsync();

        Assert.Equal(9, attorneys.Count);
        Assert.Equal(3, legalAssistants.Count);
    }

    [Fact]
    public async Task SaveAttorneyRoundTripsCreateAndUpdate()
    {
        var created = await _fixture.Repository.SaveAttorneyAsync(new AttorneyRecord { Name = "New Attorney", Title = "Associate", IsActive = true });
        Assert.True(created.Id > 0);
        Assert.True(created.SortOrder > 0);

        created.Title = "Senior Associate";
        created.IsActive = false;
        var updated = await _fixture.Repository.SaveAttorneyAsync(created);
        Assert.Equal(created.Id, updated.Id);

        var reloaded = await _fixture.Repository.GetAttorneysAsync();
        var match = Assert.Single(reloaded, a => a.Id == created.Id);
        Assert.Equal("New Attorney", match.Name);
        Assert.Equal("Senior Associate", match.Title);
        Assert.False(match.IsActive);
    }

    [Fact]
    public async Task SaveLegalAssistantRoundTripsCreateAndUpdate()
    {
        var attorneys = await _fixture.Repository.GetAttorneysAsync();
        var first = attorneys.Single(a => a.Name == "Michelle Davenport");

        var created = await _fixture.Repository.SaveLegalAssistantAsync(new LegalAssistantRecord
        {
            Name = "New LA",
            IsActive = true,
            AttorneyIds = [first.Id],
        });
        Assert.True(created.Id > 0);
        Assert.Contains(first.Id, created.AttorneyIds);

        var reloaded = (await _fixture.Repository.GetLegalAssistantsAsync()).Single(la => la.Id == created.Id);
        Assert.Equal(new[] { "Michelle Davenport" }, reloaded.AttorneyNames);
    }

    [Fact]
    public async Task UpdatingSupportedAttorneysReplacesRatherThanMergesTheTieSet()
    {
        var attorneys = await _fixture.Repository.GetAttorneysAsync();
        var stephen = attorneys.Single(a => a.Name == "Stephen Lowman");
        var cody = attorneys.Single(a => a.Name == "Cody Eenigenburg");
        var katie = attorneys.Single(a => a.Name == "Katie Meister");

        var tyler = (await _fixture.Repository.GetLegalAssistantsAsync()).Single(la => la.Name == "Tyler Story");
        Assert.Equal(
            new[] { stephen.Id, cody.Id }.OrderBy(id => id),
            tyler.AttorneyIds.OrderBy(id => id));

        // Reassign Tyler to just Katie Meister - a full replace, not an additive merge.
        tyler.AttorneyIds = [katie.Id];
        await _fixture.Repository.SaveLegalAssistantAsync(tyler);

        var reloaded = (await _fixture.Repository.GetLegalAssistantsAsync()).Single(la => la.Id == tyler.Id);
        Assert.Equal(new[] { katie.Id }, reloaded.AttorneyIds);
        Assert.DoesNotContain(stephen.Id, reloaded.AttorneyIds);
        Assert.DoesNotContain(cody.Id, reloaded.AttorneyIds);
    }
}
