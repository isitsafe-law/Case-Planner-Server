# Portable release checklist

Use this on a connected build machine before distributing a portable SQLite test package.

1. Start from the intended committed Git revision.
2. Run the client tests and build.
3. Publish with `scripts/publish-portable.ps1`.
4. Confirm `portable-build-manifest.json` exists beside the executable.
5. Compare the manifest commit to the intended revision:

   ```powershell
   $expectedCommit = (git rev-parse --short HEAD).Trim()
   powershell -ExecutionPolicy Bypass -File .\scripts\validate-portable-manifest.ps1 `
     -PackagePath .\release\CasePlannerIT_Handoff_<date> `
     -ExpectedVersion 1.0.16 `
     -ExpectedCommit $expectedCommit
   ```

6. Run `scripts/local-package-smoke.ps1` against the extracted package. It verifies the manifest against
   the running server, checks portable and backup/restore validation, confirms the document catalog, and
   exercises DOCX generation.
7. Keep `portable-build-manifest.json` with the package. Include its build identifier and commit in any
   IT/support report.
8. Do not copy a developer database, backups, logs, or generated exports into a fresh handoff package.

The smoke test is the final package check. If the manifest or build identity does not match the intended
revision, stop and republish from the correct commit.
