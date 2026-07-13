using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.ErrorHandling;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static DapperPipeline.Interpolation.Sql;

namespace DapperPipeline.Tests.Integration;

/// <summary>
/// Runs the real pipeline against a real SQL Server. <c>OPENJSON</c> and the TVP strategy had never
/// been executed — only string-compared.
/// </summary>
public sealed class SqlServerIntegrationTests : IDisposable
{
    private readonly string _schema = $"dp{Guid.NewGuid():N}"[..10];
    private readonly bool _available = Db.SqlServerAvailable;

    public SqlServerIntegrationTests()
    {
        if (!_available) return;

        using var conn = new SqlConnection(Db.SqlServerConnection);
        conn.Open();
        Db.Exec(conn, $"EXEC('CREATE SCHEMA {_schema}')");
        Db.Exec(conn, $"""
            CREATE TABLE {_schema}.team_version (id bigint PRIMARY KEY, is_active bit NOT NULL);
            INSERT INTO {_schema}.team_version VALUES (1, 1);
            CREATE TABLE {_schema}.external_id (
                source nvarchar(100) NOT NULL,
                external_id int NOT NULL,
                is_primary bit NOT NULL,
                CONSTRAINT uq_{_schema} UNIQUE (source, external_id)
            );
            """);
        // A user-defined table type, so the TVP strategy can be exercised for real.
        Db.Exec(conn, $"""
            CREATE TYPE {_schema}.EntryType AS TABLE (
                source nvarchar(100),
                external_id int,
                is_primary bit
            );
            """);
    }

    public void Dispose()
    {
        if (!_available) return;
        using var conn = new SqlConnection(Db.SqlServerConnection);
        conn.Open();
        try
        {
            Db.Exec(conn, $"DROP TABLE IF EXISTS {_schema}.external_id; DROP TABLE IF EXISTS {_schema}.team_version;");
            Db.Exec(conn, $"DROP TYPE IF EXISTS {_schema}.EntryType;");
            Db.Exec(conn, $"DROP SCHEMA IF EXISTS {_schema};");
        }
        catch { /* best-effort cleanup */ }
    }

    private sealed record Entry(string Source, int Value, bool IsPrimary);

    private interface IInsertEntriesCommand : IQueryCommand
    {
        IReadOnlyList<Entry> Entries { get; set; }
        string Schema { get; set; }
    }

    private sealed class InsertEntriesCommand : BaseQueryCommand, IInsertEntriesCommand
    {
        public IReadOnlyList<Entry> Entries { get; set; } = [];
        public string Schema { get; set; } = "";

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            var entries = builder.RowSet("entry", Entries, map =>
            {
                map.Column("source", x => x.Source);
                map.Column("external_id", x => x.Value);
                map.Column("is_primary", x => x.IsPrimary);
            });

            builder.Append($"""
                INSERT INTO {Identifier(Schema)}.external_id (source, external_id, is_primary)
                SELECT entry.source, entry.external_id, entry.is_primary
                FROM   {entries}
                """);
        }

        public override void Process(IDapperResultProcessor processor) { }
    }

    private long CountRows()
    {
        using var conn = new SqlConnection(Db.SqlServerConnection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {_schema}.external_id";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [SkippableFact]
    public async Task RowSet_openjson_actually_executes()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        var rows = new Entry[] { new("espn", 1, true), new("pfr", 2, false), new("nfl", 3, false) };

        await Db.Pipeline(new SqlServerDialect(Db.SqlServerConnection),
                s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        Assert.Equal(3L, CountRows());
    }

    [SkippableFact]
    public async Task RowSet_openjson_scales_past_the_2100_parameter_cap()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // 3,000 rows × 3 columns = 9,000 cells. OPENJSON binds ONE parameter, so this must sail past
        // SQL Server's 2100-parameter limit — the property the design claims.
        var rows = Enumerable.Range(1, 3000).Select(i => new Entry($"s{i}", i, i % 2 == 0)).ToList();

        await Db.Pipeline(new SqlServerDialect(Db.SqlServerConnection),
                s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        Assert.Equal(3000L, CountRows());
    }

    [SkippableFact]
    public async Task RowSet_tvp_strategy_actually_executes_against_a_real_table_type()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // The TVP path binds a structured parameter against a user-defined table type. It has never
        // been run; a mismatch in column order or type would only surface here.
        var dialect = new SqlServerDialect(Db.SqlServerConnection)
        {
            RowSetStrategy = SqlServerRowSetStrategy.TableValuedParameter,
            RowSetTableType = $"{_schema}.EntryType",
        };

        var rows = new Entry[] { new("espn", 1, true), new("pfr", 2, false) };

        await Db.Pipeline(dialect, s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        Assert.Equal(2L, CountRows());
    }

    // ------------------------------------------------- multi-command batching (#7)

    private interface IDeactivateCommand : IQueryCommand { string Schema { get; set; } }

    private sealed class DeactivateCommand : BaseQueryCommand, IDeactivateCommand
    {
        public string Schema { get; set; } = "";

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            var id = 1L;
            builder.Append($"UPDATE {Identifier(Schema)}.team_version SET is_active = 0 WHERE id = {id}");
        }

        public override void Process(IDapperResultProcessor processor) { }
    }

    [SkippableFact]
    public async Task Two_commands_run_in_one_transaction_on_sql_server()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // Token fusion (@p001_IdINSERT) breaks SQL Server too — #7 was never Postgres-only.
        var rows = new Entry[] { new("espn", 9, true) };

        await Db.Pipeline(new SqlServerDialect(Db.SqlServerConnection), s =>
            {
                s.AddTransient<IDeactivateCommand, DeactivateCommand>();
                s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>();
            })
            .ResolveAndRegister<IDeactivateCommand>(c => c.Schema = _schema)
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        using var conn = new SqlConnection(Db.SqlServerConnection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT is_active FROM {_schema}.team_version WHERE id = 1";
        Assert.False((bool)cmd.ExecuteScalar()!);
        Assert.Equal(1L, CountRows());
    }

    // --------------------------------------------------------- isolation level

    [SkippableFact]
    public async Task Pipeline_runs_against_a_database_with_default_settings()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // The dialect used to default to IsolationLevel.Snapshot, but ALLOW_SNAPSHOT_ISOLATION is
        // OFF on every SQL Server database unless someone turns it on. So the FIRST RunAsync that
        // read a table died with error 3952 for anyone who hadn't. This test's database is left with
        // default settings on purpose — that is the whole point of it.
        var rows = new Entry[] { new("espn", 1, true) };

        await Db.Pipeline(new SqlServerDialect(Db.SqlServerConnection),
                s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        Assert.Equal(1L, CountRows());
    }

    [SkippableFact]
    public void Default_isolation_is_read_committed_not_snapshot()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        IDatabaseDialect dialect = new SqlServerDialect(Db.SqlServerConnection);
        Assert.Equal(System.Data.IsolationLevel.ReadCommitted, dialect.DefaultIsolationLevel);

        // ...but Snapshot remains available for a database that actually allows it.
        IDatabaseDialect snapshot = new SqlServerDialect(Db.SqlServerConnection)
        {
            IsolationLevel = System.Data.IsolationLevel.Snapshot,
        };
        Assert.Equal(System.Data.IsolationLevel.Snapshot, snapshot.DefaultIsolationLevel);
    }

    [SkippableFact]
    public async Task Snapshot_still_works_when_the_database_allows_it()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // Its OWN database. ALLOW_SNAPSHOT_ISOLATION is a database-wide setting whose transition is
        // asynchronous, so toggling it on the shared database would sabotage whichever test ran
        // alongside — which is exactly what it did on the first attempt.
        var db = $"dp_snap_{Guid.NewGuid():N}"[..16];
        var master = Db.SqlServerConnection.Replace("Database=dapperpipeline", "Database=master");

        using (var conn = new SqlConnection(master))
        {
            conn.Open();
            Db.Exec(conn, $"CREATE DATABASE [{db}]");
            Db.Exec(conn, $"ALTER DATABASE [{db}] SET ALLOW_SNAPSHOT_ISOLATION ON");
        }

        var connectionString = Db.SqlServerConnection.Replace("Database=dapperpipeline", $"Database={db}");
        try
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                Db.Exec(conn, $"EXEC('CREATE SCHEMA {_schema}')");
                Db.Exec(conn, $"CREATE TABLE {_schema}.external_id (source nvarchar(100) NOT NULL, external_id int NOT NULL, is_primary bit NOT NULL);");
            }

            // Prove the opt-in path is real end-to-end, not just a property that compiles.
            var dialect = new SqlServerDialect(connectionString)
            {
                IsolationLevel = System.Data.IsolationLevel.Snapshot,
            };

            var rows = new Entry[] { new("espn", 5, true) };
            await Db.Pipeline(dialect, s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
                .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
                .RunAsync(CancellationToken.None);

            using var check = new SqlConnection(connectionString);
            check.Open();
            using var cmd = check.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {_schema}.external_id";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var conn = new SqlConnection(master);
            conn.Open();
            Db.Exec(conn, $"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}];");
        }
    }

    // ------------------------------------------------------------- error mapping

    private sealed class DuplicateException(string code) : Exception($"duplicate: {code}");

    [SkippableFact]
    public async Task Unique_violation_surfaces_the_sql_server_error_number()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        var rows = new Entry[] { new("espn", 1, true) };

        // 2627 = violation of UNIQUE constraint. ExtractErrorCode returns it as text.
        IDapperPipeline Build() => Db.Pipeline(new SqlServerDialect(Db.SqlServerConnection), s =>
        {
            s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>();
            s.AddSingleton<IErrorMapper>(new ErrorRange(2627, 2628, (_, _) => new DuplicateException("2627")));
        });

        await Build()
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DuplicateException>(() => Build()
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None));

        Assert.Contains("2627", ex.Message);
    }
}
