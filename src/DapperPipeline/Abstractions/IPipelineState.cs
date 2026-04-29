namespace DapperPipeline.Abstractions;

/// <summary>
/// Typed shared-state bag attached to a pipeline run.
/// Commands retrieve caller-supplied context (e.g. session, tenant) via <see cref="Require{T}"/>.
/// </summary>
/// <remarks>
/// Set state on the pipeline via <c>SetState&lt;T&gt;(value)</c>.
/// Types are keyed by <c>typeof(T)</c> — register one instance per type per pipeline run.
/// </remarks>
public interface IPipelineState
{
    /// <summary>
    /// Returns the registered value of type <typeparamref name="T"/>.
    /// Throws <see cref="InvalidOperationException"/> if not registered.
    /// </summary>
    T Require<T>() where T : class;

    /// <summary>
    /// Returns the registered value of type <typeparamref name="T"/>, or <c>null</c> if not registered.
    /// </summary>
    T? Get<T>() where T : class;
}