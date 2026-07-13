using System.Data;
using System.Data.Common;
using DapperPipeline.Abstractions;
using Npgsql;

namespace DapperPipeline.Dialects.PostgreSql;

/// <summary>
/// Database dialect for PostgreSQL via Npgsql.
/// Retry on: transient errors as indicated by <see cref="NpgsqlException.IsTransient"/>.
/// <c>ExtractErrorCode</c> returns the SQLSTATE (e.g. <c>23505</c>, <c>40P01</c>) — match it with
/// <c>SqlState</c> (exact) or <c>SqlStateClass</c> (by class).
/// Note: PostgreSQL has no table variables — use CTEs instead of <c>DECLARE @Var TABLE</c>.
/// </summary>
public sealed class PostgreSqlDialect : IDatabaseDialect
{
    private readonly string _connectionString;
    private readonly PostgreSqlParameterScanner _scanner = new();

    /// <inheritdoc cref="PostgreSqlDialect"/>
    public PostgreSqlDialect(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public DbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    /// <inheritdoc />
    public IParameterScanner Scanner => _scanner;

    /// <inheritdoc />
    /// <remarks>PostgreSQL literals — ARRAY[...] for the arrays a rowset binds, TRUE/FALSE for booleans.</remarks>
    public ISqlDebugRenderer DebugRenderer => PostgreSqlDebugRenderer.Instance;

    /// <inheritdoc />
    /// <remarks>ReadCommitted — PostgreSQL's own default. It has no Snapshot level.</remarks>
    public IsolationLevel DefaultIsolationLevel => IsolationLevel.ReadCommitted;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <c>false</c> — and this is not us trading atomicity for speed. PostgreSQL runs every statement
    /// up to the protocol <c>Sync</c> inside an implicit transaction, and Npgsql sends one <c>Sync</c>
    /// per batch. A failing statement therefore rolls back the ones before it whether or not we sent
    /// <c>BEGIN</c>, so an explicit transaction adds two round-trips (~143 μs) and changes nothing.
    /// </para>
    /// <para>
    /// <c>TransactionSemanticsTests.Postgres_batch_without_an_explicit_transaction_is_still_atomic</c>
    /// proves it against a real server: three INSERTs, the third violating a UNIQUE constraint, and
    /// the table ends up empty. The equivalent test on SQL Server leaves two rows behind — which is
    /// why that dialect keeps the default.
    /// </para>
    /// <para>Need a transaction anyway — to hold locks, say? <c>pipeline.WithTransaction()</c>.</para>
    /// </remarks>
    public bool UseTransactionByDefault => false;

    /// <inheritdoc />
    /// <remarks>
    /// Renders rowsets as <c>unnest(@a, @b)</c> — one array parameter per column, so row count
    /// never consumes the parameter budget.
    /// </remarks>
    public IRowSetRenderer RowSetRenderer => PostgreSqlRowSetRenderer.Instance;

    /// <inheritdoc />
    public string PipelinePreamble => "";

    /// <inheritdoc />
    public bool ShouldRetry(DbException exception) =>
        exception is NpgsqlException { IsTransient: true };

    /// <inheritdoc />
    /// <remarks>
    /// Returns the SQLSTATE (e.g. <c>"23505"</c> unique_violation, <c>"40P01"</c> deadlock_detected).
    /// Match it with <c>SqlState</c> for an exact code, or <c>SqlStateClass</c> for a family
    /// (class <c>"23"</c> = integrity constraint violations).
    /// </remarks>
    public string ExtractErrorCode(DbException exception) =>
        exception is PostgresException pgEx ? pgEx.SqlState : "";
}