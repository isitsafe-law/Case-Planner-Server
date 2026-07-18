using CasePlanner.DatabaseMigrator;

try
{
    var options = MigrationOptions.Parse(args);
    var migrator = new SqliteToSqlServerMigrator(options);
    var result = await migrator.RunAsync();
    Console.WriteLine($"Migration complete: {result.TableCount} tables and {result.RowCount} rows copied.");
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(MigrationOptions.Usage);
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Migration failed: {ex.Message}");
    return 1;
}
