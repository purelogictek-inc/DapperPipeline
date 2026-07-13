// ReSharper disable once CheckNamespace — deliberately in the Abstractions namespace.
//
// These extensions live in the DapperPipeline.SqlServer *assembly* but the DapperPipeline
// .Abstractions *namespace*, so they are in scope wherever IQueryBuilder is — and nowhere else.
// Referencing the SQL Server package is what makes them exist. A PostgreSQL- or SQLite-only project
// cannot call If(...) at all, which is the point: the SQL they emit is T-SQL and nothing else.
namespace DapperPipeline.Abstractions;

/// <summary>
/// Procedural <c>IF / ELSE</c> blocks — <strong>SQL Server only</strong>.
/// </summary>
/// <remarks>
/// <para>
/// These emit T-SQL control flow (<c>IF (…) BEGIN … END ELSE BEGIN … END</c>), which has no
/// equivalent on PostgreSQL or SQLite:
/// </para>
/// <list type="bullet">
/// <item><description>
/// SQLite has no procedural <c>IF</c> at all — <c>CASE</c> is an expression, not a statement.
/// </description></item>
/// <item><description>
/// PostgreSQL's only option is <c>DO $$ … $$</c>, which takes <strong>no parameters</strong> and
/// <strong>returns void</strong> — so it can neither bind values nor produce a result set for
/// <c>Process</c> to read.
/// </description></item>
/// </list>
/// <para>
/// For portable conditionals, put the condition in a <em>predicate</em> rather than a procedural
/// block — <c>INSERT … SELECT … WHERE NOT EXISTS (…)</c> works on all three engines. And when the
/// condition is known in your app, just branch in C# inside <c>Build</c>: it is portable, and emits
/// less SQL.
/// </para>
/// </remarks>
public static class SqlServerConditionalExtensions
{
    /// <summary>
    /// Emits <c>IF (<paramref name="clause"/>) BEGIN … END</c>, optionally with an <c>ELSE</c>.
    /// <strong>SQL Server only.</strong>
    /// </summary>
    public static IQueryBuilder If(
        this IQueryBuilder builder,
        string clause,
        Action<IQueryBuilder> ifBlock,
        Action<IQueryBuilder>? elseBlock = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(clause);
        ArgumentNullException.ThrowIfNull(ifBlock);

        return builder.If(d =>
        {
            d.Clause(clause, ifBlock);
            if (elseBlock != null) d.Else(elseBlock);
        });
    }

    /// <summary>
    /// Emits <c>IF NOT EXISTS (<paramref name="clause"/>) BEGIN … END</c>, optionally with an
    /// <c>ELSE</c>. <strong>SQL Server only.</strong>
    /// </summary>
    /// <remarks>
    /// The portable form of the common case is a guarded insert, which needs no procedural block:
    /// <code>INSERT INTO t (a) SELECT {a} WHERE NOT EXISTS (SELECT 1 FROM t WHERE k = {k})</code>
    /// </remarks>
    public static IQueryBuilder IfNotExists(
        this IQueryBuilder builder,
        string clause,
        Action<IQueryBuilder> ifBlock,
        Action<IQueryBuilder>? elseBlock = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(clause);
        ArgumentNullException.ThrowIfNull(ifBlock);

        return builder.If(d =>
        {
            d.NotExists(clause, ifBlock);
            if (elseBlock != null) d.Else(elseBlock);
        });
    }

    /// <summary>
    /// Builds a multi-branch <c>IF / ELSE IF / ELSE</c> chain. <strong>SQL Server only.</strong>
    /// </summary>
    public static IQueryBuilder If(this IQueryBuilder builder, Action<ISqlServerDecisionBuilder> decide)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(decide);

        using var decision = new DecisionBuilder(builder);
        decide(decision);
        return builder;
    }

    /// <summary>
    /// Renders the T-SQL chain using only <see cref="IQueryBuilder"/>'s public surface, so it needs
    /// no privileged access to the core builder.
    /// </summary>
    private sealed class DecisionBuilder(IQueryBuilder source) : ISqlServerDecisionBuilder
    {
        private int _clauseCount;
        private Action? _elseAction;

        /// <summary>First branch opens with IF; later ones continue the chain with END ELSE IF.</summary>
        private string ToStatement(string ifClause) =>
            _clauseCount == 0 ? $"{ifClause} BEGIN" : $"END ELSE {ifClause} BEGIN";

        private ISqlServerDecisionBuilder Do(string statement, Action<IQueryBuilder> block)
        {
            source.AppendRaw(statement).AddIndent();
            block(source);
            source.RemoveIndent();
            _clauseCount++;
            return this;
        }

        public ISqlServerDecisionBuilder Clause(string clause, Action<IQueryBuilder>? onMatch) =>
            onMatch == null ? this : Do(ToStatement($"IF ({clause})"), onMatch);

        public ISqlServerDecisionBuilder Exists(string clause, Action<IQueryBuilder>? onMatch) =>
            onMatch == null ? this : Do(ToStatement($"IF EXISTS ({clause})"), onMatch);

        public ISqlServerDecisionBuilder NotExists(string clause, Action<IQueryBuilder>? onMatch) =>
            onMatch == null ? this : Do(ToStatement($"IF NOT EXISTS ({clause})"), onMatch);

        public ISqlServerDecisionBuilder Else(Action<IQueryBuilder>? elseBlock)
        {
            _elseAction = () =>
            {
                if (_clauseCount == 0)
                    throw new InvalidOperationException("Cannot render an ELSE before an IF.");
                if (elseBlock != null) Do("END ELSE BEGIN", elseBlock);
            };
            return this;
        }

        public void Dispose()
        {
            _elseAction?.Invoke();
            _elseAction = null;
            if (_clauseCount > 0) source.AppendRaw("END");
        }
    }
}

/// <summary>
/// Fluent builder for a T-SQL <c>IF / ELSE IF / ELSE</c> chain. <strong>SQL Server only.</strong>
/// Disposing emits the closing <c>END</c>.
/// </summary>
public interface ISqlServerDecisionBuilder : IDisposable
{
    /// <summary>Adds an <c>IF (<paramref name="clause"/>) BEGIN … END</c> branch.</summary>
    ISqlServerDecisionBuilder Clause(string clause, Action<IQueryBuilder> onMatch);

    /// <summary>Adds an <c>IF EXISTS (<paramref name="clause"/>) BEGIN … END</c> branch.</summary>
    ISqlServerDecisionBuilder Exists(string clause, Action<IQueryBuilder> onMatch);

    /// <summary>Adds an <c>IF NOT EXISTS (<paramref name="clause"/>) BEGIN … END</c> branch.</summary>
    ISqlServerDecisionBuilder NotExists(string clause, Action<IQueryBuilder> onMatch);

    /// <summary>Adds the trailing <c>ELSE BEGIN … END</c> branch.</summary>
    ISqlServerDecisionBuilder Else(Action<IQueryBuilder> elseBlock);
}
