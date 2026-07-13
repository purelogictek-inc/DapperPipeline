using System.Runtime.CompilerServices;
using DapperPipeline.Abstractions;

namespace DapperPipeline.Interpolation;

/// <summary>
/// Custom <see cref="InterpolatedStringHandlerAttribute"/> for <c>IQueryBuilder.Append($"...")</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hole-by-hole dispatch enforces compile-time SQL injection prevention:
/// </para>
/// <list type="bullet">
/// <item><description>Primitive value types and <see cref="string"/> in literal portions are passed through the parameter scanner via <see cref="IQueryBuilder.AppendScannedLiteral"/>.</description></item>
/// <item><description>Primitive values in interpolation holes are auto-bound as parameters, named from the caller expression.</description></item>
/// <item><description>Types implementing <see cref="ISqlBindable"/> are auto-bound as parameters using their <c>Value</c>.</description></item>
/// <item><description>Types implementing <see cref="ISqlIdentifier"/> are emitted verbatim as raw SQL identifiers.</description></item>
/// <item><description><see cref="BoundParam{T}"/> instances (from <c>state.Bound&lt;T&gt;(name)</c>) emit the registered name directly.</description></item>
/// <item><description><strong>Raw <see cref="string"/> in an interpolation hole is a compile error</strong> — there is no <c>AppendFormatted(string)</c> overload. Wrap with <c>Sql.Identifier(...)</c> for identifier position, or pass the value as a typed parameter.</description></item>
/// </list>
/// </remarks>
[InterpolatedStringHandler]
public ref struct SqlInterpolatedHandler
{
    private readonly IQueryBuilder _qb;

    /// <summary>Constructed by the compiler — not for direct use.</summary>
    public SqlInterpolatedHandler(int literalLength, int formattedCount, IQueryBuilder qb)
    {
        _qb = qb;
    }

    /// <summary>Literal portion between holes — routed through the dialect's parameter scanner.</summary>
    public void AppendLiteral(string s) => _qb.AppendScannedLiteral(s);

    /// <summary>Identifier-position: typed wrapper emits raw text (already validated).</summary>
    public void AppendFormatted<T>(T id) where T : ISqlIdentifier
        => _qb.AppendIdentifier(id.Value);

    /// <summary>
    /// Table-position: a rowset emits a dialect-rendered derived table and binds its own values.
    /// </summary>
    /// <remarks>
    /// Non-generic on purpose. It cannot be a constrained generic like the identifier overload —
    /// constraints are not part of a method signature, so two <c>AppendFormatted&lt;T&gt;(T)</c>
    /// would collide. It is unambiguous anyway: every other generic overload here is constrained to
    /// <see cref="ISqlIdentifier"/> or <see cref="ISqlBindable"/>, so none of them accept a rowset.
    /// </remarks>
    public void AppendFormatted(ISqlRowSet rowSet)
        => _qb.EmitRowSet(rowSet);

    /// <summary>Value-position: typed bindable wrapper, auto-named from caller expression.</summary>
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string callerExpr = "")
        where T : ISqlBindable
        => _qb.BindAndEmit(value.Value, callerExpr);

    /// <summary>Pipeline-bound named parameter — emits the registered name directly.</summary>
    public void AppendFormatted<T>(BoundParam<T> bp)
        => _qb.BindShared(bp.Value, bp.Name);

    /// <summary>Value-position: primitive bound as parameter.</summary>
    public void AppendFormatted(int v,            [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(long v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(short v,          [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(byte v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(decimal v,        [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(double v,         [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(float v,          [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(bool v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(DateTime v,       [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(DateTimeOffset v, [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(Guid v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);

    /// <summary>Value-position: nullable primitive bound as parameter.</summary>
    public void AppendFormatted(int? v,            [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(long? v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(short? v,          [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(byte? v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(decimal? v,        [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(double? v,         [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(float? v,          [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(bool? v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(DateTime? v,       [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(DateTimeOffset? v, [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(Guid? v,           [CallerArgumentExpression(nameof(v))] string callerExpr = "") => _qb.BindAndEmit(v, callerExpr);

    // DELIBERATELY OMITTED:
    //   public void AppendFormatted(string s)      ← compile error for raw string in value position
    //   public void AppendFormatted(object o)      ← compile error for unboxed values
    // To pass a string value, define an ISqlBindable wrapper. To use a raw-string identifier,
    // wrap with Sql.Identifier(...).
}
