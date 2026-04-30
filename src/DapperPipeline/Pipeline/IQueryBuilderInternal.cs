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
    /// Pre-registers a named parameter value at pipeline scope (called by
    /// <c>pipeline.Bind</c> and <c>pipeline.SetState</c>'s auto-binding). Adds the value to
    /// both the SQL parameter dictionary and the cross-command value index, so subsequent
    /// primitive interpolation matching the same value reuses the registered name.
    /// </summary>
    void RegisterBinding(string name, object? value);
}