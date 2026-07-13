using System.Data.Common;
using System.Globalization;
using DapperPipeline.Abstractions;
using Microsoft.Data.Sqlite;

namespace DapperPipeline.Dialects.Sqlite;

/// <summary>
/// Database dialect for SQLite.
/// Retry on: SQLITE_BUSY (5), SQLITE_LOCKED (6).
/// Note: SQLite has no table variables — use CTEs instead of <c>DECLARE @Var TABLE</c>.
/// </summary>
public sealed class SqliteDialect : IDatabaseDialect
{
    private readonly string _connectionString;
    private readonly SqliteParameterScanner _scanner = new();

    /// <inheritdoc cref="SqliteDialect"/>
    public SqliteDialect(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public DbConnection CreateConnection() => new SqliteConnection(_connectionString);

    /// <inheritdoc />
    public IParameterScanner Scanner => _scanner;

    /// <summary>
    /// How <c>RowSet(...)</c> is rendered. Defaults to <see cref="SqliteRowSetStrategy.JsonEach"/>,
    /// which binds one parameter for any number of rows.
    /// </summary>
    public SqliteRowSetStrategy RowSetStrategy { get; init; } = SqliteRowSetStrategy.JsonEach;

    /// <inheritdoc />
    public IRowSetRenderer RowSetRenderer => new SqliteRowSetRenderer(RowSetStrategy);

    /// <inheritdoc />
    public string PipelinePreamble => "";

    /// <inheritdoc />
    public bool ShouldRetry(DbException exception) =>
        exception is SqliteException { SqliteErrorCode: 5 or 6 }; // BUSY or LOCKED

    /// <inheritdoc />
    /// <remarks>Returns <c>SqliteException.SqliteErrorCode</c> as text. Match with <c>ErrorRange</c>.</remarks>
    public string ExtractErrorCode(DbException exception) =>
        exception is SqliteException sqliteEx
            ? sqliteEx.SqliteErrorCode.ToString(CultureInfo.InvariantCulture)
            : "";
}