using DapperPipeline.Abstractions;
using DapperPipeline.Debugging;
using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.Interpolation;
using DapperPipeline.QueryBuilding;
using DapperPipeline.RowSets;

namespace DapperPipeline.Tests.RowSets;

public sealed class RowSetTests
{
    private sealed record Entry(string Source, int ExternalId);

    private static readonly Entry[] Entries =
    [
        new("espn", 1),
        new("pfr", 2),
        new("nfl", 3),
    ];

    /// <summary>Builds SQL with the identical consumer code, varying only the dialect's renderer.</summary>
    private static (string Sql, IDictionary<string, object?> Parameters) BuildWith(IRowSetRenderer renderer)
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), renderer, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);

        // ── This block is byte-for-byte identical on every dialect. That is the whole point. ──
        var entries = qb.RowSet("entry", Entries, map =>
        {
            map.Column("source", x => x.Source);
            map.Column("external_id", x => x.ExternalId);
        });

        qb.Append($"INSERT INTO t (source, external_id) SELECT entry.source, entry.external_id FROM {entries}");
        // ──────────────────────────────────────────────────────────────────────────────────────

        return (qb.Sql, (IDictionary<string, object?>)qb.Parameters);
    }

    [Fact]
    public void Postgres_binds_one_parameter_per_column_not_per_row()
    {
        var (sql, parameters) = BuildWith(PostgresRenderer());

        Assert.Contains("unnest(", sql);
        Assert.Contains("AS entry(source, external_id)", sql);

        // 3 rows × 2 columns, but only 2 parameters — row count does not touch the budget.
        Assert.Equal(2, parameters.Count);

        var arrays = parameters.Values.OfType<Array>().ToList();
        Assert.Equal(2, arrays.Count);
        Assert.All(arrays, a => Assert.Equal(3, a.Length));

        // Typed arrays, not object[] — Npgsql infers the PG array type from these.
        Assert.Contains(arrays, a => a is string[]);
        Assert.Contains(arrays, a => a is int[]);
    }

    [Fact]
    public void SqlServer_openjson_binds_a_single_parameter()
    {
        var (sql, parameters) = BuildWith(new SqlServerRowSetRendererProbe());

        Assert.Contains("OPENJSON(", sql);
        Assert.Contains("source nvarchar(max) '$.source'", sql);
        Assert.Contains("external_id int '$.external_id'", sql);
        Assert.Contains("AS entry", sql);

        Assert.Single(parameters);
        var json = Assert.IsType<string>(parameters.Values.Single());
        Assert.Contains("\"source\":\"espn\"", json);
        Assert.Contains("\"external_id\":3", json);
    }

    [Fact]
    public void Default_renderer_is_portable_and_binds_per_cell()
    {
        var (sql, parameters) = BuildWith(ValuesRowSetRenderer.Instance);

        Assert.Contains("UNION ALL", sql);
        Assert.Contains("AS source", sql);       // first SELECT names the columns
        Assert.Contains("AS entry", sql);
        Assert.Equal(6, parameters.Count);        // 3 rows × 2 columns
    }

    [Fact]
    public void Consumer_sql_is_identical_across_dialects()
    {
        // The literal SQL a consumer writes — everything outside the rendered table expression —
        // must not vary by dialect. Only the {entries} fragment differs.
        foreach (var renderer in new IRowSetRenderer[]
                 { PostgresRenderer(), new SqlServerRowSetRendererProbe(), ValuesRowSetRenderer.Instance })
        {
            var (sql, _) = BuildWith(renderer);
            Assert.StartsWith("INSERT INTO t (source, external_id) SELECT entry.source, entry.external_id FROM ", sql);
            Assert.Contains("entry", sql);
        }
    }

    [Fact]
    public void Empty_rowset_still_renders_a_selectable_table()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);

        var empty = qb.RowSet("entry", Array.Empty<Entry>(), map =>
        {
            map.Column("source", x => x.Source);
            map.Column("external_id", x => x.ExternalId);
        });
        qb.Append($"SELECT entry.source FROM {empty}");

        // Must still parse and simply match nothing — not blow up the surrounding statement.
        Assert.Contains("WHERE 1 = 0", qb.Sql);
        Assert.Contains("AS entry", qb.Sql);
    }

    [Fact]
    public void Default_renderer_refuses_to_exceed_the_parameter_cap()
    {
        var many = Enumerable.Range(0, 1500).Select(i => new Entry($"s{i}", i)).ToList();

        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);
        var rs = qb.RowSet("entry", many, map =>
        {
            map.Column("source", x => x.Source);
            map.Column("external_id", x => x.ExternalId);
        });

        // 1500 × 2 = 3000 cells, past the 2000 cap — must fail loudly rather than emit a statement
        // the engine rejects at its own parameter limit.
        var ex = Assert.Throws<InvalidOperationException>(() => qb.Append($"SELECT * FROM {rs}"));
        Assert.Contains("3000 parameters", ex.Message);
    }

    [Fact]
    public void Rowset_rejects_a_duplicate_column()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);

        Assert.Throws<InvalidOperationException>(() => qb.RowSet("entry", Entries, map =>
        {
            map.Column("source", x => x.Source);
            map.Column("source", x => x.Source);
        }));
    }

    [Fact]
    public void Rowset_rejects_an_unsafe_alias()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);

        // The alias is emitted as raw SQL, so it must be validated like any identifier.
        Assert.ThrowsAny<Exception>(() => qb.RowSet("entry; DROP TABLE t--", Entries, map =>
            map.Column("source", x => x.Source)));
    }

    private static IRowSetRenderer PostgresRenderer() =>
        new PostgreSqlDialect("Host=h;Database=d;Username=u;Password=p").RowSetRenderer;

    /// <summary>The SQL Server renderer is internal to its assembly; reach it through the dialect.</summary>
    private sealed class SqlServerRowSetRendererProbe : IRowSetRenderer
    {
        private readonly IRowSetRenderer _inner =
            new SqlServerDialect("Server=.;Database=d;Trusted_Connection=true;").RowSetRenderer;

        public string Render(ISqlRowSet rowSet, IRowSetBindContext bind) => _inner.Render(rowSet, bind);
    }
}
