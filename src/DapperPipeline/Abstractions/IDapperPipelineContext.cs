using System.Data;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Per-run pipeline configuration. Apply via <c>pipeline.Context(ctx => { ... })</c>.
/// </summary>
public interface IDapperPipelineContext
{
    /// <summary>SQL command timeout in seconds. <c>null</c> uses the connection default.</summary>
    int? CommandTimeout { set; }

    /// <summary>
    /// Transaction isolation level. Defaults to the dialect's
    /// <c>IDatabaseDialect.DefaultIsolationLevel</c> — ReadCommitted on SQL Server and PostgreSQL,
    /// Serializable on SQLite — because isolation levels are not portable.
    /// </summary>
    IsolationLevel Level { set; }

    /// <summary>Whether to log the compiled SQL at debug level. Defaults to <c>true</c>.</summary>
    bool LogSql { set; }

    /// <summary>
    /// Maximum number of retry attempts for transient errors (deadlock, timeout, network).
    /// Defaults to <c>0</c> (no retries).
    /// </summary>
    int RetryCount { set; }
}