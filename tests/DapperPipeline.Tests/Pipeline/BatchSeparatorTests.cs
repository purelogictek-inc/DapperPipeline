using System.Data;
using System.Data.Common;
using DapperPipeline.Abstractions;
using DapperPipeline.Debugging;
using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.QueryBuilding;
using DapperPipeline.RowSets;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Batching several commands into one round-trip is the library's headline feature. Each command's
/// SQL must be separated — without it, the last token of one command fuses with the first token of
/// the next (<c>@p001_IdWITH</c>), producing a syntax error that points at the wrong statement (#7).
/// </summary>
public sealed class BatchSeparatorTests
{
    /// <summary>Mimics the pipeline's per-command loop, including the separator it must emit.</summary>
    private static string BuildBatch(IDatabaseDialect dialect, params Action<IQueryBuilder>[] commands)
    {
        var qb = new QueryBuilder(dialect.Scanner, dialect.RowSetRenderer, InlineDebugRenderer.Instance);

        var scope = 1;
        foreach (var build in commands)
        {
            if (qb.HasQuery) qb.EnsureStatementSeparator(dialect.StatementSeparator);
            qb.BeginCommandScope(scope++);
            build(qb);
        }
        return qb.Sql;
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_command_that_terminates_its_own_sql_is_not_given_a_second_semicolon(IDatabaseDialect dialect)
    {
        // Before the separator existed, the documented workaround was to end every command with ';'.
        // Anyone who did that must not now get ';;' — not every engine accepts an empty statement.
        var a = 1;
        var b = 2;

        var sql = BuildBatch(dialect,
            qb => qb.Append($"SELECT {a};"),     // already terminated by the consumer
            qb => qb.Append($"SELECT {b}"));

        Assert.DoesNotContain(";;", sql);
        Assert.Matches(@"SELECT @p001_A\s*;\s*SELECT @p002_B", sql);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_self_terminated_command_still_gets_a_break_so_tokens_cannot_fuse(IDatabaseDialect dialect)
    {
        var id = 1L;

        var sql = BuildBatch(dialect,
            qb => qb.Append($"UPDATE t SET x = 0 WHERE id = {id};"),
            qb => qb.AppendRaw("WITH cte AS (SELECT 1) SELECT * FROM cte"));

        Assert.DoesNotContain(";;", sql);
        Assert.DoesNotContain("IdWITH", sql);
        Assert.Contains("WITH cte", sql);
    }

    public static TheoryData<IDatabaseDialect> Dialects =>
    [
        new SqlServerDialect("Server=.;Database=d;Trusted_Connection=true;"),
        new SqliteDialect("Data Source=:memory:"),
        new PostgreSqlDialect("Host=h;Database=d;Username=u;Password=p"),
    ];

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Two_commands_do_not_fuse_into_one_token(IDatabaseDialect dialect)
    {
        var teamVersionId = 1L;

        var sql = BuildBatch(dialect,
            qb => qb.Append($"UPDATE team_version SET is_active = false WHERE id = {teamVersionId}"),
            qb => qb.AppendRaw("WITH new_version AS (SELECT 1) SELECT * FROM new_version"));

        // The exact corruption from #7: the parameter name swallowing the next command's keyword.
        Assert.DoesNotContain("IdWITH", sql);
        Assert.DoesNotContain("TeamVersionIdWITH", sql);

        // The second command's leading keyword must survive as its own token.
        Assert.Contains("WITH new_version", sql);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Commands_are_separated_by_a_statement_terminator(IDatabaseDialect dialect)
    {
        var a = 1;
        var b = 2;

        var sql = BuildBatch(dialect,
            qb => qb.Append($"SELECT {a}"),
            qb => qb.Append($"SELECT {b}"));

        // ';' is valid between statements on all three engines.
        Assert.Contains(";", sql);

        // Whatever the separator, the two statements must not be adjacent with no break at all.
        Assert.DoesNotContain("@p001_ASELECT", sql);
        Assert.Matches(@"SELECT @p001_A\s*;\s*SELECT @p002_B", sql);
    }

    [Fact]
    public void A_single_command_is_not_given_a_leading_separator()
    {
        var id = 7;
        var sql = BuildBatch(new PostgreSqlDialect("Host=h;Database=d;Username=u;Password=p"),
            qb => qb.Append($"SELECT {id}"));

        Assert.StartsWith("SELECT", sql.TrimStart());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Every_dialect_supplies_a_usable_separator(IDatabaseDialect dialect)
    {
        // ';' is the one separator valid on SQL Server, SQLite and PostgreSQL alike.
        Assert.Contains(";", dialect.StatementSeparator);
    }

    // ------------------------------------------------------------ isolation level

    [Fact]
    public void Each_dialect_declares_an_isolation_level_its_own_driver_accepts()
    {
        // Two separate isolation bugs, both found only by running against a real database:
        //  1. The core hardcoded Snapshot; Microsoft.Data.Sqlite throws on it outright.
        //  2. SqlServerDialect then *defaulted* to Snapshot — but ALLOW_SNAPSHOT_ISOLATION is OFF on
        //     every SQL Server database unless someone turns it on, so the first RunAsync that read
        //     a table died with error 3952. ReadCommitted is SQL Server's own default and works.
        Assert.Equal(IsolationLevel.ReadCommitted,
            new SqlServerDialect("Server=.;Database=d;Trusted_Connection=true;").DefaultIsolationLevel);

        // Snapshot stays available, but as an explicit opt-in for a database that allows it.
        Assert.Equal(IsolationLevel.Snapshot,
            ((IDatabaseDialect)new SqlServerDialect("Server=.;Database=d;Trusted_Connection=true;")
                { IsolationLevel = IsolationLevel.Snapshot }).DefaultIsolationLevel);

        Assert.Equal(IsolationLevel.Serializable,
            new SqliteDialect("Data Source=:memory:").DefaultIsolationLevel);

        Assert.Equal(IsolationLevel.ReadCommitted,
            new PostgreSqlDialect("Host=h;Database=d;Username=u;Password=p").DefaultIsolationLevel);
    }

    [Fact]
    public void A_custom_dialect_defaults_to_the_universally_supported_level()
    {
        // A dialect that implements nothing extra must still get a level every engine accepts,
        // and a working statement separator — that is what keeps these additions non-breaking.
        // (Typed as the interface: a default interface member is reached through the interface,
        // not the concrete class — which is exactly how the pipeline holds its dialect.)
        IDatabaseDialect bare = new BareDialect();

        Assert.Equal(IsolationLevel.ReadCommitted, bare.DefaultIsolationLevel);
        Assert.Equal(";\n", bare.StatementSeparator);
    }

    private sealed class BareDialect : IDatabaseDialect
    {
        public DbConnection CreateConnection() => throw new NotSupportedException();
        public IParameterScanner Scanner => throw new NotSupportedException();
        public string PipelinePreamble => "";
        public bool ShouldRetry(DbException exception) => false;
        public string ExtractErrorCode(DbException exception) => "";
    }
}
