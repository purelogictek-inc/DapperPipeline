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
    /// Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Turn this on to have the pipeline prove, rather than assume, that each command's readers were
    /// handed that command's own result sets. Without it, a command that emits a different number of
    /// row-returning statements than it registers readers shifts every later command's reader onto
    /// somebody else's result set — an exception when the shapes disagree, and <strong>another
    /// command's rows returned silently when they happen to match</strong>.
    /// </para>
    /// <para>
    /// It is off by default because the batch-level checks that always run — readers left starved,
    /// result sets left unconsumed — already catch that mismatch whenever a batch's totals disagree,
    /// which is the common case and costs nothing. Markers close the remaining gap: mismatches that
    /// cancel out across commands, and the difference between catching a crossing after a handler
    /// has already run and refusing to dispatch it at all.
    /// </para>
    /// <para>
    /// Worth turning on when a batch carries several reading commands, when any command emits a
    /// statement that returns rows only conditionally, or while migrating code toward larger
    /// batches. Cost is one constant <c>SELECT</c> per additional reading command — nothing at all
    /// for a single-command run, and about 3% of the round-trip each extra command saves. Batches
    /// that register no readers never emit markers either way.
    /// </para>
    /// </remarks>
    bool VerifyAlignment { set; }
}
