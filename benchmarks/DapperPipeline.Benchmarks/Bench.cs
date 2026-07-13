using DapperPipeline.Abstractions;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// Shared setup. Benchmarks run against a real PostgreSQL over TCP, because the claim being tested
/// is about <em>round-trips</em> — and an in-memory database would hide exactly the cost that
/// batching exists to remove.
/// </summary>
public static class Bench
{
    public const string ConnectionString =
        "Host=localhost;Port=5433;Database=dapperpipeline;Username=postgres;Password=VeryStr0ngP@ssw0rd;" +
        "Maximum Pool Size=20";

    public sealed record Row(string Source, int ExternalId, bool IsPrimary);

    public static NpgsqlConnection Open()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    public static void Exec(NpgsqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Creates the benchmark schema. No indexes or constraints — we are timing the insert path.</summary>
    public static void CreateSchema()
    {
        using var conn = Open();
        Exec(conn, """
            DROP SCHEMA IF EXISTS bench CASCADE;
            CREATE SCHEMA bench;
            CREATE TABLE bench.entries (source text NOT NULL, external_id int NOT NULL, is_primary boolean NOT NULL);
            CREATE TABLE bench.counter (id int PRIMARY KEY, hits int NOT NULL);
            INSERT INTO bench.counter VALUES (1, 0);
            CREATE TABLE bench.orders (id int PRIMARY KEY, name text NOT NULL, qty int NOT NULL);
            INSERT INTO bench.orders
            SELECT i, 'order-' || i, i % 7 FROM generate_series(1, 1000) AS i;
            """);
    }

    public static void DropSchema()
    {
        using var conn = Open();
        Exec(conn, "DROP SCHEMA IF EXISTS bench CASCADE;");
    }

    public static void TruncateEntries()
    {
        using var conn = Open();
        Exec(conn, "TRUNCATE bench.entries;");
    }

    public static IDapperPipeline Pipeline(params Action<IServiceCollection>[] register)
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new PostgreSqlDialect(ConnectionString));
        foreach (var r in register) r(services);
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    public static List<Row> Rows(int n) =>
        Enumerable.Range(1, n).Select(i => new Row($"src-{i}", i, i % 2 == 0)).ToList();
}
