using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// A retried attempt must still deliver results. <c>IDapperResultProcessor.Readers</c> is
/// destructive — fetching it clears the scopes — so fetching it per attempt inside the Polly
/// retry loop meant the retried attempt saw no readers, took the plain-execute branch, and
/// every <c>OnResult</c> callback was silently skipped. The readers are now snapshotted once
/// per run; this test fails the first attempt at execute time (after the snapshot would have
/// been consumed) and asserts the second attempt still invokes the callback.
/// </summary>
public sealed class RetryReplaysReadersTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public RetryReplaysReadersTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE audit (note TEXT NOT NULL);
            INSERT INTO audit (note) VALUES ('a'), ('b'), ('c');
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private interface ICountAuditCommand : IQueryCommand<long>
    {
        long MinRowId { get; set; }
    }

    private sealed class CountAuditCommand : BaseQueryCommand<long>, ICountAuditCommand
    {
        public long MinRowId { get; set; }

        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT COUNT(*) FROM audit WHERE rowid > {MinRowId}");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<long>(rows => EmitResult(rows.Single()));
    }

    [Fact]
    public async Task First_attempt_fails_at_execute_time_and_the_retried_attempt_still_delivers_results()
    {
        var dialect = new FailFirstExecuteDialect(ConnectionString);
        var services = new ServiceCollection();
        services.AddDapperPipeline(dialect);
        services.AddTransient<ICountAuditCommand, CountAuditCommand>();
        var pipeline = services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();

        long? seen = null;
        await pipeline
            .Context(c => c.RetryCount = 2)
            .ResolveAndRegister<ICountAuditCommand>(c =>
            {
                c.MinRowId = 0;
                c.OnResult(n => seen = n);
            })
            .RunAsync(CancellationToken.None);

        Assert.True(dialect.HasFailedOnce, "the fault was never injected — this test proved nothing");
        Assert.Equal(3L, seen);
    }

    // -------------------------------------------------------------------------
    // Fault injection: a Sqlite dialect whose FIRST ExecuteReader throws SQLITE_BUSY —
    // transient per SqliteDialect.ShouldRetry — and behaves normally afterwards.
    // Failing at execute time matters: a failure at Open() happens before the readers
    // are consumed and never triggered the original defect.
    // -------------------------------------------------------------------------

    private sealed class FailFirstExecuteDialect(string connectionString) : IDatabaseDialect
    {
        private readonly SqliteDialect _inner = new(connectionString);
        private int _failed;

        public bool HasFailedOnce => Volatile.Read(ref _failed) != 0;

        /// <summary>True exactly once: the first caller claims the failure.</summary>
        internal bool ClaimFailure() => Interlocked.CompareExchange(ref _failed, 1, 0) == 0;

        public DbConnection CreateConnection() =>
            new FailingConnection((SqliteConnection)_inner.CreateConnection(), this);

        public IParameterScanner Scanner => _inner.Scanner;
        public ISqlDebugRenderer DebugRenderer => _inner.DebugRenderer;
        public IsolationLevel DefaultIsolationLevel => _inner.DefaultIsolationLevel;
        public IRowSetRenderer RowSetRenderer => _inner.RowSetRenderer;
        public string PipelinePreamble => _inner.PipelinePreamble;
        public bool ShouldRetry(DbException exception) => _inner.ShouldRetry(exception);
        public string ExtractErrorCode(DbException exception) => _inner.ExtractErrorCode(exception);
    }

    private sealed class FailingConnection(SqliteConnection inner, FailFirstExecuteDialect dialect)
        : DbConnection
    {
        [AllowNull]
        public override string ConnectionString
        {
            get => inner.ConnectionString;
            set => inner.ConnectionString = value;
        }

        public override string Database => inner.Database;
        public override string DataSource => inner.DataSource!;
        public override string ServerVersion => inner.ServerVersion;
        public override ConnectionState State => inner.State;

        public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
        public override void Close() => inner.Close();
        public override void Open() => inner.Open();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            inner.BeginTransaction(isolationLevel);

        protected override DbCommand CreateDbCommand() =>
            new FailingCommand(inner.CreateCommand(), dialect);

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class FailingCommand(SqliteCommand inner, FailFirstExecuteDialect dialect) : DbCommand
    {
        [AllowNull]
        public override string CommandText
        {
            get => inner.CommandText;
            set => inner.CommandText = value;
        }

        public override int CommandTimeout
        {
            get => inner.CommandTimeout;
            set => inner.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => inner.CommandType;
            set => inner.CommandType = value;
        }

        public override bool DesignTimeVisible
        {
            get => inner.DesignTimeVisible;
            set => inner.DesignTimeVisible = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => inner.UpdatedRowSource;
            set => inner.UpdatedRowSource = value;
        }

        protected override DbConnection? DbConnection
        {
            get => inner.Connection;
            set => inner.Connection = value is FailingConnection ? inner.Connection : (SqliteConnection?)value;
        }

        protected override DbParameterCollection DbParameterCollection => inner.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => inner.Transaction;
            set => inner.Transaction = (SqliteTransaction?)value;
        }

        public override void Cancel() => inner.Cancel();
        public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
        public override object? ExecuteScalar() => inner.ExecuteScalar();
        public override void Prepare() => inner.Prepare();
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            ThrowTransientOnFirstCall();
            return inner.ExecuteReader(behavior);
        }

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior, CancellationToken cancellationToken)
        {
            ThrowTransientOnFirstCall();
            return await inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
        }

        private void ThrowTransientOnFirstCall()
        {
            if (dialect.ClaimFailure())
                throw new SqliteException("simulated transient failure", 5); // SQLITE_BUSY
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
