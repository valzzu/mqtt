using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Npgsql;

public class UptimeHostedService : BackgroundService
{
    private readonly string _connectionString;
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

    public UptimeHostedService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public TimeSpan GetUptime() => Stopwatch.Elapsed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("UptimeHostedService started.");

        // Ensure table exists (optional; comment out if you prefer migrations)
        await EnsureTableAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SaveUptimeAsync(stoppingToken);
                Console.WriteLine($"Saved uptime: {GetUptime()}");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Npgsql.NpgsqlException ex)
            {
                Console.WriteLine($"Postgres not ready yet: {ex.Message}. Retrying in 5s...");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }


        Console.WriteLine("UptimeHostedService stopping.");
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = @"
            CREATE TABLE IF NOT EXISTS MQTTData (
                id SERIAL PRIMARY KEY,
                logged_at TIMESTAMP NOT NULL,
                uptime_seconds BIGINT NOT NULL
            );";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SaveUptimeAsync(CancellationToken ct)
    {
        
        var uptime = GetUptime();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = "UPDATE MQTTData SET uptime_seconds = @uptime, logged_at = @time WHERE id = 1";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("time", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("uptime", (long)uptime.TotalSeconds);
        await cmd.ExecuteNonQueryAsync(ct);
        
    }
}
