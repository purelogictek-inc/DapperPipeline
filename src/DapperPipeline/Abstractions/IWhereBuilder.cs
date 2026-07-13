using System.ComponentModel;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Fluent builder for SQL <c>WHERE</c> / <c>JOIN</c> clauses.
/// Emits <c>WHERE</c> only if at least one clause was added (<see cref="IsEmpty"/> is false).
/// </summary>
public interface IWhereBuilder
{
    /// <summary>Returns <c>true</c> if no clauses have been added.</summary>
    bool IsEmpty();

    /// <summary>
    /// Adds a clause <strong>verbatim</strong>, bypassing the interpolation handler and parameter
    /// binding — the raw escape hatch, exactly like <c>IQueryBuilder.AppendRaw</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ This takes a raw <see cref="string"/>, so it offers <strong>no</strong> SQL-injection
    /// protection: <c>Add($"name = '{userInput}'")</c> compiles and injects. Prefer
    /// <c>Append($"...")</c>, which binds values as parameters and rejects raw strings at compile
    /// time. Use this only for SQL that genuinely cannot be parameterized.
    /// </remarks>
    IWhereBuilder Add(string clause, bool isOr = false);

    // -------------------------------------------------------------------------
    // Handler-dispatch surface (called by WhereInterpolatedHandler, not by user code)
    // -------------------------------------------------------------------------

    /// <summary>Routes a literal portion of a clause through the dialect's parameter scanner.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    string ScanClauseLiteral(string literal);

    /// <summary>
    /// Binds a value as a parameter and returns the name to write into the clause. The clause text
    /// is assembled by the handler, so this must not touch the SQL buffer.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    string BindClauseValue(object? value, string callerExpr);

    /// <summary>Clears all clauses.</summary>
    IWhereBuilder Clear();

    /// <summary>Adds a raw JOIN clause (e.g. <c>INNER JOIN</c>, <c>LEFT OUTER JOIN</c>).</summary>
    IWhereBuilder Join(string command, string clause);

    /// <summary>Adds an <c>INNER JOIN <paramref name="clause"/></c>.</summary>
    IWhereBuilder InnerJoin(string clause);

    /// <summary>Adds an <c>INNER JOIN <paramref name="source"/> ON <paramref name="onClause"/></c>.</summary>
    IWhereBuilder InnerJoin(string source, string onClause);

    /// <summary>
    /// Adds an <c>INNER JOIN <paramref name="source"/> ON <paramref name="onClause"/></c>
    /// with additional AND conditions.
    /// </summary>
    IWhereBuilder InnerJoin(string source, string onClause, params string[] extraClauses);

    /// <summary>
    /// Adds a <c>LEFT OUTER JOIN <paramref name="source"/> ON <paramref name="onClause"/></c>
    /// with additional AND conditions.
    /// </summary>
    IWhereBuilder LeftOuterJoin(string source, string onClause, params string[] extraClauses);

    /// <summary>Replaces a token within the accumulated clause text.</summary>
    IWhereBuilder Replace(string target, string replacement);
}