IF COL_LENGTH('dbo.deadlines', 'assigned_staff_name') IS NULL
BEGIN
    ALTER TABLE dbo.deadlines ADD assigned_staff_name nvarchar(200) NULL;
END
GO
