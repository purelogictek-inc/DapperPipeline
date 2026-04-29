namespace DapperPipeline.Abstractions;

/// <summary>
/// Fluent builder for SQL <c>IF / ELSE IF / ELSE</c> blocks.
/// Dispose (via <c>using</c>) to emit the closing <c>END</c>.
/// </summary>
/// <example>
/// <code>
/// using var decision = builder.If("@IsNew = 1", q => q.Add("INSERT INTO Orders ..."));
/// decision.Else(q => q.Add("UPDATE Orders ..."));
/// </code>
/// </example>
public interface IQueryBuilderDecisionBuilder : IDisposable
{
    /// <summary>Adds an <c>IF (<paramref name="clause"/>) BEGIN ... END</c> block.</summary>
    IQueryBuilderDecisionBuilder Clause(string clause, Action<IQueryBuilder> onMatch);

    /// <summary>Adds an <c>IF EXISTS (<paramref name="clause"/>) BEGIN ... END</c> block.</summary>
    IQueryBuilderDecisionBuilder Exists(string clause, Action<IQueryBuilder> onMatch);

    /// <summary>Adds an <c>IF NOT EXISTS (<paramref name="clause"/>) BEGIN ... END</c> block.</summary>
    IQueryBuilderDecisionBuilder NotExists(string clause, Action<IQueryBuilder> onMatch);

    /// <summary>Adds an <c>ELSE BEGIN ... END</c> block.</summary>
    IQueryBuilderDecisionBuilder Else(Action<IQueryBuilder> elseBlock);
}