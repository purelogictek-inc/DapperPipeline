using DapperPipeline.Abstractions;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.QueryBuilding;
using Microsoft.Data.Sqlite;

namespace DapperPipeline.Tests.RowSets;

public sealed class SqliteRowSetTests
{
    private sealed record Entry(string Source, int Value);

    private static readonly Entry[] Entries = [new("espn", 1), new("pfr", 2), new("nfl", 3)];

    private static (string Sql, IDictionary<string, object?> Parameters) Build(SqliteDialect dialect)
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), dialect.RowSetRenderer);
        qb.BeginCommandScope(1);

        var entries = qb.RowSet("entry", Entries, map =>
        {
            map.Column("source", x => x.Source);
            map.Column("external_id", x => x.Value);
        });
        qb.Append($"SELECT entry.source, entry.external_id FROM {entries}");

        return (qb.Sql, (IDictionary<string, object?>)qb.Parameters);
    }

    [Fact]
    public void Default_uses_json_each_and_binds_a_single_parameter()
    {
        var (sql, parameters) = Build(new SqliteDialect("Data Source=:memory:"));

        Assert.Contains("json_each(", sql);
        Assert.Contains("json_extract(je.value, '$.source') AS source", sql);
        Assert.Contains("AS entry", sql);

        // One parameter for three rows — parity with SQL Server's OPENJSON.
        Assert.Single(parameters);
    }

    [Fact]
    public void Values_strategy_falls_back_to_the_portable_rendering()
    {
        var (sql, parameters) = Build(new SqliteDialect("Data Source=:memory:")
        {
            RowSetStrategy = SqliteRowSetStrategy.Values,
        });

        Assert.Contains("UNION ALL", sql);
        Assert.Equal(6, parameters.Count); // 3 rows × 2 columns
    }

    /// <summary>
    /// Executes the rendered SQL against a real in-memory SQLite database. Rendering the right text
    /// is not the same as SQLite accepting it — json_each has to actually run.
    /// </summary>
    [Fact]
    public void Json_each_rendering_actually_executes_against_sqlite()
    {
        var (sql, parameters) = Build(new SqliteDialect("Data Source=:memory:"));

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var rows = new List<(string Source, long Id)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        // Round-trips through JSON with the right values, types and order.
        Assert.Equal(3, rows.Count);
        Assert.Equal([("espn", 1L), ("pfr", 2L), ("nfl", 3L)], rows);
    }

    [Fact]
    public void Json_each_scales_past_the_per_cell_cap()
    {
        // 5,000 rows × 2 columns = 10,000 cells — far past what a per-cell renderer could bind,
        // and past SQLite's own historical 999-variable limit. json_each binds one parameter.
        var many = Enumerable.Range(0, 5000).Select(i => new Entry($"s{i}", i)).ToList();

        var dialect = new SqliteDialect("Data Source=:memory:");
        var qb = new QueryBuilder(new SqlServerParameterScanner(), dialect.RowSetRenderer);
        qb.BeginCommandScope(1);

        var rs = qb.RowSet("entry", many, map =>
        {
            map.Column("source", x => x.Source);
            map.Column("external_id", x => x.Value);
        });
        qb.Append($"SELECT * FROM {rs}");

        Assert.Single((IDictionary<string, object?>)qb.Parameters);
    }
}
