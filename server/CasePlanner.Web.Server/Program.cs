using Microsoft.Extensions.FileProviders;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using ClosedXML.Excel;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? builder.Configuration["Hosting:Urls"] ?? "http://127.0.0.1:5188");

var activeProvider = builder.Configuration["Database:ActiveProvider"] ?? DatabaseProviders.Sqlite;
if (!activeProvider.Equals(DatabaseProviders.Sqlite, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "SQL Server runtime activation is intentionally blocked because some normal application routes still use the SQLite repository. " +
        "Use the SQL Server pilot/import endpoints and /api/database/cutover-readiness to finish and verify the migration without creating split writes.");
}

var migrationTarget = new DatabaseOptions
{
    Provider = DatabaseProviders.SqlServer,
    ConnectionString = builder.Configuration.GetConnectionString("CasePlannerSqlServer"),
    CommandTimeoutSeconds = builder.Configuration.GetValue("Database:CommandTimeoutSeconds", 30)
};
builder.Services.AddSingleton(migrationTarget);
builder.Services.AddSingleton<IDatabaseConnectionFactory, DatabaseConnectionFactory>();

var entraOptions = builder.Configuration.GetSection(EntraOptions.SectionName).Get<EntraOptions>() ?? new EntraOptions();
builder.Services.AddSingleton(entraOptions);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IApplicationActorContext,HttpApplicationActorContext>();
builder.Services.AddSingleton<SqlServerAppUserRepository>();
builder.Services.AddSingleton<SqlServerCaseAssignmentRepository>();
builder.Services.AddSingleton<CaseAccessService>();
if (entraOptions.Enabled)
{
    var tenantId = builder.Configuration["AzureAd:TenantId"];
    var apiClientId = builder.Configuration["AzureAd:ClientId"];
    if (!Guid.TryParse(tenantId, out _) || !Guid.TryParse(apiClientId, out _) || !Guid.TryParse(entraOptions.SpaClientId, out _))
        throw new InvalidOperationException("Entra authentication requires valid AzureAd:TenantId, AzureAd:ClientId, and Authentication:Entra:SpaClientId GUIDs.");
    if (string.IsNullOrWhiteSpace(migrationTarget.ConnectionString))
        throw new InvalidOperationException("Entra authentication requires ConnectionStrings:CasePlannerSqlServer so authenticated users can be provisioned.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("CasePlannerUser", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context =>
                EntraClaims.HasScope(context.User, entraOptions.ApiScope)
                && (string.IsNullOrWhiteSpace(entraOptions.RequiredAppRole)
                    || context.User.IsInRole(entraOptions.RequiredAppRole)
                    || context.User.FindAll("roles").Any(c => c.Value.Equals(entraOptions.RequiredAppRole, StringComparison.OrdinalIgnoreCase)))
                && (!entraOptions.AdministratorPilotOnly || CaseAccessEvaluator.IsAdministrator(context.User, entraOptions)));
        });
    });
}

builder.Services.AddSingleton<PathService>();
var documentStorageOptions=builder.Configuration.GetSection(DocumentStorageOptions.SectionName).Get<DocumentStorageOptions>()??new DocumentStorageOptions();
builder.Services.AddSingleton(documentStorageOptions);
builder.Services.AddSingleton<IDocumentStorage,FileSystemDocumentStorage>();
builder.Services.AddSingleton<ITemplateFileStorage,FileSystemTemplateStorage>();
builder.Services.AddSingleton<CasePlannerRepository>();
builder.Services.AddSingleton<SqliteCaseChildLookupStore>();
builder.Services.AddSingleton<SqlServerCaseChildLookupStore>();
builder.Services.AddSingleton<ICaseChildLookupStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerCaseChildLookupStore>()
        : services.GetRequiredService<SqliteCaseChildLookupStore>());
builder.Services.AddSingleton<ICaseNotesExportService,ProviderNeutralCaseNotesExportService>();
builder.Services.AddSingleton<SqliteCaseImportService>();
builder.Services.AddSingleton<SqlServerCaseImportService>();
builder.Services.AddSingleton<SqlServerCaseImportAdapter>();
builder.Services.AddSingleton<ICaseImportService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerCaseImportAdapter>()
        : services.GetRequiredService<SqliteCaseImportService>());
builder.Services.AddSingleton<FileReferenceLibraryStore>();
builder.Services.AddSingleton<SqlServerReferenceLibraryStore>();
builder.Services.AddSingleton<ReferenceLibraryReconciliationService>();
builder.Services.AddSingleton<IReferenceLibraryStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer, StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerReferenceLibraryStore>()
        : services.GetRequiredService<FileReferenceLibraryStore>());
builder.Services.AddSingleton<SqliteIssueTagStore>();
builder.Services.AddSingleton<SqlServerIssueTagStore>();
builder.Services.AddSingleton<IIssueTagStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerIssueTagStore>()
        : services.GetRequiredService<SqliteIssueTagStore>());
builder.Services.AddSingleton<SqliteDocumentPlatformService>();
builder.Services.AddSingleton<SqlServerDocumentPlatformService>();
builder.Services.AddSingleton<IDocumentPlatformService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerDocumentPlatformService>()
        : services.GetRequiredService<SqliteDocumentPlatformService>());
builder.Services.AddSingleton<SqliteServiceLogStore>();
builder.Services.AddSingleton<SqlServerServiceLogStore>();
builder.Services.AddSingleton<IServiceLogStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerServiceLogStore>()
        : services.GetRequiredService<SqliteServiceLogStore>());
builder.Services.AddSingleton<SqliteCaseCatalogReader>();
builder.Services.AddSingleton<SqlServerCaseCatalogReader>();
builder.Services.AddSingleton<ICaseCatalogReader>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerCaseCatalogReader>()
        : services.GetRequiredService<SqliteCaseCatalogReader>());
builder.Services.AddSingleton<ICaseCatalogStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerCaseCatalogReader>()
        : services.GetRequiredService<SqliteCaseCatalogReader>());
builder.Services.AddSingleton<CaseCatalogReconciliationService>();
builder.Services.AddSingleton<SqliteCaseQuickActionService>();
builder.Services.AddSingleton<SqlServerCaseQuickActionService>();
builder.Services.AddSingleton<ICaseQuickActionService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerCaseQuickActionService>()
        : services.GetRequiredService<SqliteCaseQuickActionService>());
builder.Services.AddSingleton<SqliteDeadlineStore>();
builder.Services.AddSingleton<SqlServerDeadlineStore>();
builder.Services.AddSingleton<IDeadlineStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerDeadlineStore>()
        : services.GetRequiredService<SqliteDeadlineStore>());
builder.Services.AddSingleton<SqliteChecklistStore>();
builder.Services.AddSingleton<SqlServerChecklistStore>();
builder.Services.AddSingleton<IChecklistStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerChecklistStore>()
        : services.GetRequiredService<SqliteChecklistStore>());
builder.Services.AddSingleton<WorkItemReconciliationService>();
builder.Services.AddSingleton<SqliteDiscoveryTrackingStore>();
builder.Services.AddSingleton<SqlServerDiscoveryTrackingStore>();
builder.Services.AddSingleton<IDiscoveryTrackingStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerDiscoveryTrackingStore>()
        : services.GetRequiredService<SqliteDiscoveryTrackingStore>());
builder.Services.AddSingleton<DiscoveryReconciliationService>();
builder.Services.AddSingleton<SqliteCaseNoteStore>();
builder.Services.AddSingleton<SqlServerCaseNoteStore>();
builder.Services.AddSingleton<ICaseNoteStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerCaseNoteStore>()
        : services.GetRequiredService<SqliteCaseNoteStore>());
builder.Services.AddSingleton<SqliteHearingStore>();
builder.Services.AddSingleton<SqlServerHearingStore>();
builder.Services.AddSingleton<IHearingStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerHearingStore>()
        : services.GetRequiredService<SqliteHearingStore>());
builder.Services.AddSingleton<CaseWorkspaceReconciliationService>();
builder.Services.AddSingleton<SqliteWitnessStore>();
builder.Services.AddSingleton<SqlServerWitnessStore>();
builder.Services.AddSingleton<IWitnessStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerWitnessStore>()
        : services.GetRequiredService<SqliteWitnessStore>());
builder.Services.AddSingleton<SqliteOpposingAttorneyStore>();
builder.Services.AddSingleton<SqlServerOpposingAttorneyStore>();
builder.Services.AddSingleton<IOpposingAttorneyStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerOpposingAttorneyStore>()
        : services.GetRequiredService<SqliteOpposingAttorneyStore>());
builder.Services.AddSingleton<SqliteExhibitStore>();
builder.Services.AddSingleton<SqlServerExhibitStore>();
builder.Services.AddSingleton<IExhibitStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerExhibitStore>()
        : services.GetRequiredService<SqliteExhibitStore>());
builder.Services.AddSingleton<SqliteTrialMotionStore>();
builder.Services.AddSingleton<SqlServerTrialMotionStore>();
builder.Services.AddSingleton<ITrialMotionStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerTrialMotionStore>()
        : services.GetRequiredService<SqliteTrialMotionStore>());
builder.Services.AddSingleton<LitigationReconciliationService>();
builder.Services.AddSingleton<SqliteValuationPositionStore>();
builder.Services.AddSingleton<SqlServerValuationPositionStore>();
builder.Services.AddSingleton<IValuationPositionStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerValuationPositionStore>()
        : services.GetRequiredService<SqliteValuationPositionStore>());
builder.Services.AddSingleton<SqliteComparableSaleStore>();
builder.Services.AddSingleton<SqlServerComparableSaleStore>();
builder.Services.AddSingleton<IComparableSaleStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerComparableSaleStore>()
        : services.GetRequiredService<SqliteComparableSaleStore>());
builder.Services.AddSingleton<SqlitePublicationEntryStore>();
builder.Services.AddSingleton<SqlServerPublicationEntryStore>();
builder.Services.AddSingleton<IPublicationEntryStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerPublicationEntryStore>()
        : services.GetRequiredService<SqlitePublicationEntryStore>());
builder.Services.AddSingleton<SqlitePublicationSummaryStore>();
builder.Services.AddSingleton<SqlServerPublicationSummaryStore>();
builder.Services.AddSingleton<IPublicationSummaryStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerPublicationSummaryStore>()
        : services.GetRequiredService<SqlitePublicationSummaryStore>());
builder.Services.AddSingleton<PublicationSummaryReconciliationService>();
builder.Services.AddSingleton<ValuationPublicationReconciliationService>();
builder.Services.AddSingleton<SqlServerActivityStore>();
builder.Services.AddSingleton<SqliteActivityStore>();
builder.Services.AddSingleton<SqlServerActivityService>();
builder.Services.AddSingleton<IActivityStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerActivityService>()
        : services.GetRequiredService<SqliteActivityStore>());
builder.Services.AddSingleton<SqliteDiscoveryPostureStore>();
builder.Services.AddSingleton<SqlServerDiscoveryPostureStore>();
builder.Services.AddSingleton<IDiscoveryPostureStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerDiscoveryPostureStore>()
        : services.GetRequiredService<SqliteDiscoveryPostureStore>());
builder.Services.AddSingleton<SqlitePipelineHandoffStore>();
builder.Services.AddSingleton<SqlServerPipelineHandoffStore>();
builder.Services.AddSingleton<IPipelineHandoffStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerPipelineHandoffStore>()
        : services.GetRequiredService<SqlitePipelineHandoffStore>());
builder.Services.AddSingleton<SqlServerDocumentExportStore>();
builder.Services.AddSingleton<SqlServerDocumentPilotService>();
builder.Services.AddSingleton<SqliteGeneratedDocumentService>();
builder.Services.AddSingleton<SqlServerGeneratedDocumentService>();
builder.Services.AddSingleton<IGeneratedDocumentService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerGeneratedDocumentService>()
        : services.GetRequiredService<SqliteGeneratedDocumentService>());
builder.Services.AddSingleton<SqliteBinaryGeneratedDocumentService>();
builder.Services.AddSingleton<SqlServerBinaryGeneratedDocumentService>();
builder.Services.AddSingleton<IBinaryGeneratedDocumentService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer, StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerBinaryGeneratedDocumentService>()
        : services.GetRequiredService<SqliteBinaryGeneratedDocumentService>());
builder.Services.AddSingleton<ActivityDocumentReconciliationService>();
builder.Services.AddSingleton<SqliteOperationalWorkspaceQuery>();
builder.Services.AddSingleton<SqlServerWorkspaceQuery>();
builder.Services.AddSingleton<IOperationalWorkspaceQuery>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerWorkspaceQuery>()
        : services.GetRequiredService<SqliteOperationalWorkspaceQuery>());
builder.Services.AddSingleton<WorkspaceDashboardReconciliationService>();
builder.Services.AddSingleton<AttorneyDashboardReconciliationService>();
builder.Services.AddSingleton<SqlServerRiskAnalysisStore>();
builder.Services.AddSingleton<SqlServerRiskOfferStore>();
builder.Services.AddSingleton<SqliteRiskAnalysisService>();
builder.Services.AddSingleton<SqlServerRiskAnalysisService>();
builder.Services.AddSingleton<IRiskAnalysisService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerRiskAnalysisService>()
        : services.GetRequiredService<SqliteRiskAnalysisService>());
builder.Services.AddSingleton<RiskAnalysisReconciliationService>();
builder.Services.AddSingleton<SqlServerWorkTemplateStore>();
builder.Services.AddSingleton<SqliteWorkTemplateAdministration>();
builder.Services.AddSingleton<SqlServerWorkTemplateAdministration>();
builder.Services.AddSingleton<IWorkTemplateAdministration>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerWorkTemplateAdministration>()
        : services.GetRequiredService<SqliteWorkTemplateAdministration>());
builder.Services.AddSingleton<SqliteWorkflowGenerationService>();
builder.Services.AddSingleton<SqlServerWorkflowGenerationService>();
builder.Services.AddSingleton<IWorkflowGenerationService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerWorkflowGenerationService>()
        : services.GetRequiredService<SqliteWorkflowGenerationService>());
builder.Services.AddSingleton<WorkTemplateReconciliationService>();
builder.Services.AddSingleton<WorkflowGenerationReconciliationService>();
builder.Services.AddSingleton<IssueGenerationReconciliationService>();
builder.Services.AddSingleton<SqlServerOrganizationDefaultsStore>();
builder.Services.AddSingleton<SqliteOrganizationDefaultsStore>();
builder.Services.AddSingleton<SqlServerOrganizationDefaultsAdministration>();
builder.Services.AddSingleton<IOrganizationDefaultsStore>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerOrganizationDefaultsAdministration>()
        : services.GetRequiredService<SqliteOrganizationDefaultsStore>());
builder.Services.AddSingleton<OrganizationDefaultsReconciliationService>();
builder.Services.AddSingleton<SqliteDocumentCompositionService>();
builder.Services.AddSingleton<SqlServerDocumentCompositionService>();
builder.Services.AddSingleton<IDocumentCompositionService>(services =>
    activeProvider.Equals(DatabaseProviders.SqlServer,StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SqlServerDocumentCompositionService>()
        : services.GetRequiredService<SqliteDocumentCompositionService>());
builder.Services.AddSingleton<SqlServerCaseImportService>();
builder.Services.AddSingleton<CutoverReadinessService>();

var app = builder.Build();

var paths = app.Services.GetRequiredService<PathService>();
var repo = app.Services.GetRequiredService<CasePlannerRepository>();
var shutdownToken = Guid.NewGuid().ToString("N");
var hostLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
await repo.InitializeAsync();
await repo.LogAsync($"Web app startup complete. localUrl={paths.Config.LocalUrl}; releaseLocal={paths.Config.IsReleaseLocal}; root={paths.Config.RootPath}");

// The Release publish runs windowless (no console), so this is the only place unhandled
// request exceptions get recorded - without it a crash would be invisible instead of just quiet.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        await repo.LogAsync($"UNHANDLED EXCEPTION on {context.Request.Method} {context.Request.Path}: {ex}");
        throw;
    }
});

var clientDist = paths.Config.ClientDistPath;
if (Directory.Exists(clientDist))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(clientDist)
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(clientDist)
    });
}

if (entraOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path.Equals("/api/auth/config"))
        {
            await next();
            return;
        }

        var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(context.User, null, "CasePlannerUser");
        if (result.Succeeded)
        {
            await next();
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
            await context.ForbidAsync(JwtBearerDefaults.AuthenticationScheme);
        else
            await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
    });
    app.UseMiddleware<EntraUserProvisioningMiddleware>();
    app.Use(async (context, next) =>
    {
        if (entraOptions.AdministratorPilotOnly
            || !context.Request.Path.StartsWithSegments("/api")
            || CaseAccessEvaluator.IsAdministrator(context.User, entraOptions)
            || context.Request.Path.Equals("/api/auth/config")
            || context.Request.Path.Equals("/api/auth/me"))
        {
            await next();
            return;
        }

        // Ordinary-user mode is closed by default. Case-scoped routes are checked centrally;
        // explicitly filtered global/model routes carry AssignmentAwareEndpointMetadata.
        if (context.Request.Path.StartsWithSegments("/api/cases")
            && context.Request.RouteValues.TryGetValue("id", out var routeId)
            && long.TryParse(Convert.ToString(routeId), out var caseId))
        {
            var access = context.RequestServices.GetRequiredService<CaseAccessService>();
            var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            var deletingCase = HttpMethods.IsDelete(context.Request.Method)
                && context.Request.Path.Equals($"/api/cases/{caseId}");
            if (!deletingCase && (isRead ? await access.CanReadAsync(caseId, context.RequestAborted) : await access.CanWriteAsync(caseId, context.RequestAborted)))
            {
                await next();
                return;
            }
        }
        else if (context.GetEndpoint()?.Metadata.GetMetadata<AssignmentAwareEndpointMetadata>() is not null)
        {
            await next();
            return;
        }

        await context.ForbidAsync(JwtBearerDefaults.AuthenticationScheme);
    });
}

var apiClientIdForScope = builder.Configuration["AzureAd:ClientId"] ?? "";
var publicApiScope = entraOptions.ApiScope.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
    ? entraOptions.ApiScope
    : $"api://{apiClientIdForScope}/{entraOptions.ApiScope}";
app.MapGet("/api/auth/config", () => Results.Ok(new EntraPublicConfiguration(
    entraOptions.Enabled,
    entraOptions.Enabled ? $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}" : "",
    entraOptions.Enabled ? entraOptions.SpaClientId : "",
    entraOptions.Enabled ? publicApiScope : "")));
app.MapGet("/api/auth/me", (HttpContext context) =>
    context.Items.TryGetValue(EntraUserProvisioningMiddleware.ProfileItemKey, out var profile) && profile is AuthenticatedUserProfile authenticated
        ? Results.Ok(new { authenticated.Id, authenticated.TenantId, authenticated.ObjectId, authenticated.DisplayName, authenticated.Email, authenticated.Roles, IsAdmin = CaseAccessEvaluator.IsAdministrator(context.User, entraOptions) })
        : Results.Unauthorized()).WithMetadata(new AssignmentAwareEndpointMetadata());
// Read-only: any signed-in user can see who's on staff / who's assigned to a case (it's a
// staff directory, not sensitive data). Only mutation (below) is admin-gated.
app.MapGet("/api/admin/users", async (HttpContext context, SqlServerCaseAssignmentRepository assignments, CancellationToken token) =>
{
    if (!entraOptions.Enabled) return Results.NotFound();
    return Results.Ok(await assignments.GetUsersAsync(token));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/admin/case-assignments", async (long? caseId, Guid? userId, HttpContext context, SqlServerCaseAssignmentRepository assignments, CancellationToken token) =>
{
    if (!entraOptions.Enabled) return Results.NotFound();
    return Results.Ok(await assignments.GetAssignmentsAsync(caseId, userId, token));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/admin/case-assignments", async (SaveCaseAssignmentRequest request, HttpContext context, SqlServerCaseAssignmentRepository assignments, CancellationToken token) =>
{
    if (!entraOptions.Enabled) return Results.NotFound();
    if (!CaseAccessEvaluator.IsAdministrator(context.User, entraOptions)) return Results.Forbid();
    if (context.Items[EntraUserProvisioningMiddleware.ProfileItemKey] is not AuthenticatedUserProfile actor) return Results.Unauthorized();
    try { return Results.Ok(await assignments.SaveAssignmentAsync(request, actor.Id, token)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/admin/case-assignments/{caseId:long}/{userId:guid}", async (long caseId, Guid userId, HttpContext context, SqlServerCaseAssignmentRepository assignments, CancellationToken token) =>
{
    if (!entraOptions.Enabled) return Results.NotFound();
    if (!CaseAccessEvaluator.IsAdministrator(context.User, entraOptions)) return Results.Forbid();
    if (context.Items[EntraUserProvisioningMiddleware.ProfileItemKey] is not AuthenticatedUserProfile actor) return Results.Unauthorized();
    return await assignments.RevokeAssignmentAsync(caseId, userId, actor.Id, token) ? Results.NoContent() : Results.NotFound();
});
app.MapPut("/api/admin/users/{userId:guid}/active", async (Guid userId, SetUserActiveRequest request, HttpContext context, SqlServerCaseAssignmentRepository assignments, CancellationToken token) =>
{
    if (!entraOptions.Enabled) return Results.NotFound();
    if (!CaseAccessEvaluator.IsAdministrator(context.User, entraOptions)) return Results.Forbid();
    if (context.Items[EntraUserProvisioningMiddleware.ProfileItemKey] is not AuthenticatedUserProfile actor) return Results.Unauthorized();
    try { return await assignments.SetUserActiveAsync(userId, request.IsActive, actor.Id, token) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/dashboard",async(IOperationalWorkspaceQuery workspace,CaseAccessService access,CancellationToken token)=>
    Results.Ok(await workspace.GetDashboardAsync(await access.GetVisibleCaseIdsAsync(token),token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/dashboard/upcoming-work",async(string? type,string? urgency,int? limit,IOperationalWorkspaceQuery workspace,CaseAccessService access,CancellationToken token)=>
    Results.Ok(await workspace.GetUpcomingWorkAsync(type??"all",urgency??"All Open",limit??5,await access.GetVisibleCaseIdsAsync(token),token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/reports/export.xlsx", (ReportExcelRequest request) =>
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.Worksheets.Add("Report");
    sheet.Cell(1, 1).Value = request.Title;
    sheet.Cell(2, 1).Value = $"Generated: {request.GeneratedAt}";
    sheet.Cell(3, 1).Value = $"Filters: {string.Join("; ", request.Filters.Select(item => $"{item.Key}={item.Value}"))}";
    for (var column = 0; column < request.Columns.Count; column++)
    {
        var cell = sheet.Cell(5, column + 1);
        cell.Value = request.Columns[column].Label;
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.LightGray;
    }
    for (var row = 0; row < request.Rows.Count; row++)
    {
        for (var column = 0; column < request.Columns.Count; column++)
            sheet.Cell(row + 6, column + 1).Value = request.Rows[row].GetValueOrDefault(request.Columns[column].Key) ?? "";
    }
    sheet.SheetView.FreezeRows(5);
    if (request.Columns.Count > 0 && request.Rows.Count > 0)
    {
        var range = sheet.Range(5, 1, request.Rows.Count + 5, request.Columns.Count);
        range.SetAutoFilter();
        range.Rows().Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        for (var row = 6; row <= request.Rows.Count + 5; row += 2)
            range.Row(row).Style.Fill.BackgroundColor = XLColor.AliceBlue;
    }
    sheet.Columns().AdjustToContents();
    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    var fileName = string.IsNullOrWhiteSpace(request.FileName) ? "Case_Report.xlsx" : request.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? request.FileName : request.FileName + ".xlsx";
    return Results.File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/dashboard/attorney", async (string? matterType, string? project, string? county, string? priority,
    string? currentHolder, string? stage, bool? trialTrack, string? momentumStatus, string? search,IOperationalWorkspaceQuery workspace,CaseAccessService access,CancellationToken token) =>
    Results.Ok(await workspace.GetAttorneyDashboardAsync(new AttorneyDashboardFilters
    {
        MatterType = matterType,
        Project = project,
        County = county,
        Priority = priority,
        CurrentHolder = currentHolder,
        Stage = stage,
        TrialTrack = trialTrack,
        MomentumStatus = momentumStatus,
        Search = search,
    },await access.GetVisibleCaseIdsAsync(token),token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/discovery-posture", async (long id,IDiscoveryPostureStore posture,CancellationToken token) =>
    Results.Ok(await posture.GetAsync(id,token)));
app.MapPost("/api/cases/{id:long}/discovery-posture", async (long id, DiscoveryPosture model,IDiscoveryPostureStore posture,CancellationToken token) =>
{
    model.CaseId = id;
    try{return Results.Ok(await posture.SaveAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/cases/{id:long}/pipeline-handoffs", async (long id,IPipelineHandoffStore handoffs,CancellationToken token) =>
    Results.Ok(await handoffs.GetAsync(id,token)));
app.MapPost("/api/cases/{id:long}/pipeline-handoff", async (long id, PipelineHandoffRequest request,IPipelineHandoffStore handoffs,CancellationToken token) =>
{
    try{return Results.Ok(await handoffs.SaveAsync(id,request,token));}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/cases/{id:long}/activity", async (long id,IActivityStore activity,CancellationToken token) =>
    Results.Ok(await activity.GetAsync(id,token)));
app.MapPost("/api/cases/{id:long}/activity", async (long id, RecordActivityRequest request,IActivityStore activity,CancellationToken token) =>
    Results.Ok(await activity.RecordAsync(id,request,token)));
app.MapPut("/api/activity/{id:long}", async (long id,UpdateActivityRequest request,IActivityStore activity,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await activity.GetCaseIdAsync(id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    try
    {
        return Results.Ok(await activity.UpdateAsync(id,request,token));
    }
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
app.MapPost("/api/cases/{id:long}/next-action", async (long id, SetNextActionRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.SetNextActionAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/cases/{id:long}/waiting", async (long id, SetWaitingRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.SetWaitingAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/cases/{id:long}/waiting", async (long id,string? rowVersion,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.ClearWaitingAsync(id,rowVersion,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/cases/{id:long}/defer", async (long id, DeferActionRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.DeferAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
}).WithMetadata(new AssignmentAwareEndpointMetadata());

bool IsLoopback(HttpContext context) => context.Connection.RemoteIpAddress is null || IPAddress.IsLoopback(context.Connection.RemoteIpAddress);

app.MapGet("/api/app/shutdown-token", (HttpContext context) =>
    IsLoopback(context) ? Results.Ok(new { token = shutdownToken }) : Results.StatusCode(StatusCodes.Status403Forbidden));
app.MapPost("/api/app/shutdown", async (HttpContext context) =>
{
    if (!IsLoopback(context) || !context.Request.Headers.TryGetValue("X-CasePlanner-Shutdown-Token", out var supplied) || supplied != shutdownToken)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    await repo.LogAsync("Graceful shutdown requested by the running application.");
    _ = Task.Run(async () =>
    {
        // Let the shutdown response finish and give any in-flight local request a brief window
        // to complete its transaction/export before the host begins stopping.
        await Task.Delay(250);
        hostLifetime.StopApplication();
    });
    return Results.Ok(new { shuttingDown = true });
});
app.MapDelete("/api/cases/{id:long}/defer", async (long id,string? rowVersion,string? reason,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.ClearDefermentAsync(id,rowVersion,reason,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/cases/bulk-defer", async (BulkDeferActionRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersions=await actions.BulkDeferAsync(request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/cases/{id:long}/holder", async (long id, SetHolderRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.SetHolderAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/cases/{id:long}/priority", async (long id, SetPriorityRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.SetPriorityAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/cases/{id:long}/trial-track", async (long id, SetTrialTrackRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.SetTrialTrackAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/cases/{id:long}/short-note", async (long id, ShortNoteRequest request,ICaseQuickActionService actions,CancellationToken token) =>
{
    try{return Results.Ok(new{rowVersion=await actions.SetShortNoteAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/cases", async (string? search, string? status, string? county, string? stage, bool? includeClosed, string? track, string? caseStatus, string? dateOpenedFrom, string? dateOpenedTo, string? dateClosedFrom, string? dateClosedTo, ICaseCatalogReader cases, CaseAccessService access, CancellationToken token) =>
{
    var result=await cases.GetCasesAsync(new(search??"",status??"",county??"",stage??"",includeClosed??false,track??"",caseStatus??"",dateOpenedFrom??"",dateOpenedTo??"",dateClosedFrom??"",dateClosedTo??""),token);
    var visible=await access.GetVisibleCaseIdsAsync(token);
    return Results.Ok(visible is null?result:result.Where(c=>visible.Contains(c.Id)));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/case-statuses", () => Results.Ok(new[] { "Pipeline", "Filed / Service Pending", "Active Litigation", "Settlement Pending", "Trial Preparation", "Resolved / Closed", "Triage" })).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/case-status-mapping-review", async (CaseAccessService access,CancellationToken token) =>
{
    var result=(await repo.GetCasesAsync("","","","",true)).Where(c=>c.StatusMappingReview);var visible=await access.GetVisibleCaseIdsAsync(token);return Results.Ok(visible is null?result:result.Where(c=>visible.Contains(c.Id)));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}", async (long id,IOperationalWorkspaceQuery workspace,CaseAccessService access,CancellationToken token) =>
{
    var result = await workspace.GetWorkspaceAsync(id,await access.GetVisibleCaseIdsAsync(token),token);
    return result is null ? Results.NotFound() : Results.Ok(result);
});
app.MapPost("/api/cases",async(CaseRecord model,ICaseCatalogStore cases,CaseAccessService access,CancellationToken token)=>
{
    var allowed=model.Id==0?access.CanCreateCases:await access.CanWriteAsync(model.Id,token);
    return allowed?Results.Ok(await cases.SaveCaseAsync(model,token)):Results.Forbid();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/cases/{id:long}", async (long id, ICaseCatalogStore cases, HttpContext context, CancellationToken token) =>
{
    // Admin-only when Entra is enabled; unrestricted when Entra is disabled (local/SQLite default),
    // matching the existing !options.Enabled-means-unrestricted convention used by
    // CaseAccessService.IsUnrestricted/CanCreateCases. CaseAccessEvaluator.IsAdministrator itself
    // has no such carve-out (it only checks role claims), so that check is applied here at the
    // call site rather than inside the shared evaluator.
    if (entraOptions.Enabled && !CaseAccessEvaluator.IsAdministrator(context.User, entraOptions)) return Results.Forbid();
    await cases.DeleteCaseAsync(id, cancellationToken: token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/notes", async (long id, ICaseNoteStore notes) => Results.Ok(await notes.GetAsync(id)));
app.MapPost("/api/case-notes", async (CaseNoteRecord model, ICaseNoteStore notes,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await notes.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/case-notes/{id:long}", async (long id,ICaseNoteStore notes,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("case-note",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await notes.DeleteAsync(id);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/cases/{id:long}/export-notes", async (long id,ICaseNotesExportService exports,CaseAccessService access,CancellationToken token) =>
    await access.CanReadAsync(id,token)?Results.Ok(await exports.ExportAsync(id,token)):Results.Forbid())
    .WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/hearings", async (long id, IHearingStore hearings) => Results.Ok(await hearings.GetAsync(id)));
app.MapPost("/api/hearings", async (HearingRecord model, IHearingStore hearings,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await hearings.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/hearings/{id:long}", async (long id,IHearingStore hearings,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("hearing",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await hearings.DeleteAsync(id);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/deadlines", async (long id, IDeadlineStore deadlines, CancellationToken token) => Results.Ok(await deadlines.GetAsync(id, token)));
app.MapPost("/api/cases/{id:long}/generate-deadlines", async (long id,IWorkflowGenerationService generation,CancellationToken token) =>
{
    var result = await generation.GenerateDeadlinesAsync(id,token);
    return Results.Ok(new { added = result.Added, updated = result.Updated });
});
app.MapPost("/api/deadlines", async (DeadlineItem model, IDeadlineStore deadlines,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await deadlines.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/checklist", async (long id, IChecklistStore checklist, CancellationToken token) => Results.Ok(await checklist.GetAsync(id, token)));
app.MapPost("/api/cases/{id:long}/generate-checklist", async (long id,IWorkflowGenerationService generation,CancellationToken token) =>
    Results.Ok(new { added = await generation.GenerateChecklistAsync(id,token) }));
app.MapPost("/api/checklist", async (ChecklistItemRecord model, IChecklistStore checklist,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await checklist.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/checklist/{id:long}", async (long id,IChecklistStore checklist,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("checklist",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await checklist.DeleteAsync(id, token: token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/deadlines/{id:long}", async (long id,IDeadlineStore deadlines,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("deadline",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await deadlines.DeleteAsync(id, token: token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/discovery", async (long id, IDiscoveryTrackingStore discovery) => Results.Ok(await discovery.GetAsync(id)));
app.MapPost("/api/discovery", async (DiscoveryItemRecord model, IDiscoveryTrackingStore discovery,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await discovery.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/discovery/{id:long}", async (long id,IDiscoveryTrackingStore discovery,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("discovery",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await discovery.DeleteAsync(id, token: token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/valuation-positions", async (long id,IValuationPositionStore positions) => Results.Ok(await positions.GetAsync(id)));
app.MapPost("/api/valuation-positions", async (ValuationPositionRecord model,IValuationPositionStore positions,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await positions.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/comparable-sales", async (long id,IComparableSaleStore sales) => Results.Ok(await sales.GetAsync(id)));
app.MapPost("/api/comparable-sales", async (ComparableSaleRecord model,IComparableSaleStore sales,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await sales.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/comparable-sales/{id:long}", async (long id,IComparableSaleStore sales,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("comparable-sale",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await sales.DeleteAsync(id);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/witnesses", async (long id, IWitnessStore witnesses) => Results.Ok(await witnesses.GetAsync(id)));
app.MapPost("/api/witnesses", async (WitnessRecord model,IWitnessStore witnesses,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await witnesses.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/witnesses/{id:long}", async (long id,IWitnessStore witnesses,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("witness",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await witnesses.DeleteAsync(id);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/opposing-attorneys", async (long id, IOpposingAttorneyStore opposingAttorneys) => Results.Ok(await opposingAttorneys.GetAsync(id)));
app.MapPost("/api/cases/{id:long}/opposing-attorneys", async (long id, OpposingAttorneyRecord model,IOpposingAttorneyStore opposingAttorneys,CaseAccessService access,CancellationToken token) =>
{
    model.CaseId = id;
    return await access.CanWriteAsync(id,token)?Results.Ok(await opposingAttorneys.SaveAsync(model,token)):Results.Forbid();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/opposing-attorneys/{id:long}", async (long id,IOpposingAttorneyStore opposingAttorneys,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("opposing-attorney",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await opposingAttorneys.DeleteAsync(id);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/exhibits", async (long id, IExhibitStore exhibits) => Results.Ok(await exhibits.GetAsync(id)));
app.MapPost("/api/exhibits", async (ExhibitRecord model,IExhibitStore exhibits,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await exhibits.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/exhibits/{id:long}", async (long id,IExhibitStore exhibits,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("exhibit",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await exhibits.DeleteAsync(id);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/trial-motions", async (long id, ITrialMotionStore motions) => Results.Ok(await motions.GetAsync(id)));
app.MapPost("/api/trial-motions", async (TrialMotionRecord model,ITrialMotionStore motions,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await motions.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/trial-motions/{id:long}", async (long id,ITrialMotionStore motions,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("trial-motion",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await motions.DeleteAsync(id);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/publication-service", async (long id,IPublicationEntryStore publications) => Results.Ok(await publications.GetAsync(id)));
app.MapPost("/api/publication-service", async (PublicationEntryRecord model,IPublicationEntryStore publications,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await publications.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/publication-service/{id:long}", async (long id,IPublicationEntryStore publications,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("publication-entry",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await publications.DeleteAsync(id,null,token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/service-log", async (long id,IServiceLogStore serviceLog,CancellationToken token) => Results.Ok(await serviceLog.GetAsync(id,token)));
app.MapPost("/api/service-log", async (ServiceLogEntry model,IServiceLogStore serviceLog,CaseAccessService access,CancellationToken token) =>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await serviceLog.SaveAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/service-log/{id:long}", async (long id,IServiceLogStore serviceLog,ICaseChildLookupStore children,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await children.GetCaseIdAsync("service-log",id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await serviceLog.DeleteAsync(id,token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/publication", async (long id,IPublicationSummaryStore publications,CancellationToken token) =>
    Results.Ok(await publications.GetAsync(id,token) ?? new PublicationRecord { CaseId = id }));
app.MapPut("/api/cases/{id:long}/publication", async (long id, PublicationRecord model,IPublicationSummaryStore publications,CaseAccessService access,CancellationToken token) =>
{
    try
    {
        if(!await access.CanWriteAsync(id,token))return Results.Forbid();
        model.CaseId = id;
        return Results.Ok(await publications.SaveAsync(model,token));
    }
    catch (WorkItemConcurrencyException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/work-queues/service",async(IOperationalWorkspaceQuery workspace,CaseAccessService access,CancellationToken token)=>
    Results.Ok(await workspace.GetServiceQueueAsync(await access.GetVisibleCaseIdsAsync(token),token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/issue-tags", async (long id,IIssueTagStore issueTags,CancellationToken token) =>
    Results.Ok(new
    {
        available = await issueTags.GetCatalogAsync(token),
        assigned = await issueTags.GetCaseTagsAsync(id,token)
    }));
app.MapPost("/api/cases/{id:long}/issue-tags/{tagId:long}", async (long id, long tagId,IIssueTagStore issueTags,CancellationToken token) =>
{
    try
    {
        return Results.Ok(await issueTags.AddAsync(id,tagId,token));
    }
    catch (DuplicateIssueTagException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});
app.MapDelete("/api/case-issue-tags/{id:long}", async (long id,string? rowVersion,IIssueTagStore issueTags,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await issueTags.GetCaseIdAsync(id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await issueTags.RemoveAsync(id,rowVersion,token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/document-exports", async (long id,IGeneratedDocumentService documents,CancellationToken token) => Results.Ok(await documents.GetAsync(id,token)));
app.MapPost("/api/cases/{id:long}/generate/{kind}", async (long id, string kind, IOperationalWorkspaceQuery workspaces, IBinaryGeneratedDocumentService binaryDocuments, CancellationToken token) =>
{
    if (kind is not ("summary" or "memo")) return Results.BadRequest(new { error = "Supported utilities are summary and memo." });
    var workspace = await workspaces.GetWorkspaceAsync(id, null, token);
    if (workspace is null) return Results.NotFound();
    var title = kind == "summary" ? "Case Summary" : "Case Review Memo";
    var text = BasicDocumentComposer.BuildText(workspace, title);
    var record = await binaryDocuments.SaveAsync(id, new SaveGeneratedDocumentRequest
    {
        Kind = kind,
        Title = title,
        Text = text,
        IsFinalized = true
    }, DocumentGenerationEngine.CreateDocxFromText(text), ".docx", token);
    return Results.Ok(record);
});
app.MapPost("/api/document-exports/{id:long}/qa", async (long id,DocumentExportRecord model,IGeneratedDocumentService documents,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await documents.GetCaseIdAsync(id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    return Results.Ok(await documents.SaveQaAsync(id,model,token));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/work-queues/deadlines",async(IDeadlineStore deadlines,CaseAccessService access,CancellationToken token)=>
{
    var items=await deadlines.GetAsync(null,token);var visible=await access.GetVisibleCaseIdsAsync(token);return Results.Ok(visible is null?items:items.Where(x=>visible.Contains(x.CaseId)));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/work-queues/checklist",async(IChecklistStore checklist,CaseAccessService access,CancellationToken token)=>
{
    var items=await checklist.GetAsync(null,token);var visible=await access.GetVisibleCaseIdsAsync(token);return Results.Ok(visible is null?items:items.Where(x=>visible.Contains(x.CaseId)));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/work-queues/discovery",async(IDiscoveryTrackingStore discovery,CaseAccessService access,CancellationToken token)=>
{
    var items=await discovery.GetAsync(null,token);var visible=await access.GetVisibleCaseIdsAsync(token);return Results.Ok(visible is null?items:items.Where(x=>visible.Contains(x.CaseId)));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/work-queues/hearings",async(IHearingStore hearings,CaseAccessService access,CancellationToken token)=>
{
    var items=await hearings.GetAsync(null,token);var visible=await access.GetVisibleCaseIdsAsync(token);return Results.Ok(visible is null?items:items.Where(x=>visible.Contains(x.CaseId)));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/issue-tags",async(IIssueTagStore issueTags,CancellationToken token)=>Results.Ok(await issueTags.GetCatalogAsync(token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/issue-tags", async (IssueTagRecord model,IIssueTagStore issueTags,CancellationToken token) =>
{
    try
    {
        return Results.Ok(await issueTags.CreateAsync(model.Name,model.Description,model.Category,token));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (DuplicateIssueTagException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPut("/api/issue-tags/{id:long}", async (long id,IssueTagRecord model,IIssueTagStore issueTags,CancellationToken token) =>
{
    try
    {
        return Results.Ok(await issueTags.RenameAsync(id,model.Name,model.Description,model.Category,token));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (DuplicateIssueTagException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/issue-tags/{id:long}", async (long id,IIssueTagStore issueTags,CancellationToken token) =>
{
    try
    {
        await issueTags.RetireAsync(id,token);
        return Results.Ok();
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/issue-tags/usage",async(IIssueTagStore issueTags,CancellationToken token)=>Results.Ok(await issueTags.GetUsageAsync(token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/checklist-templates",async(IWorkTemplateAdministration templates,CancellationToken token)=>
    Results.Ok(await templates.GetChecklistAsync(token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/deadline-templates",async(IWorkTemplateAdministration templates,CancellationToken token)=>
    Results.Ok(await templates.GetDeadlinesAsync(token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/deadline-templates", async (DeadlineTemplateRecord model,IWorkTemplateAdministration templates,CancellationToken token) =>
{
    try{return Results.Ok(await templates.SaveDeadlineAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/cases/{id:long}/work-template-candidates", async (long id,IWorkflowGenerationService generation,CancellationToken token) => Results.Ok(await generation.GetCandidatesAsync(id,token)));
app.MapPost("/api/cases/{id:long}/work-template-selections", async (long id, AddWorkTemplatesRequest request,IWorkflowGenerationService generation,CancellationToken token) => Results.Ok(new { added=await generation.AddSelectionsAsync(id,request,token) }));
app.MapPost("/api/checklist-templates", async (ChecklistTemplateRecord model,IWorkTemplateAdministration templates,CancellationToken token) =>
{
    try{return Results.Ok(await templates.SaveChecklistAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/checklist-templates/{id:long}", async (long id,string? rowVersion,IWorkTemplateAdministration templates,CancellationToken token) =>
{
    try{await templates.DeleteChecklistAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/checklist-template-items", async (ChecklistTemplateItemRecord model,IWorkTemplateAdministration templates,CancellationToken token) =>
{
    try{return Results.Ok(await templates.SaveChecklistItemAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/checklist-template-items/{id:long}", async (long id,string? rowVersion,IWorkTemplateAdministration templates,CancellationToken token) =>
{
    try{await templates.DeleteChecklistItemAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/backups", async () => Results.Ok(await repo.GetBackupsAsync()));
app.MapPost("/api/backups", async () => Results.Ok(await repo.CreateBackupNowAsync()));
app.MapPost("/api/backups/restore", async (RestoreBackupRequest request) =>
{
    await repo.RestoreBackupAsync(request.FileName);
    return Results.Ok();
});
app.MapPost("/api/data-management/sample-data/delete", async () =>
{
    try { return Results.Ok(new { deleted = await repo.DeleteSampleDataAsync() }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/data-management/reset", async (DatabaseResetRequest request) =>
{
    try
    {
        await repo.ResetEntireDatabaseAsync(request.Scope, request.Confirmation);
        return Results.Ok(new { reset = true });
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.BadRequest(new { error = $"Database reset could not be completed: {ex.Message}" }); }
});
app.MapPost("/api/import/cases-csv", async (HttpRequest request,ICaseImportService importer,CancellationToken token) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart form upload." });
    }

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "CSV file is required." });
    }

    await using var stream = file.OpenReadStream();
    return Results.Ok(await importer.ImportCsvAsync(stream,token));
});
app.MapPost("/api/import/cases-xlsx", async (HttpRequest request,ICaseImportService importer,CancellationToken token) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart form upload." });
    }

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Excel file (.xlsx or .xlsm) is required." });
    }

    // ClosedXML needs a seekable stream.
    using var buffer = new MemoryStream();
    await file.CopyToAsync(buffer);
    buffer.Position = 0;
    return Results.Ok(await importer.ImportXlsxAsync(buffer,token));
});
app.MapGet("/api/org-defaults",async(IOrganizationDefaultsStore defaults,CancellationToken token)=>
    Results.Ok(await defaults.GetAsync(token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/org-defaults", async (OrgDefaults model,IOrganizationDefaultsStore defaults,CancellationToken token) =>
{
    try{return Results.Ok(await defaults.SaveAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/template-tags",()=>Results.Ok(DocumentGenerationEngine.GetAllTemplateTags())).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/document-exports/{id:long}/download", async (long id,IGeneratedDocumentService exports,CaseAccessService access,IDocumentStorage documents,CancellationToken token) =>
{
    var caseId=await exports.GetCaseIdAsync(id,token);if(caseId is null)return Results.NotFound();if(!await access.CanReadAsync(caseId.Value,token))return Results.Forbid();
    var record = await exports.GetByIdAsync(id,token);
    if (record is null)
    {
        return Results.NotFound();
    }

    var contentType = Path.GetExtension(record.OutputPath).ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "text/plain"
    };
    var stream=await documents.OpenReadAsync(record.OutputPath,token);
    return stream is null?Results.NotFound():Results.File(stream,contentType,Path.GetFileName(record.OutputPath));
});
app.MapDelete("/api/document-exports/{id:long}", async (long id,IGeneratedDocumentService exports,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await exports.GetCaseIdAsync(id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    return await exports.DeleteAsync(id,token)?Results.Ok():Results.NotFound();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/document-platform/templates/{key}/checklist", async (long id,string key,IDocumentPlatformService platform,CaseAccessService access,CancellationToken token) =>
{
    if(!await access.CanReadAsync(id,token))return Results.Forbid();
    var checklist=await platform.GetChecklistAsync(id,key,token);
    return checklist is null?Results.NotFound():Results.Ok(checklist);
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/cases/{id:long}/document-platform/templates/{key}/generate", async (long id,string key,DocumentGenerationRequest request,IDocumentPlatformService platform,CaseAccessService access,CancellationToken token) =>
{
    if(!await access.CanWriteAsync(id,token))return Results.Forbid();
    try
    {
        return Results.Ok(await platform.GenerateAsync(id,key,request,token));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/document-platform/generations", async (long id,IDocumentPlatformService platform,CaseAccessService access,CancellationToken token) =>
{
    if(!await access.CanReadAsync(id,token))return Results.Forbid();
    return Results.Ok(await platform.GetGenerationsForCaseAsync(id,token));
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/document-platform-generations/{id:long}/download", async (long id,IDocumentPlatformService platform,CaseAccessService access,IDocumentStorage documents,CancellationToken token) =>
{
    var record=await platform.GetGenerationByIdAsync(id,token);
    if(record is null)return Results.NotFound();
    if(!await access.CanReadAsync(record.CaseId,token))return Results.Forbid();
    var stream=await documents.OpenReadAsync(record.OutputPath,token);
    return stream is null?Results.NotFound():Results.File(stream,"application/vnd.openxmlformats-officedocument.wordprocessingml.document",Path.GetFileName(record.OutputPath));
});
app.MapDelete("/api/document-platform-generations/{id:long}", async (long id,IDocumentPlatformService platform,CaseAccessService access,CancellationToken token) =>
{
    var record=await platform.GetGenerationByIdAsync(id,token);
    if(record is null)return Results.NotFound();
    if(!await access.CanWriteAsync(record.CaseId,token))return Results.Forbid();
    return await platform.DeleteGenerationAsync(id,token)?Results.Ok():Results.NotFound();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/document-platform/templates", async (IDocumentPlatformService platform,CancellationToken token) =>
    Results.Ok(await platform.GetAllTemplatesAsync(token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/document-platform/templates/upload", async (HttpRequest request,IDocumentPlatformService platform,CancellationToken token) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart form upload." });
    }

    var form = await request.ReadFormAsync(token);
    var file = form.Files["file"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Template file is required." });
    }

    var templateKey = form["templateKey"].FirstOrDefault() ?? "";
    var title = form["title"].FirstOrDefault() ?? "";
    var description = form["description"].FirstOrDefault();
    var category = form["category"].FirstOrDefault() ?? "Other";

    try
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, token);
        return Results.Ok(await platform.UploadTemplateAsync(templateKey, title, description, category, stream.ToArray(), token));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPut("/api/document-platform/templates/{key}/configuration", async (string key,DocumentTemplateConfigurationRequest request,IDocumentPlatformService platform,CancellationToken token) =>
{
    try
    {
        return Results.Ok(await platform.SaveConfigurationAsync(key, request, token));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPost("/api/document-platform/templates/{key}/versions/{version:int}/activate", async (string key,int version,IDocumentPlatformService platform,CancellationToken token) =>
{
    try
    {
        return Results.Ok(await platform.ActivateVersionAsync(key, version, token));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/document-platform/templates/{key}", async (string key,IDocumentPlatformService platform,CancellationToken token) =>
{
    try
    {
        await platform.DeleteTemplateAsync(key, token);
        return Results.Ok();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/document-platform/sample-template", () =>
{
    var bytes = DocumentGenerationEngine.BuildSampleMergeFieldTemplateDocx();
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Merge Field Reference.docx");
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/reference-library",async(IReferenceLibraryStore library,CancellationToken token)=>Results.Ok(await library.GetAsync(token))).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapPut("/api/reference-library/{key}",async(string key,ReferenceDocumentUpdate model,IReferenceLibraryStore library,CancellationToken token)=>
{
    try{model.Key=key;return Results.Ok(await library.SaveAsync(model,token));}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/reference-library/{key}",async(string key,IReferenceLibraryStore library,CancellationToken token)=>
{
    try{await library.DeleteAsync(key,token);return Results.Ok();}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/cases/{id:long}/risk-analysis", async (long id,IRiskAnalysisService risk,CancellationToken token) => Results.Ok(await risk.GetAsync(id,token)));
app.MapGet("/api/cases/{id:long}/risk-analysis/history", async (long id,IRiskAnalysisService risk,CancellationToken token) => Results.Ok(await risk.GetHistoryAsync(id,token)));
app.MapGet("/api/cases/{id:long}/risk-analysis/history/{historyId:long}", async (long id, long historyId,IRiskAnalysisService risk,CancellationToken token) =>
{
    try { return Results.Ok(await risk.GetHistorySnapshotAsync(id,historyId,token)); }
    catch (InvalidOperationException ex) { return Results.NotFound(new { error = ex.Message }); }
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/document-exports/{id:long}/content", async (long id,IGeneratedDocumentService documents,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await documents.GetCaseIdAsync(id,token);if(caseId is null)return Results.NotFound();if(!await access.CanReadAsync(caseId.Value,token))return Results.Forbid();
    var record = await documents.GetByIdAsync(id,token);
    if (record is null) return Results.NotFound();
    var content = await documents.GetContentAsync(id,token);
    return Results.Ok(new { id = record.Id, title = record.DocumentTitle, content, isDraft = record.IsDraft, isFinalized = record.IsFinalized, baseTemplateVersion = record.BaseTemplateVersion, issueTagVersions = record.IssueTagVersions });
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/cases/{id:long}/risk-analysis/history/{historyId:long}", async (long id, long historyId,IRiskAnalysisService risk,CancellationToken token) =>
{
    try { await risk.DeleteHistoryAsync(id,historyId,token); return Results.Ok(); }
    catch (InvalidOperationException ex) { return Results.NotFound(new { error = ex.Message }); }
});
app.MapPost("/api/cases/{id:long}/risk-analysis/preview", async (long id, RiskAnalysisInput input,IRiskAnalysisService risk,CancellationToken token) =>
{
    try
    {
        return Results.Ok(await risk.PreviewAsync(id,input,token));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
app.MapPost("/api/cases/{id:long}/risk-analysis", async (long id, RiskAnalysisInput input,IRiskAnalysisService risk,CancellationToken token) =>
{
    return Results.Ok(await risk.SaveAsync(id,input,token));
});
app.MapDelete("/api/cases/{id:long}/risk-analysis", async (long id,IRiskAnalysisService risk,CancellationToken token) =>
{
    await risk.DeleteAsync(id,token);
    return Results.Ok();
});
app.MapPost("/api/cases/{id:long}/risk-analysis/narrative", async (long id, RiskNarrativeManualInputs manual,IDocumentCompositionService composition,CancellationToken token) =>
    Results.Ok(new { narrative = await composition.GenerateRiskNarrativeAsync(id,manual,token) }));
app.MapGet("/api/cases/{id:long}/risk-analysis/export", async (long id,IOperationalWorkspaceQuery workspaceQuery,IRiskAnalysisService risk,CaseAccessService access,CancellationToken token) =>
{
    var workspace = await workspaceQuery.GetWorkspaceAsync(id,await access.GetVisibleCaseIdsAsync(token),token);
    if (workspace is null)
    {
        return Results.NotFound();
    }

    var analysis = await risk.GetAsync(id,token);
    var offerLog = await risk.GetOffersAsync(id,token);
    var bytes = RiskAnalysisExcelExportService.BuildWorkbook(workspace.Case, analysis, offerLog);
    var fileNameCase = string.IsNullOrWhiteSpace(workspace.Case.CaseNumber) ? id.ToString() : workspace.Case.CaseNumber;
    var safeFileName = string.Concat(fileNameCase.Split(Path.GetInvalidFileNameChars()));
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"RiskAnalysis_{safeFileName}.xlsx");
});
app.MapGet("/api/cases/{id:long}/risk-analysis-offers", async (long id,IRiskAnalysisService risk,CancellationToken token) => Results.Ok(await risk.GetOffersAsync(id,token)));
app.MapPost("/api/risk-analysis-offers",async(RiskAnalysisOfferLogEntry model,IRiskAnalysisService risk,CaseAccessService access,CancellationToken token)=>
    await access.CanWriteAsync(model.CaseId,token)?Results.Ok(await risk.SaveOfferAsync(model,token)):Results.Forbid()).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapDelete("/api/risk-analysis-offers/{id:long}", async (long id,IRiskAnalysisService risk,CaseAccessService access,CancellationToken token) =>
{
    var caseId=await risk.GetOfferCaseIdAsync(id,token);if(caseId is null)return Results.NotFound();if(!await access.CanWriteAsync(caseId.Value,token))return Results.Forbid();
    await risk.DeleteOfferAsync(id,token);
    return Results.Ok();
}).WithMetadata(new AssignmentAwareEndpointMetadata());
app.MapGet("/api/diagnostics", async () => Results.Ok(await repo.GetDiagnosticsAsync()));
app.MapGet("/api/health", async () =>
{
    var diagnostics = await repo.GetDiagnosticsAsync();
    return Results.Ok(new
    {
        status = "ok",
        diagnostics.Version,
        diagnostics.DatabaseProvider,
        diagnostics.DatabasePath,
        diagnostics.WriteSafetyOk,
        diagnostics.CaseCount,
        diagnostics.DeadlineCount,
        diagnostics.ChecklistCount,
        diagnostics.DiscoveryCount
    });
});
app.MapGet("/api/database/migration-target-status", async (IDatabaseConnectionFactory factory, DatabaseOptions options, CancellationToken token) =>
    Results.Ok(await DatabaseProbe.CheckAsync(factory, options.CommandTimeoutSeconds, token)));
app.MapGet("/api/database/document-storage-status",(IDocumentStorage documents)=>Results.Ok(new
{
    documents.Provider,
    documents.RootPath,
    RootExists=Directory.Exists(documents.RootPath),
    IsUnc=documents.RootPath.StartsWith(@"\\",StringComparison.Ordinal)
}));
app.MapGet("/api/database/administration-capabilities",(IConfiguration configuration)=>Results.Ok(
    new DatabaseAdministrationCapabilities(
        configuration["Database:ActiveProvider"]??DatabaseProviders.Sqlite,
        configuration.GetValue("Database:SqlServerPilotWritesEnabled",false),
        false,
        "SQLite: application; SQL Server: IT/DBA",
        activeProvider.Equals(DatabaseProviders.Sqlite,StringComparison.OrdinalIgnoreCase),
        activeProvider.Equals(DatabaseProviders.Sqlite,StringComparison.OrdinalIgnoreCase),
        activeProvider.Equals(DatabaseProviders.Sqlite,StringComparison.OrdinalIgnoreCase),
        "Normal imports write SQLite; SQL Server pilot imports are separately write-gated.",
        [
            "SQL Server backup, restore, retention, and point-in-time recovery must be supplied by IT/DBA.",
            "Application reset and sample-data deletion are not valid central SQL Server administration procedures.",
            "The SQL Excel pilot imports Open and Closed case sheets only; Discovery remains a cutover blocker."
        ])));
app.MapGet("/api/database/cutover-readiness",async(CutoverReadinessService readiness,CancellationToken token)=>
    Results.Ok(await readiness.CheckAsync(token)));
app.MapPost("/api/database/sqlserver-pilot/import/cases-csv",async(HttpRequest request,SqlServerCaseImportService importer,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if(!request.HasFormContentType)return Results.BadRequest(new{error="Expected multipart form upload."});
    var form=await request.ReadFormAsync(token);var file=form.Files["file"];
    if(file is null||file.Length==0)return Results.BadRequest(new{error="CSV file is required."});
    await using var stream=file.OpenReadStream();
    return Results.Ok(await importer.ImportCsvAsync(stream,token));
});
app.MapPost("/api/database/sqlserver-pilot/import/cases-xlsx",async(HttpRequest request,SqlServerCaseImportService importer,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if(!request.HasFormContentType)return Results.BadRequest(new{error="Expected multipart form upload."});
    var form=await request.ReadFormAsync(token);var file=form.Files["file"];
    if(file is null||file.Length==0)return Results.BadRequest(new{error="Excel file (.xlsx or .xlsm) is required."});
    using var buffer=new MemoryStream();await file.CopyToAsync(buffer,token);buffer.Position=0;
    return Results.Ok(await importer.ImportXlsxAsync(buffer,token));
});
app.MapGet("/api/database/sqlserver-pilot/cases", async (string? search, string? status, string? county, string? stage, bool? includeClosed, string? track, string? caseStatus, SqlServerCaseCatalogReader cases, CancellationToken token) =>
    Results.Ok(await cases.GetCasesAsync(new(search ?? "", status ?? "", county ?? "", stage ?? "", includeClosed ?? false, track ?? "", caseStatus ?? ""), token)));
app.MapGet("/api/database/sqlserver-pilot/cases/{caseId:long}/work-template-candidates",async(long caseId,SqlServerWorkflowGenerationService generation,CancellationToken token)=>
    Results.Ok(await generation.GetCandidatesAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/cases/{caseId:long}/generate-deadlines",async(long caseId,SqlServerWorkflowGenerationService generation,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var result=await generation.GenerateDeadlinesAsync(caseId,token);return Results.Ok(new{added=result.Added,updated=result.Updated});
});
app.MapPost("/api/database/sqlserver-pilot/cases/{caseId:long}/generate-checklist",async(long caseId,SqlServerWorkflowGenerationService generation,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(new{added=await generation.GenerateChecklistAsync(caseId,token)});
});
app.MapPost("/api/database/sqlserver-pilot/cases/{caseId:long}/work-template-selections",async(long caseId,AddWorkTemplatesRequest request,SqlServerWorkflowGenerationService generation,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(new{added=await generation.AddSelectionsAsync(caseId,request,token)});
});
app.MapGet("/api/database/reconciliation/workflow-generation",async(WorkflowGenerationReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/issue-tags",async(SqlServerIssueTagStore issueTags,CancellationToken token)=>
    Results.Ok(await issueTags.GetCatalogAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/cases/{caseId:long}/issue-tags",async(long caseId,SqlServerIssueTagStore issueTags,CancellationToken token)=>
    Results.Ok(await issueTags.GetCaseTagsAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/cases/{caseId:long}/issue-tags/{tagId:long}",async(long caseId,long tagId,SqlServerIssueTagStore issueTags,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await issueTags.AddAsync(caseId,tagId,token));}
    catch(DuplicateIssueTagException ex){return Results.Conflict(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/case-issue-tags/{id:long}",async(long id,string? rowVersion,SqlServerIssueTagStore issueTags,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await issueTags.RemoveAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.NotFound(new{error=ex.Message});}
});
app.MapGet("/api/database/reconciliation/issue-generations",async(IssueGenerationReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapPost("/api/database/sqlserver-pilot/cases", async (CaseRecord model, SqlServerCaseCatalogReader cases, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await cases.SaveCaseAsync(model, token)); }
    catch (CaseConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/cases/{id:long}", async (long id, string? rowVersion, SqlServerCaseCatalogReader cases, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await cases.DeleteCaseAsync(id, rowVersion, token); return Results.Ok(); }
    catch (CaseConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/database/sqlserver-pilot/cases/{id:long}/priority",async(
    long id,SetPriorityRequest request,SqlServerCaseQuickActionService actions,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(new{rowVersion=await actions.SetPriorityAsync(id,request,token)});}
    catch(CaseConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/cases/{id:long}/discovery-posture",async(
    long id,SqlServerDiscoveryPostureStore posture,CancellationToken token)=>
    Results.Ok(await posture.GetAsync(id,token)));
app.MapPost("/api/database/sqlserver-pilot/cases/{id:long}/discovery-posture",async(
    long id,DiscoveryPosture model,SqlServerDiscoveryPostureStore posture,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    model.CaseId=id;
    try{return Results.Ok(await posture.SaveAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/cases/{id:long}/pipeline-handoffs",async(
    long id,SqlServerPipelineHandoffStore handoffs,CancellationToken token)=>
    Results.Ok(await handoffs.GetAsync(id,token)));
app.MapGet("/api/database/reconciliation/cases", async (CaseCatalogReconciliationService reconciliation, CancellationToken token) =>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/deadlines", async (long? caseId, SqlServerDeadlineStore deadlines, CancellationToken token) =>
    Results.Ok(await deadlines.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/deadlines", async (DeadlineItem model, SqlServerDeadlineStore deadlines, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await deadlines.SaveAsync(model, token)); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/deadlines/{id:long}", async (long id, string? rowVersion, SqlServerDeadlineStore deadlines, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await deadlines.DeleteAsync(id, rowVersion, token); return Results.Ok(); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/sqlserver-pilot/checklist", async (long? caseId, SqlServerChecklistStore checklist, CancellationToken token) =>
    Results.Ok(await checklist.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/checklist", async (ChecklistItemRecord model, SqlServerChecklistStore checklist, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await checklist.SaveAsync(model, token)); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/checklist/{id:long}", async (long id, string? rowVersion, SqlServerChecklistStore checklist, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await checklist.DeleteAsync(id, rowVersion, token); return Results.Ok(); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/reconciliation/work-items", async (WorkItemReconciliationService reconciliation, CancellationToken token) =>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/discovery", async (long? caseId, SqlServerDiscoveryTrackingStore discovery, CancellationToken token) =>
    Results.Ok(await discovery.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/discovery", async (DiscoveryItemRecord model, SqlServerDiscoveryTrackingStore discovery, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await discovery.SaveAsync(model, token)); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/discovery/{id:long}", async (long id, string? rowVersion, SqlServerDiscoveryTrackingStore discovery, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await discovery.DeleteAsync(id, rowVersion, token); return Results.Ok(); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/reconciliation/discovery", async (DiscoveryReconciliationService reconciliation, CancellationToken token) =>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/case-notes", async (long? caseId, SqlServerCaseNoteStore notes, CancellationToken token) => Results.Ok(await notes.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/case-notes", async (CaseNoteRecord model, SqlServerCaseNoteStore notes, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await notes.SaveAsync(model, token)); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/case-notes/{id:long}", async (long id, string? rowVersion, SqlServerCaseNoteStore notes, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await notes.DeleteAsync(id, rowVersion, token); return Results.Ok(); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/sqlserver-pilot/hearings", async (long? caseId, SqlServerHearingStore hearings, CancellationToken token) => Results.Ok(await hearings.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/hearings", async (HearingRecord model, SqlServerHearingStore hearings, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await hearings.SaveAsync(model, token)); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/hearings/{id:long}", async (long id, string? rowVersion, SqlServerHearingStore hearings, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await hearings.DeleteAsync(id, rowVersion, token); return Results.Ok(); }
    catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/reconciliation/case-workspace", async (CaseWorkspaceReconciliationService reconciliation, CancellationToken token) => Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/witnesses", async (long? caseId, SqlServerWitnessStore store, CancellationToken token) => Results.Ok(await store.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/witnesses", async (WitnessRecord model, SqlServerWitnessStore store, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await store.SaveAsync(model, token)); } catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/witnesses/{id:long}", async (long id, string? rowVersion, SqlServerWitnessStore store, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await store.DeleteAsync(id, rowVersion, token); return Results.Ok(); } catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/sqlserver-pilot/exhibits", async (long? caseId, SqlServerExhibitStore store, CancellationToken token) => Results.Ok(await store.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/exhibits", async (ExhibitRecord model, SqlServerExhibitStore store, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await store.SaveAsync(model, token)); } catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/exhibits/{id:long}", async (long id, string? rowVersion, SqlServerExhibitStore store, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await store.DeleteAsync(id, rowVersion, token); return Results.Ok(); } catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/sqlserver-pilot/trial-motions", async (long? caseId, SqlServerTrialMotionStore store, CancellationToken token) => Results.Ok(await store.GetAsync(caseId, token)));
app.MapPost("/api/database/sqlserver-pilot/trial-motions", async (TrialMotionRecord model, SqlServerTrialMotionStore store, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await store.SaveAsync(model, token)); } catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapDelete("/api/database/sqlserver-pilot/trial-motions/{id:long}", async (long id, string? rowVersion, SqlServerTrialMotionStore store, IConfiguration configuration, CancellationToken token) =>
{
    if (!configuration.GetValue("Database:SqlServerPilotWritesEnabled", false)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try { await store.DeleteAsync(id, rowVersion, token); return Results.Ok(); } catch (WorkItemConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/database/reconciliation/litigation-workspace", async (LitigationReconciliationService reconciliation, CancellationToken token) => Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/valuation-positions", async (long? caseId,SqlServerValuationPositionStore store,CancellationToken token)=>Results.Ok(await store.GetAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/valuation-positions",async(ValuationPositionRecord model,SqlServerValuationPositionStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await store.SaveAsync(model,token));}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/comparable-sales",async(long? caseId,SqlServerComparableSaleStore store,CancellationToken token)=>Results.Ok(await store.GetAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/comparable-sales",async(ComparableSaleRecord model,SqlServerComparableSaleStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await store.SaveAsync(model,token));}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/comparable-sales/{id:long}",async(long id,string? rowVersion,SqlServerComparableSaleStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await store.DeleteAsync(id,rowVersion,token);return Results.Ok();}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/publication-entries",async(long? caseId,SqlServerPublicationEntryStore store,CancellationToken token)=>Results.Ok(await store.GetAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/publication-entries",async(PublicationEntryRecord model,SqlServerPublicationEntryStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await store.SaveAsync(model,token));}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/publication-entries/{id:long}",async(long id,string? rowVersion,SqlServerPublicationEntryStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await store.DeleteAsync(id,rowVersion,token);return Results.Ok();}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/publication-summaries",async(long? caseId,SqlServerPublicationSummaryStore store,CancellationToken token)=>
    Results.Ok(await store.GetAsync(caseId,token)));
app.MapPut("/api/database/sqlserver-pilot/cases/{id:long}/publication",async(long id,PublicationRecord model,SqlServerPublicationSummaryStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{model.CaseId=id;return Results.Ok(await store.SaveAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/reconciliation/publication-summaries",async(PublicationSummaryReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/reconciliation/valuation-publication",async(ValuationPublicationReconciliationService reconciliation,CancellationToken token)=>Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/activity",async(long? caseId,SqlServerActivityStore store,CancellationToken token)=>Results.Ok(await store.GetAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/activity/{caseId:long}",async(long caseId,RecordActivityRequest request,SqlServerActivityStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await store.RecordAsync(caseId,request.ActivityType,request.Notes,request.OccurredAt,token));}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPut("/api/database/sqlserver-pilot/activity/{id:long}",async(long id,string? rowVersion,UpdateActivityRequest request,SqlServerActivityStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await store.UpdateAsync(id,request,rowVersion,token));}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/document-exports",async(long? caseId,SqlServerDocumentExportStore store,CancellationToken token)=>Results.Ok(await store.GetAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/document-exports/{caseId:long}/text",async(long caseId,SaveGeneratedDocumentRequest request,SqlServerDocumentPilotService service,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await service.GenerateTextAsync(caseId,request,token));}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/database/sqlserver-pilot/document-exports/{id:long}/qa",async(long id,DocumentExportRecord model,SqlServerDocumentExportStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await store.SaveQaAsync(id,model.QaStatus,model.QaNotes,model.RowVersion,token));}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/document-exports/{id:long}",async(long id,string? rowVersion,SqlServerDocumentExportStore store,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await store.SoftDeleteAsync(id,rowVersion,token);return Results.Ok();}catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/reconciliation/activity-documents",async(ActivityDocumentReconciliationService reconciliation,CancellationToken token)=>Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/sqlserver-pilot/workspace/{caseId:long}",async(long caseId,SqlServerWorkspaceQuery query,CaseAccessService access,CancellationToken token)=>
    await access.CanReadAsync(caseId,token)?Results.Ok(await query.GetWorkspaceAsync(caseId,await access.GetVisibleCaseIdsAsync(token),token)):Results.Forbid());
app.MapGet("/api/database/sqlserver-pilot/dashboard",async(SqlServerWorkspaceQuery query,CaseAccessService access,CancellationToken token)=>
    Results.Ok(await query.GetDashboardAsync(await access.GetVisibleCaseIdsAsync(token),token)));
app.MapGet("/api/database/sqlserver-pilot/upcoming-work",async(string? type,string? urgency,int? limit,SqlServerWorkspaceQuery query,CaseAccessService access,CancellationToken token)=>
    Results.Ok(await query.GetUpcomingWorkAsync(type??"all",urgency??"All Open",limit??5,await access.GetVisibleCaseIdsAsync(token),token)));
app.MapGet("/api/database/sqlserver-pilot/service-queue",async(SqlServerWorkspaceQuery query,CaseAccessService access,CancellationToken token)=>
    Results.Ok(await query.GetServiceQueueAsync(await access.GetVisibleCaseIdsAsync(token),token)));
app.MapGet("/api/database/reconciliation/workspace-dashboard/{caseId:long}",async(long caseId,WorkspaceDashboardReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(caseId,token)));
app.MapGet("/api/database/sqlserver-pilot/dashboard/attorney",async(string? matterType,string? project,string? county,string? priority,string? currentHolder,string? stage,bool? trialTrack,string? momentumStatus,string? search,SqlServerWorkspaceQuery query,CaseAccessService access,CancellationToken token)=>
    Results.Ok(await query.GetAttorneyDashboardAsync(new(){MatterType=matterType,Project=project,County=county,Priority=priority,CurrentHolder=currentHolder,Stage=stage,TrialTrack=trialTrack,MomentumStatus=momentumStatus,Search=search},await access.GetVisibleCaseIdsAsync(token),token)));
app.MapGet("/api/database/reconciliation/dashboard-attorney",async(string? matterType,string? project,string? county,string? priority,string? currentHolder,string? stage,bool? trialTrack,string? momentumStatus,string? search,AttorneyDashboardReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(new(){MatterType=matterType,Project=project,County=county,Priority=priority,CurrentHolder=currentHolder,Stage=stage,TrialTrack=trialTrack,MomentumStatus=momentumStatus,Search=search},token)));
app.MapGet("/api/database/sqlserver-pilot/risk-analysis/{caseId:long}",async(long caseId,SqlServerRiskAnalysisStore risk,CancellationToken token)=>
{
    try{return Results.Ok(await risk.GetAsync(caseId,token));}
    catch(InvalidOperationException ex){return Results.NotFound(new{error=ex.Message});}
});
app.MapPost("/api/database/sqlserver-pilot/risk-analysis/{caseId:long}/preview",async(long caseId,RiskAnalysisInput input,SqlServerRiskAnalysisStore risk,CancellationToken token)=>
{
    try{return Results.Ok(await risk.PreviewAsync(caseId,input,token));}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/database/sqlserver-pilot/risk-analysis/{caseId:long}",async(long caseId,RiskAnalysisInput input,SqlServerRiskAnalysisStore risk,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    input.CaseId=caseId;
    try{return Results.Ok(await risk.SaveAsync(input,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/risk-analysis/{id:long}",async(long id,string? rowVersion,SqlServerRiskAnalysisStore risk,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await risk.DeleteAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/risk-analysis/{caseId:long}/history",async(long caseId,SqlServerRiskAnalysisStore risk,CancellationToken token)=>
    Results.Ok(await risk.GetHistoryAsync(caseId,token)));
app.MapGet("/api/database/sqlserver-pilot/risk-analysis/{caseId:long}/history/{historyId:long}",async(long caseId,long historyId,SqlServerRiskAnalysisStore risk,CancellationToken token)=>
{
    try{return Results.Ok(await risk.GetHistorySnapshotAsync(caseId,historyId,token));}
    catch(InvalidOperationException ex){return Results.NotFound(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/risk-analysis/history/{historyId:long}",async(long historyId,string? rowVersion,SqlServerRiskAnalysisStore risk,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await risk.DeleteHistoryAsync(historyId,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/risk-analysis-offers",async(long? caseId,SqlServerRiskOfferStore offers,CancellationToken token)=>
    Results.Ok(await offers.GetAsync(caseId,token)));
app.MapPost("/api/database/sqlserver-pilot/risk-analysis-offers",async(RiskAnalysisOfferLogEntry model,SqlServerRiskOfferStore offers,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await offers.SaveAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/risk-analysis-offers/{id:long}",async(long id,string? rowVersion,SqlServerRiskOfferStore offers,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await offers.DeleteAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/reconciliation/risk-analysis/{caseId:long}",async(long caseId,RiskAnalysisReconciliationService reconciliation,CancellationToken token)=>
{
    try{return Results.Ok(await reconciliation.CompareAsync(caseId,token));}
    catch(InvalidOperationException ex){return Results.NotFound(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/checklist-templates",async(SqlServerWorkTemplateStore templates,CancellationToken token)=>
    Results.Ok(await templates.GetChecklistAsync(token)));
app.MapPost("/api/database/sqlserver-pilot/checklist-templates",async(ChecklistTemplateRecord model,SqlServerWorkTemplateStore templates,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await templates.SaveChecklistAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/checklist-templates/{id:long}",async(long id,string? rowVersion,SqlServerWorkTemplateStore templates,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await templates.DeleteChecklistAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPost("/api/database/sqlserver-pilot/checklist-template-items",async(ChecklistTemplateItemRecord model,SqlServerWorkTemplateStore templates,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await templates.SaveChecklistItemAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/checklist-template-items/{id:long}",async(long id,string? rowVersion,SqlServerWorkTemplateStore templates,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await templates.DeleteChecklistItemAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/deadline-templates",async(SqlServerWorkTemplateStore templates,CancellationToken token)=>
    Results.Ok(await templates.GetDeadlinesAsync(token)));
app.MapPost("/api/database/sqlserver-pilot/deadline-templates",async(DeadlineTemplateRecord model,SqlServerWorkTemplateStore templates,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await templates.SaveDeadlineAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapDelete("/api/database/sqlserver-pilot/deadline-templates/{id:long}",async(long id,string? rowVersion,SqlServerWorkTemplateStore templates,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{await templates.DeleteDeadlineAsync(id,rowVersion,token);return Results.Ok();}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/reconciliation/work-templates",async(WorkTemplateReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapPost("/api/database/sqlserver-pilot/cases/{caseId:long}/risk-analysis/narrative",async(
    long caseId,RiskNarrativeManualInputs manual,SqlServerDocumentCompositionService composition,CancellationToken token)=>
{
    try{return Results.Ok(new{narrative=await composition.GenerateRiskNarrativeAsync(caseId,manual,token)});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/sqlserver-pilot/org-defaults",async(SqlServerOrganizationDefaultsStore defaults,CancellationToken token)=>
    Results.Ok(await defaults.GetAsync(token)));
app.MapPost("/api/database/sqlserver-pilot/org-defaults",async(OrgDefaults model,SqlServerOrganizationDefaultsStore defaults,IConfiguration configuration,CancellationToken token)=>
{
    if(!configuration.GetValue("Database:SqlServerPilotWritesEnabled",false))return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try{return Results.Ok(await defaults.SaveAsync(model,token));}
    catch(WorkItemConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
    catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapGet("/api/database/reconciliation/org-defaults",async(OrganizationDefaultsReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(token)));
app.MapGet("/api/database/reconciliation/reference-library",async(ReferenceLibraryReconciliationService reconciliation,CancellationToken token)=>
    Results.Ok(await reconciliation.CompareAsync(token)));

if (Directory.Exists(clientDist))
{
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(Path.Combine(clientDist, "index.html"));
    });
}
else
{
    app.MapGet("/", () => Results.Text($"Client build not found yet. Expected frontend assets under {clientDist}. Run npm install and npm run build in the client folder.", "text/plain"));
}

app.Run();
