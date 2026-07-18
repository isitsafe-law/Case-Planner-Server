# Microsoft Entra ID setup

Case Planner uses a single-tenant React SPA plus ASP.NET Core web API design. The browser uses MSAL's
authorization-code flow with PKCE; it never stores a client secret. The API validates bearer tokens with
Microsoft.Identity.Web and maps the immutable combined `tid:oid` identity into SQL Server `app_users`.

Authentication is committed **disabled**. Do not enable it for general users until case-assignment
authorization is enforced across every case and work-queue endpoint.

## Required Entra app registrations

Create two single-tenant app registrations in the agency workforce tenant.

### 1. Case Planner API

1. Record its Application (client) ID and Directory (tenant) ID.
2. Under **Expose an API**, accept or set the Application ID URI `api://<API-CLIENT-ID>`.
3. Add delegated scope `CasePlanner.Access` for administrators and users.
4. Add application roles `CasePlanner.User` and `CasePlanner.Admin`, both with **Users/Groups** as allowed
   member types. Administrators should receive both roles because the base API policy requires the user role.
5. Assign only the approved pilot group/users to the enterprise application.

The API app does not need a client secret because it validates tokens and does not currently call a
downstream API.

### 2. Case Planner SPA

1. Under **Authentication**, add a **Single-page application** platform.
2. Add the exact local redirect URI `http://127.0.0.1:5188` for the current pilot.
3. Add the eventual HTTPS server URI only after hosting is approved; production should use HTTPS.
4. Under **API permissions**, add the delegated `CasePlanner.Access` permission exposed by the API app.
5. Grant tenant-wide admin consent if agency policy calls for it.
6. Do not create or place a client secret in the React application.

## Server configuration

Use environment variables or the deployment secret/configuration store. GUID values are identifiers,
not secrets, but keeping environment-specific values out of committed JSON makes deployments portable.

```powershell
$env:AzureAd__TenantId = "<DIRECTORY-TENANT-ID>"
$env:AzureAd__ClientId = "<API-APPLICATION-ID>"
$env:Authentication__Entra__SpaClientId = "<SPA-APPLICATION-ID>"
$env:Authentication__Entra__ApiScope = "CasePlanner.Access"
$env:Authentication__Entra__RequiredAppRole = "CasePlanner.User"
$env:Authentication__Entra__AdministratorAppRole = "CasePlanner.Admin"
$env:Authentication__Entra__AdministratorPilotOnly = "true"
$env:ConnectionStrings__CasePlannerSqlServer = "Server=.\CASEPLANNERDEV;Database=CasePlannerDev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$env:Authentication__Entra__Enabled = "true"
dotnet run --project server\CasePlanner.Web.Server
```

For local registration, use the exact host from the redirect URI. `localhost` and `127.0.0.1` are distinct
redirect URIs to Entra. For a shared deployment, configure an HTTPS DNS name and replace the local SQL
Server certificate exception with normal certificate validation.

## Runtime behavior

- `GET /api/auth/config` is public and returns only the authority, SPA client ID, and API scope required
  by MSAL. It contains no secret.
- All other `/api` routes require a valid access token when Entra is enabled.
- Tokens must contain the exact delegated scope and, when configured, the required app role.
- Valid users are inserted or refreshed in `dbo.app_users`; authorization keys use `tid` plus `oid`.
- Display name and email are informational and are never used to authorize access.
- Deactivating an `app_users` row prevents it from being reprovisioned, although a friendly forbidden-user
  response is still part of the next authorization phase.
- Administrator endpoints for users and case assignments exist under `/api/admin`; they return 404 while
  Entra is disabled and require the configured administrator app role when enabled.
- `AdministratorPilotOnly=true` is a release safety gate: all protected APIs require the administrator
  role even when a token otherwise has the user role and delegated scope. Do not remove this gate until
  assignment filtering covers every route listed below.

## Assignment-aware route boundary

The ordinary-user authorization boundary is now fail-closed. When authentication is enabled and
`AdministratorPilotOnly=false`:

- administrators remain unrestricted;
- `Owner` and `Collaborator` assignments can read and write their assigned case routes;
- `ReadOnly` assignments can read but cannot write;
- the case catalog, both dashboards, upcoming-work view, service queue, and migrated work queues are filtered
  before aggregation so counts and rows contain only assigned cases;
- saves for migrated child records validate the submitted `CaseId`;
- child-record deletes, activity edits, issue-tag removal, document content/download/QA, and risk-offer routes
  resolve the stored child record back to its owning case before authorization;
- case creation, case deletion, organization-wide mutations, and every unclassified route remain denied to
  ordinary users;
- required organization-wide template/tag/reference catalogs are available read-only.

Assignment checks are cached only within the current HTTP request. SQL Server remains the authoritative
source for user identity and assignments even while SQLite is the active business-data provider.

## Still required before shared use

1. Complete SQL Server repository cutover for document generation, settings, dashboards, and activity writes.
2. Add authenticated integration tests using IT-owned Entra test identities and assignment groups.
3. Configure HTTPS, reverse proxy/hosting, SQL Server service identity, backups, monitoring, and retention.
4. Perform a security review and a limited pilot before opening access broadly.

New audit writes use the current Entra user's durable application user ID and a human-readable display label.
When authentication is intentionally disabled for home development, the label is `Local development user` and
the user ID is null. Historical imported audit rows remain unchanged rather than being attributed retroactively.
