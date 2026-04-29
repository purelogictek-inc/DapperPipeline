using System.Data;
using DapperPipeline.Abstractions;
using Microsoft.Extensions.Logging;

namespace DapperPipeline.Pipeline;

internal sealed class DapperPipelineContext(ILogger logger) : IDapperPipelineContext
{
    private IsolationLevel? _level;

    public int? CommandTimeout { get; set; }
    public bool LogSql { get; set; } = true;
    public int RetryCount { get; set; }

    public IsolationLevel Level
    {
        get => _level ?? IsolationLevel.Snapshot;
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
    }

    internal void LogSuccess() => logger.LogDebug("SQL batch succeeded. IsolationLevel={Level}", Level);
    internal void LogFailure(Exception ex) => logger.LogError(ex, "SQL batch failed. IsolationLevel={Level}", Level);
    internal void LogRetry(int attempt, int max) => logger.LogInformation("Retry {Attempt} of {Max}", attempt, max);
}