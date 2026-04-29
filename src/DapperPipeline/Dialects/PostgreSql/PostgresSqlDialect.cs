using System.Data.Common;
using DapperPipeline.Abstractions;
using Npgsql;

namespace DapperPipeline.Dialects.PostgreSql;

/// <summary>
/// Database dialect for PostgresSQL via Npgsql.
/// Retry on: transient errors as indicated by <see cref="NpgsqlException.IsTransient"/>.
/// <c>ExtractErrorCode</c> returns 0 — use a custom <c>IErrorMapper</c> that inspects
/// <c>PostgresException.SqlState</c> for business error handling.
/// Note: PostgreSQL has no table variables — use CTEs instead of <c>DECLARE @Var TABLE</c>.
/// </summary>
public sealed class PostgresSqlDialect : IDatabaseDialect
{
    private readonly string _connectionString;
    private readonly PostgresSqlParameterScanner _scanner = new();

    /// <inheritdoc cref="PostgresSqlDialect"/>
    public PostgresSqlDialect(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public DbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    /// <inheritdoc />
    public IParameterScanner Scanner => _scanner;

    /// <inheritdoc />
    public bool ShouldRetry(DbException exception) =>
        exception is NpgsqlException { IsTransient: true };

    /// <inheritdoc />
    /// <remarks>
    /// PostgreSQL uses SQLSTATE string codes (e.g. "40P01" for deadlock), not integers.
    /// Returns 0 — implement a custom <c>IErrorMapper</c> casting to <c>PostgresException</c>
    /// and checking <c>SqlState</c> for business error mapping.
    /// </remarks>
    public int ExtractErrorCode(DbException exception) => 0;
}