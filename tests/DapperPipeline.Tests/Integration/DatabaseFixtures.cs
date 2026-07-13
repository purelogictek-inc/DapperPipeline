using System.Data.Common;
using DapperPipeline.Abstractions;
using DapperPipeline.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Microsoft.Data.SqlClient;

namespace DapperPipeline.Tests.Integration;

/// <summary>
/// Connection details for the containerised databases these tests run against.
/// </summary>
/// <remarks>
/// String-comparison tests are not evidence. Every renderer here — PostgreSQL's <c>unnest</c>,
/// SQL Server's <c>OPENJSON</c> and TVPs, the statement separator, the isolation levels — was
/// previously verified only by asserting on generated text, which proves nothing about whether the
/// engine will accept it.
///
/// Tests use [SkippableFact] + Skip.IfNot(...), so with no database they are reported as SKIPPED.
/// An early `return` would have reported them as PASSED, which is worse than having no test at all.
/// </remarks>
internal static class Db
{
    private const string DatabaseName = "dapperpipeline";

    /// <summary>
    /// Connection strings come from the environment when set, so CI can point at its service
    /// containers (different ports) without the tests hardcoding anyone's local setup.
    /// </summary>
    public static readonly string PostgresConnection =
        Environment.GetEnvironmentVariable("DP_POSTGRES")
        ?? $"Host=localhost;Port=5433;Database={DatabaseName};Username=postgres;Password=VeryStr0ngP@ssw0rd;Include Error Detail=true";

    public static readonly string SqlServerConnection =
        Environment.GetEnvironmentVariable("DP_SQLSERVER")
        ?? $"Server=localhost,1433;Database={DatabaseName};User Id=sa;Password=VeryStr0ngP@ssw0rd;TrustServerCertificate=true;Encrypt=false";

    /// <summary>Is the server actually up? Used to skip rather than fail where there are no containers.</summary>
    private static bool CanConnect(Func<DbConnection> factory)
    {
        try
        {
            using var conn = factory();
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool PostgresAvailable => CanConnect(() => new NpgsqlConnection(PostgresConnection));

    /// <summary>
    /// SQL Server ships with no application database, so create it on first use.
    /// </summary>
    /// <remarks>
    /// It must be created with <strong>default settings</strong> — in particular
    /// <c>ALLOW_SNAPSHOT_ISOLATION OFF</c>, which is what a real consumer has. Running these tests
    /// against <c>master</c> (which uniquely has snapshot ON) is exactly what hid the isolation bug.
    /// </remarks>
    public static bool SqlServerAvailable => _sqlServerReady.Value;

    private static readonly Lazy<bool> _sqlServerReady = new(() =>
    {
        var master = SqlServerConnection.Replace($"Database={DatabaseName}", "Database=master");
        if (!CanConnect(() => new SqlConnection(master))) return false;

        using var conn = new SqlConnection(master);
        conn.Open();
        Exec(conn, $"IF DB_ID('{DatabaseName}') IS NULL CREATE DATABASE [{DatabaseName}];");
        return true;
    });

    public static void Exec(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Builds a real pipeline over the given dialect, as a consumer would.</summary>
    public static IDapperPipeline Pipeline(IDatabaseDialect dialect, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(dialect);
        extra?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }
}
