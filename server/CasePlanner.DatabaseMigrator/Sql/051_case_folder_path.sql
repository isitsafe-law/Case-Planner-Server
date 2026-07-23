SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback batch, item 9: a network (UNC) path the user pastes in, e.g.
-- \\fileserver\share\JobNumber\Tract, so POST /api/cases/{id}/open-folder can launch Windows
-- Explorer pointed at it (Process.Start, ProcessStartInfo.ArgumentList - see Program.cs). This
-- only makes sense in the current single-machine deployment model (the app runs on each user's
-- own Windows machine as a local process), same as every other locally-executed action in this
-- codebase. Same COL_LENGTH-guarded ALTER pattern as every other optional case column. There is no
-- live SQL Server sandbox available here to exercise this against a real pilot instance - same
-- limitation already noted for every other migration file in this repo; this one has been
-- reviewed for consistency with its siblings but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'case_folder_path') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [case_folder_path] nvarchar(1000) NULL;
