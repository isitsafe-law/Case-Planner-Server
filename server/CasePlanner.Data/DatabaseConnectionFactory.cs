using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace CasePlanner.Data;

public interface IDatabaseConnectionFactory
{
    string Provider { get; }
    DbConnection CreateConnection();
}

public sealed class DatabaseConnectionFactory(DatabaseOptions options) : IDatabaseConnectionFactory
{
    public string Provider => options.Provider;

    public DbConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException($"Database:ConnectionString is required for provider '{options.Provider}'.");
        }

        return options.Provider.ToLowerInvariant() switch
        {
            "sqlite" => new SqliteConnection(options.ConnectionString),
            "sqlserver" => new SqlConnection(options.ConnectionString),
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{options.Provider}'. Use '{DatabaseProviders.Sqlite}' or '{DatabaseProviders.SqlServer}'.")
        };
    }
}
