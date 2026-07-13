using System.Data;
using System.Data.Common;
using System.Globalization;
using DapperPipeline.Abstractions;
using Microsoft.Data.SqlClient;

namespace DapperPipeline.Dialects.SqlServer;

/// <summary>
/// Database dialect for SQL Server and Azure SQL.
/// Default isolation level: Snapshot.
/// Retry on: optimistic lock conflict (3960), deadlock (1205), timeout (-2), network error (11).
/// </summary>
public sealed class SqlServerDialect : IDatabaseDialect
{
    private readonly string _connectionString;
    private readonly SqlServerParameterScanner _scanner = new();

    /// <inheritdoc cref="SqlServerDialect"/>
    public SqlServerDialect(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public DbConnection CreateConnection() => new SqlConnection(_connectionString);

    /// <inheritdoc />
    public IParameterScanner Scanner => _scanner;

    /// <summary>
    /// The transaction isolation level for a pipeline run. Defaults to
    /// <see cref="System.Data.IsolationLevel.ReadCommitted"/> — SQL Server's own default, and the
    /// only level guaranteed to work on an unmodified database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <strong>Snapshot is not a safe default</strong>, and used to be one. It requires
    /// <c>ALTER DATABASE … SET ALLOW_SNAPSHOT_ISOLATION ON</c>, which is <strong>OFF by default</strong>
    /// on every SQL Server database — so the first <c>RunAsync</c> that read a table failed with
    /// error 3952 (<em>"Snapshot isolation transaction failed … because snapshot isolation is not
    /// allowed in this database"</em>) for any consumer who had not turned it on. The assumption was
    /// inherited from a codebase that happened to have it enabled.
    /// </para>
    /// <para>If your database <em>does</em> allow snapshot isolation, opt in explicitly:</para>
    /// <code>
    /// services.AddDapperPipeline(new SqlServerDialect(conn)
    /// {
    ///     IsolationLevel = IsolationLevel.Snapshot,   // requires ALLOW_SNAPSHOT_ISOLATION ON
    /// });
    /// </code>
    /// </remarks>
    public IsolationLevel IsolationLevel { get; init; } = IsolationLevel.ReadCommitted;

    /// <inheritdoc />
    public IsolationLevel DefaultIsolationLevel => IsolationLevel;

    /// <inheritdoc />
    /// <remarks>
    /// A <c>DECLARE</c>/<c>SET</c> preamble followed by the SQL — paste-and-run in SSMS. This is the
    /// rendering the core used to emit for <em>every</em> dialect, which made <c>ToDebug()</c>
    /// useless in psql and the sqlite3 shell.
    /// </remarks>
    public ISqlDebugRenderer DebugRenderer => SqlServerDebugRenderer.Instance;

    /// <summary>
    /// How <c>RowSet(...)</c> is rendered. Defaults to <see cref="SqlServerRowSetStrategy.OpenJson"/>,
    /// which binds one parameter for any number of rows and needs nothing installed in the database.
    /// </summary>
    /// <remarks>
    /// This is a deployment concern, not a command concern — your <c>Build</c> methods are identical
    /// whichever strategy is in force.
    /// </remarks>
    public SqlServerRowSetStrategy RowSetStrategy { get; init; } = SqlServerRowSetStrategy.OpenJson;

    /// <summary>
    /// The user-defined table type used when <see cref="RowSetStrategy"/> is
    /// <see cref="SqlServerRowSetStrategy.TableValuedParameter"/> (e.g. <c>dbo.EntryType</c>).
    /// Its columns must match the rowset's, in order.
    /// </summary>
    public string? RowSetTableType { get; init; }

    /// <inheritdoc />
    public IRowSetRenderer RowSetRenderer => new SqlServerRowSetRenderer(RowSetStrategy, RowSetTableType);

    /// <inheritdoc />
    public string PipelinePreamble => "SET NOCOUNT ON;\n";

    /// <inheritdoc />
    public bool ShouldRetry(DbException exception) => exception is SqlException { Number: 3960 or 1205 or -2 or 11 };

    /// <inheritdoc />
    /// <remarks>Returns <c>SqlException.Number</c> as text (e.g. <c>"50001"</c>). Match with <c>ErrorRange</c>.</remarks>
    public string ExtractErrorCode(DbException exception) =>
        exception is SqlException sqlEx
            ? sqlEx.Number.ToString(CultureInfo.InvariantCulture)
            : "";
}