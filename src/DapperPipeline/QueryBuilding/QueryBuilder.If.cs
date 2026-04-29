using DapperPipeline.Abstractions;

namespace DapperPipeline.QueryBuilding;

internal sealed partial class QueryBuilder
{
    private sealed class DecisionBuilder(QueryBuilder source) : IQueryBuilderDecisionBuilder
    {
        private int _clauseCount;
        private Action? _elseAction;

        private string ToStatement(string ifClause) =>
            $"{(_clauseCount == 0 ? ifClause : $"END ELSE {ifClause}")} BEGIN";

        private IQueryBuilderDecisionBuilder Do(string statement, Action<IQueryBuilder> ifBlock)
        {
            source.UpdateSql(statement, false, true).AddIndent();
            ifBlock(source);
            source.RemoveIndent();
            _clauseCount += 1;
            return this;
        }

        public IQueryBuilderDecisionBuilder Clause(string clause, Action<IQueryBuilder>? onMatch) =>
            onMatch == null ? this : Do(ToStatement($"IF ({clause})"), onMatch);

        public IQueryBuilderDecisionBuilder Exists(string clause, Action<IQueryBuilder>? onMatch) =>
            onMatch == null ? this : Do(ToStatement($"IF EXISTS ({clause})"), onMatch);

        public IQueryBuilderDecisionBuilder NotExists(string clause, Action<IQueryBuilder>? onMatch) =>
            onMatch == null ? this : Do(ToStatement($"IF NOT EXISTS ({clause})"), onMatch);

        public IQueryBuilderDecisionBuilder Else(Action<IQueryBuilder>? elseBlock)
        {
            _elseAction = () =>
            {
                if (_clauseCount == 0)
                    throw new InvalidOperationException("Cannot render ELSE condition before IF condition.");
                if (elseBlock != null) Do("END ELSE BEGIN", elseBlock);
            };
            return this;
        }

        public void Dispose()
        {
            _elseAction?.Invoke();
            _elseAction = null;
            if (_clauseCount > 0) source.UpdateSql("END", false, true);
        }
    }
}
