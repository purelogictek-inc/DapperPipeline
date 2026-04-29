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

    /// <summary>Sets the shared parameter names that the scanner should leave unrewritten.</summary>
    void SetSharedParams(IReadOnlySet<string> sharedParams);
}