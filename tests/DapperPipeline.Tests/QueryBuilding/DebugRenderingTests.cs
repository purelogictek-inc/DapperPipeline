using System.Globalization;
using DapperPipeline.Abstractions;
using DapperPipeline.Debugging;
using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.QueryBuilding;
using DapperPipeline.RowSets;
using static DapperPipeline.Interpolation.Sql;

namespace DapperPipeline.Tests.QueryBuilding;

/// <summary>
/// ToDebug() used to emit T-SQL (<c>DECLARE @p AS nvarchar(max)</c> / <c>SET</c>) from the core, so
/// its output was nonsense in psql or the sqlite3 shell. The rendering now belongs to the dialect.
/// </summary>
public sealed class DebugRenderingTests
{
    private static string Debug(IDatabaseDialect dialect)
    {
        var qb = new QueryBuilder(dialect.Scanner, dialect.RowSetRenderer, dialect.DebugRenderer);
        qb.BeginCommandScope(1);

        var orderId = 42L;
        var name = "o'brien";
        qb.Append($"SELECT * FROM Orders WHERE Id = {orderId} AND Name = {name.SqlParam()}");

        return qb.ToDebug();
    }

    [Fact]
    public void SqlServer_still_emits_the_declare_set_preamble_for_ssms()
    {
        var debug = Debug(new SqlServerDialect("Server=.;Database=d;Trusted_Connection=true;"));

        Assert.Contains("DECLARE", debug);
        Assert.Contains("SET", debug);
        Assert.Contains("@p001_OrderId", debug);
    }

    [Theory]
    [MemberData(nameof(NonSqlServerDialects))]
    public void Other_dialects_inline_values_instead_of_emitting_t_sql(IDatabaseDialect dialect)
    {
        var debug = Debug(dialect);

        // DECLARE/SET is SQL Server's; psql and sqlite3 would choke on it.
        Assert.DoesNotContain("DECLARE", debug);
        Assert.DoesNotContain("nvarchar(max)", debug);

        // The values are visible in the SQL, which is the point of a debug rendering.
        Assert.Contains("42", debug);
        Assert.Contains("'o''brien'", debug);   // and correctly escaped
        Assert.DoesNotContain("@p001_", debug);
    }

    public static TheoryData<IDatabaseDialect> NonSqlServerDialects =>
    [
        new SqliteDialect("Data Source=:memory:"),
        new PostgreSqlDialect("Host=h;Database=d;Username=u;Password=p"),
    ];

    [Fact]
    public void Debug_output_is_labelled_as_not_for_execution()
    {
        var debug = Debug(new SqliteDialect("Data Source=:memory:"));

        // Inlining values produces exactly the concatenated SQL this library exists to prevent.
        // It must not be mistaken for something to run.
        Assert.Contains("Do not execute", debug, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inline_renderer_is_culture_invariant()
    {
        // The old renderer formatted decimals with the current culture, so under de-DE a decimal
        // literal became "1,5" — one value silently turning into two arguments.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var rendered = InlineDebugRenderer.Instance.Render(
                "SELECT @p001_Price",
                new Dictionary<string, object?> { ["@p001_Price"] = 1.5m });

            Assert.Contains("1.5", rendered);
            Assert.DoesNotContain("1,5", rendered);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Inline_renderer_does_not_clobber_a_prefix_of_a_longer_name()
    {
        // @p001_Id must not be substituted inside @p001_Id__2 — the loop-ordinal names make this
        // a real collision, not a hypothetical one.
        var rendered = InlineDebugRenderer.Instance.Render(
            "VALUES (@p001_Id), (@p001_Id__2)",
            new Dictionary<string, object?> { ["@p001_Id"] = 1, ["@p001_Id__2"] = 2 });

        Assert.Contains("(1)", rendered);
        Assert.Contains("(2)", rendered);
        Assert.DoesNotContain("__2", rendered);
    }

    [Fact]
    public void Inline_renderer_renders_null_as_sql_null()
    {
        var rendered = InlineDebugRenderer.Instance.Render(
            "WHERE Name = @p001_Name",
            new Dictionary<string, object?> { ["@p001_Name"] = null });

        Assert.Contains("NULL", rendered);
        Assert.DoesNotContain("''", rendered);
    }

    // ------------------------------------------- each dialect speaks its own literals

    private static string RenderWith(IDatabaseDialect dialect, object? value) =>
        dialect.DebugRenderer.Render("SELECT @p001_V", new Dictionary<string, object?> { ["@p001_V"] = value });

    [Fact]
    public void Postgres_renders_booleans_and_arrays_the_way_postgres_wants_them()
    {
        var pg = new PostgreSqlDialect("Host=h;Database=d;Username=u;Password=p");

        // Postgres will not accept 1/0 for a boolean column.
        Assert.Contains("TRUE", RenderWith(pg, true));

        // And it is the one dialect that binds a real array per rowset column (unnest(@a, @b)) —
        // so a rowset's own debug output is unreadable unless arrays render as ARRAY[...].
        var arr = RenderWith(pg, new[] { "espn", "pfr" });
        Assert.Contains("ARRAY['espn', 'pfr']", arr);
    }

    [Fact]
    public void Sqlite_renders_booleans_as_integers_because_that_is_what_it_stores()
    {
        var sqlite = new SqliteDialect("Data Source=:memory:");

        Assert.Contains("1", RenderWith(sqlite, true));
        Assert.DoesNotContain("TRUE", RenderWith(sqlite, true));

        Assert.Contains("x'0A0B'", RenderWith(sqlite, new byte[] { 0x0A, 0x0B }));
    }

    [Fact]
    public void A_custom_dialect_still_gets_readable_debug_output()
    {
        IDatabaseDialect bare = new BareDialect();

        var rendered = bare.DebugRenderer.Render(
            "SELECT @p001_Id",
            new Dictionary<string, object?> { ["@p001_Id"] = 7 });

        Assert.Contains("7", rendered);
    }

    private sealed class BareDialect : IDatabaseDialect
    {
        public System.Data.Common.DbConnection CreateConnection() => throw new NotSupportedException();
        public IParameterScanner Scanner => throw new NotSupportedException();
        public string PipelinePreamble => "";
        public bool ShouldRetry(System.Data.Common.DbException exception) => false;
        public string ExtractErrorCode(System.Data.Common.DbException exception) => "";
    }
}
