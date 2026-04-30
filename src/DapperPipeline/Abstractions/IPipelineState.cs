using DapperPipeline.Interpolation;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Typed shared-state bag attached to a pipeline run. Commands retrieve caller-supplied context
/// via <see cref="Require{T}"/> (typed POCOs) and named pipeline-bound scalars via
/// <see cref="Bound{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// State is registered on the pipeline via <c>SetState&lt;T&gt;(value)</c> and
/// <c>Bind(name, value)</c>. <c>SetState</c> stores the POCO instance for typed access AND
/// auto-binds its scalar properties under names of the form <c>@TypeNamePropertyName</c>;
/// <c>Bind</c> registers a single named scalar with the exact name given.
/// </para>
/// <para>
/// State POCOs are keyed by <c>typeof(T)</c> — register one instance per type per pipeline run.
/// Bindings are keyed by name (without <c>@</c> sigil).
/// </para>
/// </remarks>
public interface IPipelineState
{
    /// <summary>
    /// Returns the registered POCO of type <typeparamref name="T"/>.
    /// Throws <see cref="InvalidOperationException"/> if not registered.
    /// </summary>
    T Require<T>() where T : class;

    /// <summary>
    /// Returns the registered POCO of type <typeparamref name="T"/>, or <c>null</c> if not registered.
    /// </summary>
    T? Get<T>() where T : class;

    /// <summary>
    /// Returns the named pipeline-bound scalar value as a <see cref="BoundParam{T}"/>, suitable
    /// for direct use in an interpolation hole (<c>builder.Append($"... = {state.Bound&lt;long&gt;(\"X\")}")</c>).
    /// Throws <see cref="InvalidOperationException"/> if no binding with that name exists, or
    /// if the stored value is not assignable to <typeparamref name="T"/>.
    /// </summary>
    BoundParam<T> Bound<T>(string name);
}
