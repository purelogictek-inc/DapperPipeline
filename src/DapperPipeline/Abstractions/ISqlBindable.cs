namespace DapperPipeline.Abstractions;

/// <summary>
/// Marker contract for typed domain wrappers that should be bound as a SQL parameter when used
/// in a value position inside an interpolated <c>Append($"...")</c> string.
/// </summary>
/// <remarks>
/// <para>
/// Implement this on consumer-defined typed primitives (e.g. <c>CustomerId</c>,
/// <c>OrderNumber</c>) to opt them into the interpolation handler's allowlist. The handler
/// dispatches any <c>{value}</c> hole whose static type implements <see cref="ISqlBindable"/>
/// to its generic <c>AppendFormatted</c> overload, which auto-parameterizes <see cref="Value"/>
/// and emits a parameter reference.
/// </para>
/// <para>
/// <see cref="Value"/> is what gets stored in the Dapper parameter dictionary. For a
/// <c>CustomerId(long Id)</c> wrapper, <c>Value</c> would typically return <c>Id.ToString()</c>
/// or the underlying typed value. For a <c>WatscoIdentifier</c>-style alphanumeric ID, return
/// the string representation.
/// </para>
/// </remarks>
public interface ISqlBindable
{
    /// <summary>The underlying value to bind as a SQL parameter.</summary>
    string Value { get; }
}
