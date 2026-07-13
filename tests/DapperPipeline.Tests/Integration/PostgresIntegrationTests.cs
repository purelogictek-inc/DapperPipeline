using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.ErrorHandling;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static DapperPipeline.Interpolation.Sql;

namespace DapperPipeline.Tests.Integration;

/// <summary>
/// Runs the real pipeline against a real PostgreSQL server. Everything here was previously
/// "verified" by comparing generated strings, which proves only that we generate what we expect —
/// not that Postgres will accept it.
/// </summary>
public sealed class PostgresIntegrationTests : IDisposable
{
    private readonly string _schema = $"dp_{Guid.NewGuid():N}"[..12];
    private readonly bool _available = Db.PostgresAvailable;

    public PostgresIntegrationTests()
    {
        if (!_available) return;

        using var conn = new NpgsqlConnection(Db.PostgresConnection);
        conn.Open();
        Db.Exec(conn, $"""
            CREATE SCHEMA {_schema};
            CREATE TABLE {_schema}.team_version (id bigint PRIMARY KEY, is_active boolean NOT NULL);
            INSERT INTO {_schema}.team_version VALUES (1, true);
            CREATE TABLE {_schema}.external_id (
                source text NOT NULL,
                external_id int NOT NULL,
                is_primary boolean NOT NULL,
                UNIQUE (source, external_id)
            );
            """);
    }

    public void Dispose()
    {
        if (!_available) return;
        using var conn = new NpgsqlConnection(Db.PostgresConnection);
        conn.Open();
        Db.Exec(conn, $"DROP SCHEMA IF EXISTS {_schema} CASCADE;");
    }

    private PostgreSqlDialect Dialect => new(Db.PostgresConnection);

    private sealed record Entry(string Source, int Value, bool IsPrimary);

    // ------------------------------------------------------------------- rowsets

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

    [SkippableFact]
    public async Task RowSet_unnest_actually_executes_and_inserts_the_rows()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        var rows = new Entry[] { new("espn", 1, true), new("pfr", 2, false), new("nfl", 3, false) };

        await Db.Pipeline(Dialect, s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        using var conn = new NpgsqlConnection(Db.PostgresConnection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT source, external_id, is_primary FROM {_schema}.external_id ORDER BY external_id";
        using var r = cmd.ExecuteReader();

        var got = new List<(string, int, bool)>();
        while (r.Read()) got.Add((r.GetString(0), r.GetInt32(1), r.GetBoolean(2)));

        Assert.Equal([("espn", 1, true), ("pfr", 2, false), ("nfl", 3, false)], got);
    }

    [SkippableFact]
    public async Task RowSet_scales_to_many_rows_on_one_round_trip()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // 5,000 rows. unnest binds one array per column, so this must not approach Postgres'
        // 65535-parameter cap — the property the whole rowset design rests on.
        var rows = Enumerable.Range(1, 5000).Select(i => new Entry($"s{i}", i, i % 2 == 0)).ToList();

        await Db.Pipeline(Dialect, s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        using var conn = new NpgsqlConnection(Db.PostgresConnection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {_schema}.external_id";
        Assert.Equal(5000L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ------------------------------------------------- multi-command batching (#7)

    private interface IDeactivateCommand : IQueryCommand { string Schema { get; set; } }

    private sealed class DeactivateCommand : BaseQueryCommand, IDeactivateCommand
    {
        public string Schema { get; set; } = "";

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            var id = 1L;
            builder.Append($"UPDATE {Identifier(Schema)}.team_version SET is_active = false WHERE id = {id}");
        }

        public override void Process(IDapperResultProcessor processor) { }
    }

    [SkippableFact]
    public async Task Two_commands_run_in_one_transaction_on_postgres()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // #7: without a statement separator these fused into '@p001_IdINSERT' and Postgres rejected
        // the batch. The second command deliberately starts with a keyword.
        var rows = new Entry[] { new("espn", 9, true) };

        await Db.Pipeline(Dialect, s =>
            {
                s.AddTransient<IDeactivateCommand, DeactivateCommand>();
                s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>();
            })
            .ResolveAndRegister<IDeactivateCommand>(c => c.Schema = _schema)
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        using var conn = new NpgsqlConnection(Db.PostgresConnection);
        conn.Open();

        using var a = conn.CreateCommand();
        a.CommandText = $"SELECT is_active FROM {_schema}.team_version WHERE id = 1";
        Assert.False((bool)a.ExecuteScalar()!);

        using var b = conn.CreateCommand();
        b.CommandText = $"SELECT COUNT(*) FROM {_schema}.external_id WHERE source = 'espn'";
        Assert.Equal(1L, Convert.ToInt64(b.ExecuteScalar()));
    }

    // ------------------------------------------------------- SQLSTATE error mapping (#3)

    private sealed class DuplicateException(string state) : Exception($"duplicate: {state}");

    [SkippableFact]
    public async Task Unique_violation_surfaces_its_real_sqlstate_and_maps_to_a_domain_exception()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        // #3: ExtractErrorCode used to return 0 for every Postgres error, so a consumer could not
        // tell a unique violation from a foreign-key one. 23505 must arrive intact.
        var rows = new Entry[] { new("espn", 1, true) };

        IDapperPipeline Build() => Db.Pipeline(Dialect, s =>
        {
            s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>();
            s.AddSingleton<IErrorMapper>(new SqlState("23505", _ => new DuplicateException("23505")));
        });

        await Build()
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        // Same row again — violates the UNIQUE constraint.
        var ex = await Assert.ThrowsAsync<DuplicateException>(() => Build()
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None));

        Assert.Contains("23505", ex.Message);
    }

    // ---------------------------------------------------------- isolation + WHERE

    private interface ISelectCommand : IQueryCommand<int> { string Schema { get; set; } }

    private sealed class SelectCommand : BaseQueryCommand<int>, ISelectCommand
    {
        public string Schema { get; set; } = "";

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            var source = "espn";
            var where = builder.Where(w => w.Append($"source = {source.SqlParam()}"));
            builder.Append($"SELECT external_id FROM {Identifier(Schema)}.external_id {where}");
        }

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<int>(rows => EmitResult(rows.FirstOrDefault()));
    }

    [SkippableFact]
    public async Task Where_builder_and_Text_bind_correctly_against_postgres()
    {
        Skip.IfNot(_available, "database not reachable — start the container");

        var rows = new Entry[] { new("espn", 77, true) };
        await Db.Pipeline(Dialect, s => s.AddTransient<IInsertEntriesCommand, InsertEntriesCommand>())
            .ResolveAndRegister<IInsertEntriesCommand>(c => { c.Entries = rows; c.Schema = _schema; })
            .RunAsync(CancellationToken.None);

        var result = 0;
        await Db.Pipeline(Dialect, s => s.AddTransient<ISelectCommand, SelectCommand>())
            .ResolveAndRegister<ISelectCommand>(c =>
            {
                c.Schema = _schema;
                c.OnResult(v => result = v);
            })
            .RunAsync(CancellationToken.None);

        // Proves the WHERE clause's parameters reached the server in the same command's parameter set.
        Assert.Equal(77, result);
    }
}
