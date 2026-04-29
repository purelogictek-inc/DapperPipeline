namespace DapperPipeline.Interpolation;

/// <summary>
/// Pipeline-bound named scalar — represents a value registered via
/// <c>pipeline.Bind(name, value)</c> or <c>pipeline.SetState&lt;T&gt;</c>'s auto-binding.
/// </summary>
/// <remarks>
/// <para>
/// Surfaces only as the return type of <c>state.Bound&lt;T&gt;(name)</c> for direct use in
/// an interpolation hole. The <see cref="SqlInterpolatedHandler"/>'s
/// <c>AppendFormatted&lt;T&gt;(BoundParam&lt;T&gt;)</c> overload extracts <see cref="Name"/>
/// and emits <c>@{Name}</c>, materializing the parameter on first reference.
/// </para>
/// <para>
/// Do not declare <see cref="BoundParam{T}"/> as a property type on state POCOs —
/// <c>SetState&lt;T&gt;</c> reflects plain scalar properties; wrap-typed properties are not
/// part of the supported model. Library types should not leak into domain code.
/// </para>
/// <para>
/// <see cref="Name"/> is the SQL parameter name <em>without</em> the leading <c>@</c> sigil.
/// </para>
/// </remarks>
public readonly record struct BoundParam<T>(string Name, T Value);
