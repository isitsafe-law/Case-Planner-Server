# Case Planner – First Connected Machine Checklist

Use this checklist on the first Windows machine that can reach the approved NuGet source and, later, the SQL Server pilot instance.

## Build machine

Install .NET 10 SDK, Node.js 20+, npm, and PowerShell. From the repository root:

```powershell
dotnet restore .\CasePlanner.slnx
powershell -ExecutionPolicy Bypass -File .\scripts\phase1-smoke.ps1 -Restore
Push-Location .\client
npm ci
npm test -- --run
npm run build
Pop-Location
```

For a machine without NuGet access, the web-only local build/publish check can use already-restored assets
(it intentionally skips launching the app):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\phase1-smoke.ps1 -WebOnly
```

For the packaged localhost handoff build, run the independent runtime check from the extracted package:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-local.ps1
```

The check starts the published executable on a temporary localhost port, verifies `/api/health` and the
document catalog, generates an Interrogatories DOCX, validates its ZIP signature, and stops the process.

## IIS pilot server

Install the ASP.NET Core/.NET 10 Hosting Bundle. Publish with:

```powershell
dotnet publish .\server\CasePlanner.Web.Server\CasePlanner.Web.Server.csproj -c Release -p:PublishProfile=IisFrameworkDependent -o .\publish
```

For the self-contained Windows x64 IT handoff ZIP, use a connected build machine and run:

```powershell
dotnet restore .\CasePlanner.slnx
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1 -Output release\CasePlannerIT_Handoff_<date>
```

Then copy `docs`, `config`, and the ordered `server\CasePlanner.DatabaseMigrator\Sql` scripts into the
handoff folder before compressing it. The portable profile requires the `net10.0/win-x64` restore assets;
the offline local machine cannot produce that self-contained profile.

Create an IIS site and application pool running **No Managed Code**. Grant the application-pool identity access to the configured data and document-storage roots.

## SQL pilot

The application remains SQLite-first until IT approves SQL cutover. Before enabling SQL Server:

1. Create the `CasePlannerDev` database on the approved instance.
2. Run the ordered scripts in `server/CasePlanner.DatabaseMigrator/Sql`.
3. Confirm the application identity can connect using the approved Entra or service-account method.
4. Set `Database:ActiveProvider=SqlServer` only after SQL smoke tests pass.

The repository includes `server/CasePlanner.Web.Server/appsettings.SqlServer.example.json` as the
copy-from template for the approved cutover configuration. It enables SQL Server and Entra deliberately;
do not use it on the local SQLite test package, and do not enable it until the readiness report and IT
authentication/storage approvals are complete.

Do not copy a developer SQLite database into production. Treat local database contents as test data only.
