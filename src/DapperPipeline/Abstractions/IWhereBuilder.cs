namespace DapperPipeline.Abstractions;

/// <summary>
/// Fluent builder for SQL <c>WHERE</c> / <c>JOIN</c> clauses.
/// Emits <c>WHERE</c> only if at least one clause was added (<see cref="IsEmpty"/> is false).
/// </summary>
public interface IWhereBuilder
{
    /// <summary>Returns <c>true</c> if no clauses have been added.</summary>
    bool IsEmpty();

    /// <summary>Adds a <c>WHERE</c> / <c>AND</c> clause, or an <c>OR</c> clause if <paramref name="isOr"/> is true.</summary>
    IWhereBuilder Add(string clause, bool isOr = false);

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