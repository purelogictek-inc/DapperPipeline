using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// What does a transaction actually cost, per engine? This is the number the whole
/// "should we default to no transaction" question turns on.
/// </summary>
/// <remarks>
/// The folklore is that a transaction around a read is "lightweight and safe" — which is true of the
/// SERVER-side work, and says nothing about the wire. BEGIN and COMMIT are round-trips, and a
/// round-trip is ~75 μs whatever the engine thinks of it. Measured, not assumed.
/// </remarks>
[MemoryDiagnoser]
public class TransactionCostBenchmarks
{
    private const string Sql = "SELECT 1";

    private static string SqlServerConn =>
        Environment.GetEnvironmentVariable("DP_SQLSERVER")
        ?? "Server=localhost,1433;Database=dapperpipeline;User Id=sa;Password=VeryStr0ngP@ssw0rd;TrustServerCertificate=true;Encrypt=false";

    private static string PostgresConn =>
        Environment.GetEnvironmentVariable("DP_POSTGRES")
        ?? "Host=localhost;Port=5433;Database=dapperpipeline;Username=postgres;Password=VeryStr0ngP@ssw0rd";

    [Benchmark(Baseline = true, Description = "SQL Server: no transaction")]
    public int SqlServer_NoTx()
    {
        using var conn = new SqlConnection(SqlServerConn);
        conn.Open();
        return conn.QuerySingle<int>(Sql);
    }

    [Benchmark(Description = "SQL Server: in a transaction")]
    public int SqlServer_Tx()
    {
        using var conn = new SqlConnection(SqlServerConn);
        conn.Open();
        using var tx = conn.BeginTransaction();
        var r = conn.QuerySingle<int>(Sql, transaction: tx);
        tx.Commit();
        return r;
    }

    [Benchmark(Description = "PostgreSQL: no transaction")]
    public int Postgres_NoTx()
    {
        using var conn = new NpgsqlConnection(PostgresConn);
        conn.Open();
        return conn.QuerySingle<int>(Sql);
    }

    [Benchmark(Description = "PostgreSQL: in a transaction")]
    public int Postgres_Tx()
    {
        using var conn = new NpgsqlConnection(PostgresConn);
        conn.Open();
        using var tx = conn.BeginTransaction();
        var r = conn.QuerySingle<int>(Sql, transaction: tx);
        tx.Commit();
        return r;
    }
}
