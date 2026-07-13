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