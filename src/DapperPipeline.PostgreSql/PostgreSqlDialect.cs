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