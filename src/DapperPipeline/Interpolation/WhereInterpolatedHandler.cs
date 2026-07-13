using System.Runtime.CompilerServices;
using System.Text;
using DapperPipeline.Interpolation;

// Deliberately in the Abstractions namespace so `Append` is in scope wherever IWhereBuilder is —
// the same reason IQueryBuilder's Append lives there. An extension method you have to hunt for is
// an extension method nobody finds; here the compiler's unhelpful suggestion is LINQ's
// Enumerable.Append, which is exactly the trap the WHERE builder used to fall into.
namespace DapperPipeline.Abstractions;

/// <summary>
/// Compile-time-safe interpolation for <c>WHERE</c> / <c>JOIN</c> clauses, mirroring
/// <c>IQueryBuilder.Append($"...")</c>.
/// </summary>
public static class WhereBuilderAppendExtensions
{
    /// <summary>
    /// Appends a clause composed via <c>$"..."</c> interpolation. Values are <strong>bound as
    /// parameters</strong>; a raw <see cref="string"/> in an interpolation hole is a compile error.
    /// </summary>
    /// <param name="where">The clause builder.</param>
    /// <param name="handler">The interpolated clause.</param>
    /// <param name="isOr">Join to the previous clause with <c>OR</c> instead of <c>AND</c>.</param>
    /// <example>
    /// <code>
    /// var where = builder.Where(w =>
    /// {
    ///     w.Append($"o.BranchId = {branchId}");
    ///     if (filter.StatusId.HasValue)
    ///         w.Append($"o.StatusId = {filter.StatusId.Value}");
    /// });
    /// </code>
    /// </example>
    public static IWhereBuilder Append(
        this IWhereBuilder where,
        [InterpolatedStringHandlerArgument(nameof(where))] ref WhereInterpolatedHandler handler,
        bool isOr = false)
        => where.Add(handler.Clause, isOr);
}

/// <summary>
/// Interpolated-string handler for <see cref="WhereBuilderAppendExtensions.Append"/>. Accepts the
/// same allowlist as <c>SqlInterpolatedHandler</c> — and, just as deliberately, has no
/// <c>AppendFormatted(string)</c>, so a raw string cannot be spliced into a clause.
/// </summary>
[InterpolatedStringHandler]
public ref struct WhereInterpolatedHandler
{
    private readonly IWhereBuilder _where;
    private readonly StringBuilder _clause;

    /// <inheritdoc cref="WhereInterpolatedHandler"/>
    public WhereInterpolatedHandler(int literalLength, int formattedCount, IWhereBuilder where)
    {
        _where = where;
        _clause = new StringBuilder(literalLength + formattedCount * 12);
    }

    /// <summary>The assembled clause text, with every value replaced by its parameter name.</summary>
    internal readonly string Clause => _clause.ToString();

    /// <summary>Literal portion — routed through the dialect's parameter scanner.</summary>
    public void AppendLiteral(string s) => _clause.Append(_where.ScanClauseLiteral(s));

    /// <summary>Identifier position: a typed wrapper emits raw text (already validated).</summary>
    public void AppendFormatted<T>(T id) where T : ISqlIdentifier => _clause.Append(id.Value);

    /// <summary>Value position: a typed bindable wrapper (including <c>Sql.Text(...)</c>).</summary>
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string callerExpr = "")
        where T : ISqlBindable
        => _clause.Append(_where.BindClauseValue(value.Value, callerExpr));

    /// <summary>Pipeline-bound named parameter — emits the registered name directly.</summary>
    public void AppendFormatted<T>(BoundParam<T> bp) => _clause.Append(bp.Name);

    // Primitives — bound as parameters, named from the caller expression.
    /// <summary>Value position: primitive bound as a parameter.</summary>
    public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(long v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(short v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(byte v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(decimal v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(double v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(float v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(bool v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(DateTime v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(DateTimeOffset v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int, string)" />
    public void AppendFormatted(Guid v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);

    /// <summary>Value position: nullable primitive bound as a parameter.</summary>
    public void AppendFormatted(int? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(long? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(short? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(byte? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(decimal? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(double? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(float? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(bool? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(DateTime? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(DateTimeOffset? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);
    /// <inheritdoc cref="AppendFormatted(int?, string)" />
    public void AppendFormatted(Guid? v, [CallerArgumentExpression(nameof(v))] string e = "") => Bind(v, e);

    private void Bind(object? value, string callerExpr)
        => _clause.Append(_where.BindClauseValue(value, callerExpr));

    // A raw string or an unboxed value in a clause hole is a compile error — the whole point. These
    // two overloads exist only to be picked by overload resolution and then rejected, so the error
    // is ours rather than the compiler's guess. [Obsolete(error: true)] is a hard error (CS0619) and
    // cannot be silenced with #pragma warning disable, so nothing is weakened. See the longer note in
    // SqlInterpolatedHandler.

    /// <summary>Never call this. It exists so a bare string fails to compile with a useful message.</summary>
    [Obsolete(
        "A bare string cannot go in a WHERE interpolation hole — it is ambiguous, and guessing wrong " +
        "is how injections happen. Say which you mean:  Sql.Text(x) binds it as a VALUE (safe, " +
        "parameterized).  Sql.Identifier(x) emits it as a column NAME (validated, raw).",
        error: true)]
    public void AppendFormatted(string value) => throw new NotSupportedException();

    /// <summary>Never call this. It exists so an untyped value fails to compile with a useful message.</summary>
    [Obsolete(
        "An untyped object cannot go in a WHERE interpolation hole. Pass a primitive (int, Guid, " +
        "DateTime, ...), a typed ISqlBindable wrapper, or Sql.Text(x) for a string value.",
        error: true)]
    public void AppendFormatted(object value) => throw new NotSupportedException();
}
