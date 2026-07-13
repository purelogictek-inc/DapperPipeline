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
    /// SQL injected at the start of every batched pipeline run, before any command's SQL.
    /// Use for dialect-specific preamble like SQL Server's <c>SET NOCOUNT ON</c>; return an
    /// empty string when no preamble is needed (SQLite, PostgreSQL).
    /// </summary>
    string PipelinePreamble { get; }

    /// <summary>
    /// Returns <c>true</c> if the pipeline should retry the transaction after
    /// <paramref name="exception"/> (e.g. deadlock, optimistic lock conflict, transient timeout).
    /// </summary>
    bool ShouldRetry(DbException exception);

    /// <summary>
    /// Extracts the dialect-specific error code from <paramref name="exception"/>, as text.
    /// Returns an empty string when the exception carries no code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Do not use <c>DbException.ErrorCode</c> — that is an HRESULT, not a SQL error number.
    /// </para>
    /// <para>
    /// The code is a <see cref="string"/> because not every engine numbers its errors. PostgreSQL
    /// uses five-character alphanumeric SQLSTATEs (<c>23505</c>, <c>40P01</c> — note the letter),
    /// which cannot be represented as an <see cref="int"/>.
    /// </para>
    /// <para>
    /// SQL Server: <c>SqlException.Number</c> as text (e.g. <c>"50001"</c> for a business THROW).
    /// SQLite: <c>SqliteException.SqliteErrorCode</c> as text.
    /// PostgreSQL: <c>PostgresException.SqlState</c> (the SQLSTATE).
    /// </para>
    /// <para>
    /// Match numeric codes with <c>ErrorRange</c>; match SQLSTATEs with <c>SqlState</c> or
    /// <c>SqlStateClass</c>.
    /// </para>
    /// </remarks>
    string ExtractErrorCode(DbException exception);

    /// <summary>
    /// Renders <see cref="ISqlRowSet"/>s as a table expression for this engine.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>ValuesRowSetRenderer</c>, a portable <c>SELECT … UNION ALL</c> rendering — so a
    /// custom dialect gets working rowsets without implementing anything. Override it to bind
    /// set-based instead of one parameter per cell (PostgreSQL uses <c>unnest</c>, SQL Server
    /// <c>OPENJSON</c> or a TVP).
    /// </remarks>
    IRowSetRenderer RowSetRenderer => DapperPipeline.RowSets.ValuesRowSetRenderer.Instance;
}
