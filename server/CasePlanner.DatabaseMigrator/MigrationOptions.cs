namespace CasePlanner.DatabaseMigrator;

public sealed record MigrationOptions(
    string SqlitePath,
    string SqlServerConnectionString,
    string Schema,
    bool AllowNonEmpty,
    bool SchemaOnly)
{
    public const string Usage = "Usage: dotnet run --project server/CasePlanner.DatabaseMigrator -- --sqlite <path> [--sqlserver <connection-string>] [--schema dbo] [--schema-only] [--allow-non-empty]. The connection string may instead be supplied in CASEPLANNER_SQLSERVER_CONNECTION_STRING.";

    public static MigrationOptions Parse(string[] args)
    {
        string? sqlite = null;
        string? sqlServer = Environment.GetEnvironmentVariable("CASEPLANNER_SQLSERVER_CONNECTION_STRING");
        var schema = "dbo";
        var allowNonEmpty = false;
        var schemaOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sqlite" when i + 1 < args.Length:
                    sqlite = args[++i];
                    break;
                case "--sqlserver" when i + 1 < args.Length:
                    sqlServer = args[++i];
                    break;
                case "--schema" when i + 1 < args.Length:
                    schema = args[++i];
                    break;
                case "--allow-non-empty":
                    allowNonEmpty = true;
                    break;
                case "--schema-only":
                    schemaOnly = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(sqlite) || string.IsNullOrWhiteSpace(sqlServer))
            throw new ArgumentException("Both --sqlite and --sqlserver are required.");
        if (!File.Exists(sqlite))
            throw new ArgumentException($"SQLite source file was not found: {sqlite}");
        SqlName.RequireSafe(schema, "schema");

        return new(Path.GetFullPath(sqlite), sqlServer, schema, allowNonEmpty, schemaOnly);
    }
}

internal static class SqlName
{
    public static string Quote(string value)
    {
        RequireSafe(value, "SQL identifier");
        return $"[{value}]";
    }

    public static void RequireSafe(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new ArgumentException($"Invalid {label} '{value}'. Only letters, numbers, and underscores are allowed.");
    }
}
