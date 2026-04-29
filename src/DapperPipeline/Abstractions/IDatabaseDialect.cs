using System.Data.Common;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Encapsulates all database-specific behaviour: connection creation, parameter scanning,
/// retry decisions, and error code extraction.
/// Inject one implementation at startup — commands never reference the dialect directly.
/// </summary>
public interface IDatabaseDialect
{
    /// <summary>Creates and returns an open-able database connection.</summary>
    DbConnection CreateConnection();

    /// <summary>The parameter scanner for this dialect.</summary>
    IParameterScanner Scanner { get; }

    /// <summary>
    /// Returns <c>true</c> if the pipeline should retry the transaction after
    /// <paramref name="exception"/> (e.g. deadlock, optimistic lock conflict, transient timeout).
    /// </summary>
    bool ShouldRetry(DbException exception);

    /// <summary>
    /// Extracts the dialect-specific numeric error code from <paramref name="exception"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Do not use <c>DbException.ErrorCode</c> — that is an HRESULT, not a SQL error number.
    /// </para>
    /// <para>
    /// SQL Server: <c>SqlException.Number</c> (e.g. 50001 for a business THROW error).
    /// SQLite: <c>SqliteException.SqliteErrorCode</c>.
    /// PostgreSQL: returns 0 — use a custom <c>IErrorMapper</c> that casts to <c>NpgsqlException</c>
    /// and inspects <c>SqlState</c> instead.
    /// </para>
    /// </remarks>
    int ExtractErrorCode(DbException exception);
}
