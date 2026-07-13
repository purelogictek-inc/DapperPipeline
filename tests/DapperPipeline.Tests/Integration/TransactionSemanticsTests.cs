using System.Data;
using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static DapperPipeline.Interpolation.Sql;

namespace DapperPipeline.Tests.Integration;

/// <summary>
/// What does a batch actually do when we DON'T open a transaction?
/// </summary>
/// <remarks>
/// <para>
/// This is the question behind "should PostgreSQL just default to no transaction?" — and it cannot
/// be answered by reading our code, because the answer lives in the wire protocol. PostgreSQL wraps
/// everything up to a <c>Sync</c> message in an <em>implicit</em> transaction, and Npgsql sends one
/// <c>Sync</c> at the end of a batch. If that holds, a multi-statement batch is atomic on Postgres
/// with no <c>BEGIN</c>/<c>COMMIT</c> at all, and skipping them is free.
/// </para>
/// <para>
/// If it does NOT hold, then defaulting Postgres to no transaction silently trades away atomicity.
/// These tests pin whichever it is, per engine, against the real server — so the default is chosen
/// on evidence and a future protocol change breaks a test instead of someone's data.
/// </para>
/// </remarks>
public sealed class TransactionSemanticsTests
{
    /// <summary>Inserts one good row, then a row that violates the UNIQUE constraint.</summary>
    private sealed class TwoInsertsCommand : BaseQueryCommand
    {
        public string Table { get; set; } = "";

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            builder.AppendRaw($"INSERT INTO {Table} (v) VALUES ('good');");
            builder.AppendRaw($"INSERT INTO {Table} (v) VALUES ('dup');");
            builder.AppendRaw($"INSERT INTO {Table} (v) VALUES ('dup');");   // UNIQUE violation
        }

        public override void Process(IDapperResultProcessor processor) { }
    }

    // ── PostgreSQL ──────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Postgres_batch_without_an_explicit_transaction_is_still_atomic()
    {
        Skip.IfNot(Db.PostgresAvailable, "database not reachable — start the container");

        var schema = $"dp_{Guid.NewGuid():N}"[..12];
        await using var conn = new NpgsqlConnection(Db.PostgresConnection);
        await conn.OpenAsync();
        Db.Exec(conn, $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.t (v text UNIQUE);");

        try
        {
            var services = new ServiceCollection();
            services.AddDapperPipeline(new PostgreSqlDialect(Db.PostgresConnection));
            services.AddTransient<TwoInsertsCommand>();
            var pipeline = services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();

            await Assert.ThrowsAnyAsync<Exception>(() => pipeline
                .WithoutTransaction()
                .ResolveAndRegister<TwoInsertsCommand>(c => c.Table = $"{schema}.t")
                .RunAsync(CancellationToken.None));

            // Npgsql sends the whole batch before a single Sync, and PostgreSQL treats everything up
            // to a Sync as one implicit transaction. So the earlier INSERTs are rolled back too,
            // even though WE never issued BEGIN. Atomicity here is free.
            await using var check = new NpgsqlCommand($"SELECT COUNT(*) FROM {schema}.t", conn);
            Assert.Equal(0L, (long)(await check.ExecuteScalarAsync())!);
        }
        finally
        {
            Db.Exec(conn, $"DROP SCHEMA {schema} CASCADE;");
        }
    }

    [SkippableFact]
    public async Task Postgres_defaults_to_no_transaction_and_is_still_atomic()
    {
        Skip.IfNot(Db.PostgresAvailable, "database not reachable — start the container");

        var schema = $"dp_{Guid.NewGuid():N}"[..12];
        await using var conn = new NpgsqlConnection(Db.PostgresConnection);
        await conn.OpenAsync();
        Db.Exec(conn, $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.t (v text UNIQUE);");

        try
        {
            var services = new ServiceCollection();
            services.AddDapperPipeline(new PostgreSqlDialect(Db.PostgresConnection));
            services.AddTransient<TwoInsertsCommand>();
            var pipeline = services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();

            // NOTE: no WithoutTransaction() here. The dialect says PostgreSQL doesn't need one, so
            // the pipeline sends no BEGIN — and the batch is STILL all-or-nothing. That is the whole
            // bet of the dialect default: we drop two round-trips and give up nothing.
            await Assert.ThrowsAnyAsync<Exception>(() => pipeline
                .ResolveAndRegister<TwoInsertsCommand>(c => c.Table = $"{schema}.t")
                .RunAsync(CancellationToken.None));

            await using var check = new NpgsqlCommand($"SELECT COUNT(*) FROM {schema}.t", conn);
            Assert.Equal(0L, (long)(await check.ExecuteScalarAsync())!);
        }
        finally
        {
            Db.Exec(conn, $"DROP SCHEMA {schema} CASCADE;");
        }
    }

    [SkippableFact]
    public async Task Postgres_asking_for_an_isolation_level_without_a_transaction_throws()
    {
        Skip.IfNot(Db.PostgresAvailable, "database not reachable — start the container");

        var services = new ServiceCollection();
        services.AddDapperPipeline(new PostgreSqlDialect(Db.PostgresConnection));
        services.AddTransient<TwoInsertsCommand>();
        var pipeline = services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();

        // An isolation level is a property OF a transaction. Postgres opens none by default, so this
        // level could never be applied — and running at the session default while the caller believes
        // they got Serializable is the exact class of silent bug this library exists to refuse.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline
            .Context(ctx => ctx.Level = IsolationLevel.Serializable)
            .ResolveAndRegister<TwoInsertsCommand>(c => c.Table = "irrelevant")
            .RunAsync(CancellationToken.None));

        Assert.Contains("Serializable", ex.Message);
        Assert.Contains("WithTransaction()", ex.Message);
    }

    [SkippableFact]
    public async Task Postgres_WithTransaction_opts_back_in()
    {
        Skip.IfNot(Db.PostgresAvailable, "database not reachable — start the container");

        var services = new ServiceCollection();
        services.AddDapperPipeline(new PostgreSqlDialect(Db.PostgresConnection));
        services.AddTransient<TwoInsertsCommand>();
        var pipeline = services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();

        // Explicit beats dialect: with a real transaction, Serializable is applicable and the run
        // proceeds to the database (where it fails on the UNIQUE violation, not on our guard).
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => pipeline
            .WithTransaction()
            .Context(ctx => ctx.Level = IsolationLevel.Serializable)
            .ResolveAndRegister<TwoInsertsCommand>(c => c.Table = "no_such_table")
            .RunAsync(CancellationToken.None));

        Assert.IsNotType<InvalidOperationException>(ex);
    }

    // ── SQL Server ──────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task SqlServer_batch_without_an_explicit_transaction_is_NOT_atomic()
    {
        Skip.IfNot(Db.SqlServerAvailable, "database not reachable — start the container");

        var table = $"dbo.t_{Guid.NewGuid():N}"[..14];
        await using var conn = new SqlConnection(Db.SqlServerConnection);
        await conn.OpenAsync();
        Db.Exec(conn, $"CREATE TABLE {table} (v nvarchar(50) UNIQUE);");

        try
        {
            var services = new ServiceCollection();
            services.AddDapperPipeline(new SqlServerDialect(Db.SqlServerConnection));
            services.AddTransient<TwoInsertsCommand>();
            var pipeline = services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();

            await Assert.ThrowsAnyAsync<Exception>(() => pipeline
                .WithoutTransaction()
                .ResolveAndRegister<TwoInsertsCommand>(c => c.Table = table)
                .RunAsync(CancellationToken.None));

            // T-SQL has no implicit batch transaction: each statement commits on its own, so the
            // statements before the failure STAY. This is exactly why WithoutTransaction() must not
            // become a default here.
            await using var check = new SqlCommand($"SELECT COUNT(*) FROM {table}", conn);
            Assert.Equal(2, (int)(await check.ExecuteScalarAsync())!);
        }
        finally
        {
            Db.Exec(conn, $"DROP TABLE {table};");
        }
    }
}
