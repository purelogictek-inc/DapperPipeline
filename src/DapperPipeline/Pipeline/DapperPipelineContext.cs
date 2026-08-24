using System.Data;
using DapperPipeline.Abstractions;
using Microsoft.Extensions.Logging;

namespace DapperPipeline.Pipeline;

internal sealed class DapperPipelineContext(ILogger logger, IsolationLevel defaultLevel)
    : IDapperPipelineContext
{
    private IsolationLevel? _level;

    public int? CommandTimeout { get; set; }
    public bool LogSql { get; set; } = true;
    public int RetryCount { get; set; }
    public bool VerifyAlignment { get; set; } = true;

    /// <summary>
    /// The transaction isolation level. Falls back to the <em>dialect's</em> default rather than a
    /// hardcoded one — Snapshot is SQL Server's, and Microsoft.Data.Sqlite throws when handed it,
    /// so a core default made the pipeline unrunnable on SQLite.
    /// </summary>
    /// <summary>
    /// Did the caller actually ask for a level, or are we falling back to the dialect's? A run with
    /// no transaction cannot honour a requested level, and silently ignoring one is not acceptable.
    /// </summary>
    internal bool LevelWasRequested => _level is not null;

    public IsolationLevel Level
    {
        get => _level ?? defaultLevel;
        set
        {
            if (_level == null || value > _level)
                _level = value;
        }
    }

    internal void Clear()
    {
        _level = null;
        CommandTimeout = null;
        RetryCount = 0;
        LogSql = true;
        VerifyAlignment = true;
    }

    internal void LogSuccess() => logger.LogDebug("SQL batch succeeded. IsolationLevel={Level}", Level);
    internal void LogFailure(Exception ex) => logger.LogError(ex, "SQL batch failed. IsolationLevel={Level}", Level);
    internal void LogRetry(int attempt, int max) => logger.LogInformation("Retry {Attempt} of {Max}", attempt, max);
}