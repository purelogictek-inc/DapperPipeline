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

    /// <summary>
    /// Whether to emit and verify per-command alignment markers on batches that read results.
    /// Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Costs one constant <c>SELECT</c> per reading command, on the round trip the batch was
    /// already making. In exchange the pipeline refuses to hand a command's readers a result set
    /// that belongs to a different command — the failure that otherwise returns another command's
    /// rows silently whenever the shapes happen to be compatible.
    /// </para>
    /// <para>
    /// Turn it off only with a measurement in hand. Batches that register no readers never emit
    /// markers, so write-only paths are unaffected either way.
    /// </para>
    /// </remarks>
    bool VerifyAlignment { set; }
}