using DapperPipeline.Abstractions;

namespace DapperPipeline.Pipeline;

/// <summary>
/// Pipeline-internal extension of <see cref="IQueryBuilder"/> exposing lifecycle hooks
/// that commands must not call directly.
/// </summary>
internal interface IQueryBuilderInternal : IQueryBuilder
{
    /// <summary>Starts a new command scope, resetting per-command parameter state.</summary>
    void BeginCommandScope(int scopeIndex);

    /// <summary>
    /// Ensures the SQL built so far is terminated by <paramref name="separator"/> before the next
    /// command appends to it — without doubling one that is already there.
    /// </summary>
    /// <remarks>
    /// Idempotent on purpose. Before the separator existed, the documented workaround was for
    /// consumers to end every command's SQL with <c>;</c> themselves. Appending unconditionally
    /// would turn that into <c>;;</c> the moment they upgraded, which not every engine accepts —
    /// so a command that already terminates itself gets only a newline.
    /// </remarks>
    void EnsureStatementSeparator(string separator);

    /// <summary>
    /// Pre-registers a named parameter value at pipeline scope (called by
    /// <c>pipeline.Bind</c> and <c>pipeline.SetState</c>'s auto-binding). Adds the value to
    /// both the SQL parameter dictionary and the cross-command value index, so subsequent
    /// primitive interpolation matching the same value reuses the registered name.
    /// </summary>
    void RegisterBinding(string name, object? value);
}