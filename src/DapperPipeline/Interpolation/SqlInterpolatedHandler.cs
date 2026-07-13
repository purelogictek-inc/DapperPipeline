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
    /// Clause-position: a WHERE builder emits its assembled clause.
    /// </summary>
    /// <remarks>
    /// Emitted verbatim, and deliberately <em>not</em> re-scanned: the clause's values were already
    /// bound as parameters when it was composed, so running it back through the parameter scanner
    /// would reject its own <c>@p001_…</c> names as unknown.
    /// </remarks>
    public void AppendFormatted(IWhereBuilder where)
        => _qb.AppendIdentifier(where.ToString() ?? "");

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

    // ── The enforcement, and why it looks like this ─────────────────────────────────────────────
    //
    // A bare string in a hole has always been a compile error, because no overload above accepts one.
    // But the error the compiler produced on its own was actively misleading:
    //
    //   CS0311: The type 'string' cannot be used as type parameter 'T' ... no implicit reference
    //           conversion from 'string' to 'ISqlIdentifier'.
    //
    // It never mentions Sql.Text — the answer in almost every case — and instead points at
    // ISqlIdentifier, nudging the reader toward Sql.Identifier(...), which emits their input as RAW
    // SQL. The rule was right and the advice was dangerous.
    //
    // These overloads exist purely to be *chosen* by overload resolution and then rejected, so the
    // error is ours. [Obsolete(error: true)] is a hard compile error (CS0619), not a warning — it
    // cannot be silenced with #pragma warning disable, so the guarantee is unchanged. We simply get
    // to say why.

    /// <summary>Never call this. It exists so a bare string fails to compile with a useful message.</summary>
    [Obsolete(
        "A bare string cannot go in a SQL interpolation hole — it is ambiguous, and guessing wrong " +
        "is how injections happen. Say which you mean:  Sql.Text(x) binds it as a VALUE (safe, " +
        "parameterized).  Sql.Identifier(x) emits it as a table/column NAME (validated, raw).",
        error: true)]
    public void AppendFormatted(string value) => throw new NotSupportedException();

    /// <summary>Never call this. It exists so an untyped value fails to compile with a useful message.</summary>
    [Obsolete(
        "An untyped object cannot go in a SQL interpolation hole. Pass a primitive (int, Guid, " +
        "DateTime, ...), a typed ISqlBindable wrapper, or Sql.Text(x) for a string value.",
        error: true)]
    public void AppendFormatted(object value) => throw new NotSupportedException();
}
