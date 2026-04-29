namespace DapperPipeline.Interpolation;

/// <summary>
/// Pipeline-bound named scalar — represents a value registered via
/// <c>pipeline.Bind(name, value)</c> or <c>pipeline.SetState&lt;T&gt;</c>'s auto-binding.
/// </summary>
/// <remarks>
/// <para>
/// Internal type. Consumers never construct or declare <see cref="BoundParam{T}"/> directly —
/// it surfaces only as the return type of <c>state.Bound&lt;T&gt;(name)</c>, where it's passed
/// straight into an interpolation hole. The interpolation handler's
/// <c>AppendFormatted&lt;T&gt;(BoundParam&lt;T&gt;)</c> overload extracts <see cref="Name"/>
/// and emits <c>@{Name}</c> directly, materializing the parameter on first reference.
/// </para>
/// <para>
/// <see cref="Name"/> is the SQL parameter name <em>without</em> the leading <c>@</c> sigil.
/// </para>
/// </remarks>
internal readonly record struct BoundParam<T>(string Name, T Value);

/// <summary>Non-generic facade over <see cref="BoundParam{T}"/> for the bindings registry.</summary>
internal readonly record struct BoundParam(string Name, object? Value);
