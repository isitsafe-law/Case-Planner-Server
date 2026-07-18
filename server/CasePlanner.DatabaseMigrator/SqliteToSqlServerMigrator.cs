using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace CasePlanner.DatabaseMigrator;

public sealed record MigrationResult(int TableCount, long RowCount);

public sealed class SqliteToSqlServerMigrator(MigrationOptions options)
{
    public async Task<MigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var source = new SqliteConnection($"Data Source={options.SqlitePath};Mode=ReadOnly");
        await using var target = new SqlConnection(options.SqlServerConnectionString);
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);

        await EnsureSchemaAsync(target, cancellationToken);
        var tables = await ReadTablesAsync(source, cancellationToken);
        long totalRows = 0;

        foreach (var table in tables)
        {
            var columns = await ReadColumnsAsync(source, table, cancellationToken);
            if (columns.Count == 0) continue;
            var indexes = await ReadIndexesAsync(source, table, cancellationToken);

            Console.WriteLine($"Preparing {options.Schema}.{table}...");
            await CreateTableAsync(target, table, columns, indexes, cancellationToken);
            await EnsureTargetIsSafeAsync(target, table, cancellationToken);

            if (!options.SchemaOnly)
            {
                var copied = await CopyRowsAsync(source, target, table, columns, cancellationToken);
                totalRows += copied;
                Console.WriteLine($"  copied {copied} row(s)");
            }

            await CreateIndexesAsync(target, table, indexes, cancellationToken);
        }

        await ApplySqlServerFoundationAsync(target, cancellationToken);
        await RecordMigrationAsync(target, tables.Count, totalRows, cancellationToken);
        return new(tables.Count, totalRows);
    }

    private async Task EnsureSchemaAsync(SqlConnection target, CancellationToken token)
    {
        var schema = SqlName.Quote(options.Schema);
        await using var command = target.CreateCommand();
        command.CommandText = $"IF SCHEMA_ID(@schema) IS NULL EXEC('CREATE SCHEMA {schema}');";
        command.Parameters.AddWithValue("@schema", options.Schema);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<List<string>> ReadTablesAsync(SqliteConnection source, CancellationToken token)
    {
        var result = new List<string>();
        await using var command = source.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var name = reader.GetString(0);
            SqlName.RequireSafe(name, "table name");
            result.Add(name);
        }
        return result;
    }

    private static async Task<List<SourceColumn>> ReadColumnsAsync(SqliteConnection source, string table, CancellationToken token)
    {
        var result = new List<SourceColumn>();
        await using var command = source.CreateCommand();
        command.CommandText = $"PRAGMA table_info({SqlName.Quote(table)})";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var name = reader.GetString(1);
            SqlName.RequireSafe(name, "column name");
            result.Add(new(name, reader.IsDBNull(2) ? "TEXT" : reader.GetString(2), reader.GetInt32(3) == 1, reader.GetInt32(5) > 0));
        }
        return result;
    }

    private async Task CreateTableAsync(SqlConnection target, string table, List<SourceColumn> columns, List<SourceIndex> indexes, CancellationToken token)
    {
        var qualified = $"{SqlName.Quote(options.Schema)}.{SqlName.Quote(table)}";
        var indexedColumns = indexes.SelectMany(i => i.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = columns.Select(column =>
        {
            var primaryInteger = column.PrimaryKey && column.SqliteType.Contains("INT", StringComparison.OrdinalIgnoreCase);
            var sqlType = MapType(column.SqliteType, column.PrimaryKey || indexedColumns.Contains(column.Name));
            var nullability = column.PrimaryKey || column.NotNull ? "NOT NULL" : "NULL";
            var identity = primaryInteger ? " IDENTITY(1,1)" : "";
            var primaryKey = column.PrimaryKey ? " PRIMARY KEY" : "";
            return $"{SqlName.Quote(column.Name)} {sqlType}{identity} {nullability}{primaryKey}";
        });

        await using var command = target.CreateCommand();
        command.CommandText = $"IF OBJECT_ID(@qualified, 'U') IS NULL CREATE TABLE {qualified} ({string.Join(", ", definitions)});";
        command.Parameters.AddWithValue("@qualified", $"{options.Schema}.{table}");
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task EnsureTargetIsSafeAsync(SqlConnection target, string table, CancellationToken token)
    {
        if (options.AllowNonEmpty || options.SchemaOnly) return;
        await using var command = target.CreateCommand();
        command.CommandText = $"SELECT TOP (1) 1 FROM {SqlName.Quote(options.Schema)}.{SqlName.Quote(table)}";
        if (await command.ExecuteScalarAsync(token) is not null)
            throw new InvalidOperationException($"Target table {options.Schema}.{table} is not empty. Use a clean database or explicitly pass --allow-non-empty.");
    }

    private async Task<long> CopyRowsAsync(SqliteConnection source, SqlConnection target, string table, List<SourceColumn> columns, CancellationToken token)
    {
        var primaryInteger = columns.Any(c => c.PrimaryKey && c.SqliteType.Contains("INT", StringComparison.OrdinalIgnoreCase));
        var qualified = $"{SqlName.Quote(options.Schema)}.{SqlName.Quote(table)}";
        await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(token);
        if (primaryInteger) await ExecuteAsync(target, transaction, $"SET IDENTITY_INSERT {qualified} ON", token);

        long count = 0;
        try
        {
            await using var select = source.CreateCommand();
            select.CommandText = $"SELECT * FROM {SqlName.Quote(table)}";
            await using var reader = await select.ExecuteReaderAsync(token);
            var names = columns.Select(c => SqlName.Quote(c.Name)).ToArray();
            var parameters = columns.Select((_, i) => $"@p{i}").ToArray();

            while (await reader.ReadAsync(token))
            {
                await using var insert = target.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO {qualified} ({string.Join(",", names)}) VALUES ({string.Join(",", parameters)})";
                for (var i = 0; i < columns.Count; i++)
                    insert.Parameters.AddWithValue(parameters[i], reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i));
                await insert.ExecuteNonQueryAsync(token);
                count++;
            }

            if (primaryInteger) await ExecuteAsync(target, transaction, $"SET IDENTITY_INSERT {qualified} OFF", token);
            await transaction.CommitAsync(token);
            return count;
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    private static async Task<List<SourceIndex>> ReadIndexesAsync(SqliteConnection source, string table, CancellationToken token)
    {
        await using var list = source.CreateCommand();
        list.CommandText = $"PRAGMA index_list({SqlName.Quote(table)})";
        await using var reader = await list.ExecuteReaderAsync(token);
        var rawIndexes = new List<(string Name, bool Unique)>();
        while (await reader.ReadAsync(token))
        {
            var name = reader.GetString(1);
            SqlName.RequireSafe(name, "index name");
            rawIndexes.Add((name, reader.GetInt32(2) == 1));
        }

        var result = new List<SourceIndex>();
        var ordinal = 0;
        foreach (var index in rawIndexes)
        {
            var columns = new List<string>();
            await using var info = source.CreateCommand();
            info.CommandText = $"PRAGMA index_info({SqlName.Quote(index.Name)})";
            await using var infoReader = await info.ExecuteReaderAsync(token);
            while (await infoReader.ReadAsync(token)) columns.Add(infoReader.GetString(2));
            if (columns.Count == 0) continue;

            var targetName = index.Name.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase)
                ? $"UX_{table}_{++ordinal}"
                : index.Name;
            result.Add(new(targetName, index.Unique, columns));
        }
        return result;
    }

    private async Task CreateIndexesAsync(SqlConnection target, string table, List<SourceIndex> indexes, CancellationToken token)
    {
        foreach (var index in indexes)
        {
            var qualifiedTable = $"{SqlName.Quote(options.Schema)}.{SqlName.Quote(table)}";
            var qualifiedIndex = SqlName.Quote(index.Name);
            var unique = index.Unique ? "UNIQUE " : "";
            // SQLite permits multiple rows with NULL in a unique key. A filtered SQL Server
            // index preserves that behavior and avoids migration failures on legacy optional keys.
            var filter = index.Unique
                ? $" WHERE {string.Join(" AND ", index.Columns.Select(c => $"{SqlName.Quote(c)} IS NOT NULL"))}"
                : "";
            await using var create = target.CreateCommand();
            create.CommandText = $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(@table) AND name = @name) CREATE {unique}INDEX {qualifiedIndex} ON {qualifiedTable} ({string.Join(",", index.Columns.Select(SqlName.Quote))}){filter};";
            create.Parameters.AddWithValue("@table", $"{options.Schema}.{table}");
            create.Parameters.AddWithValue("@name", index.Name);
            await create.ExecuteNonQueryAsync(token);
        }
    }

    private async Task RecordMigrationAsync(SqlConnection target, int tables, long rows, CancellationToken token)
    {
        var qualified = $"{SqlName.Quote(options.Schema)}.[caseplanner_migrations]";
        await using var command = target.CreateCommand();
        command.CommandText = $"IF OBJECT_ID(@name, 'U') IS NULL CREATE TABLE {qualified} ([id] bigint IDENTITY PRIMARY KEY, [source_file] nvarchar(2048) NOT NULL, [tables_copied] int NOT NULL, [rows_copied] bigint NOT NULL, [schema_only] bit NOT NULL, [completed_utc] datetime2 NOT NULL); INSERT INTO {qualified} (source_file,tables_copied,rows_copied,schema_only,completed_utc) VALUES (@source,@tables,@rows,@schemaOnly,SYSUTCDATETIME());";
        command.Parameters.AddWithValue("@name", $"{options.Schema}.caseplanner_migrations");
        command.Parameters.AddWithValue("@source", options.SqlitePath);
        command.Parameters.AddWithValue("@tables", tables);
        command.Parameters.AddWithValue("@rows", rows);
        command.Parameters.AddWithValue("@schemaOnly", options.SchemaOnly);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplySqlServerFoundationAsync(SqlConnection target, CancellationToken token)
    {
        var scriptFolder = Path.Combine(AppContext.BaseDirectory, "Sql");
        var scripts = Directory.Exists(scriptFolder)
            ? Directory.GetFiles(scriptFolder, "*.sql").Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        if (scripts.Count == 0)
            throw new FileNotFoundException("The SQL Server foundation scripts were not copied to the migrator output.", scriptFolder);

        foreach (var scriptPath in scripts)
        {
            var sql = (await File.ReadAllTextAsync(scriptPath, token)).Replace("$(Schema)", options.Schema, StringComparison.Ordinal);
            await using var command = target.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(token);
        }
    }

    private static string MapType(string sqliteType, bool indexed)
    {
        var type = sqliteType.ToUpperInvariant();
        if (type.Contains("INT")) return "bigint";
        if (type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB")) return "float";
        if (type.Contains("BLOB")) return "varbinary(max)";
        if (type.Contains("NUM") || type.Contains("DEC")) return "decimal(19,4)";
        return indexed ? "nvarchar(450)" : "nvarchar(max)";
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }

    private sealed record SourceColumn(string Name, string SqliteType, bool NotNull, bool PrimaryKey);
    private sealed record SourceIndex(string Name, bool Unique, List<string> Columns);
}
