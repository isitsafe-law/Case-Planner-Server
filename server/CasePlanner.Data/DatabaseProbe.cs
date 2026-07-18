using System.Diagnostics;

namespace CasePlanner.Data;

public sealed record DatabaseProbeResult(string Provider, bool Configured, bool Reachable, long? ElapsedMilliseconds, string Message);

public static class DatabaseProbe
{
    public static async Task<DatabaseProbeResult> CheckAsync(
        IDatabaseConnectionFactory factory,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = factory.CreateConnection();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 120)));
            await connection.OpenAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = Math.Clamp(timeoutSeconds, 1, 120);
            await command.ExecuteScalarAsync(timeout.Token);
            return new(factory.Provider, true, true, stopwatch.ElapsedMilliseconds, "Connection succeeded.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase))
        {
            return new(factory.Provider, false, false, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new(factory.Provider, true, false, stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }
}
