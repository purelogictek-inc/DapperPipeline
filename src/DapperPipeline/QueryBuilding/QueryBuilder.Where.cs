namespace DapperPipeline.QueryBuilding;
using Abstractions;
using Utilities;

internal sealed partial class QueryBuilder
{
    private sealed record TypedClause(string Command, string Clause)
    {
        public override string ToString() => $"{Command} {Clause}";

        public TypedClause Replace(string target, string replacement) =>
            this with { Clause = Clause.Replace(target, replacement) };
    }

    private sealed class WhereClauseBuilder : IWhereBuilder
    {
        private readonly bool _upCase;
        private readonly string _indent;
        private readonly List<TypedClause> _joins = [];
        private readonly List<TypedClause> _where = [];

        internal WhereClauseBuilder(int indents, bool upCase = false)
        {
            _indent = new string('\t', indents);
            _upCase = upCase;
        }

        internal WhereClauseBuilder(int indents, IWhereBuilder source, bool upCase = false) : this(indents, upCase)
        {
            if (source is not WhereClauseBuilder src) return;
            if (src._joins.Count > 0) _joins.AddRange(src._joins);
            if (src._where.Count > 0) _where.AddRange(src._where);
        }

        internal WhereClauseBuilder(int indents, IWhereBuilder source, string target, string replacement)
        {
            _indent = new string('\t', indents);
            if (source is not WhereClauseBuilder src) return;
            _upCase = src._upCase;
            _joins.AddRange(src._joins.Select(c => c.Replace(target, replacement)));
            _where.AddRange(src._where.Select(c => c.Replace(target, replacement)));
        }

        public bool IsEmpty() => _where.Count == 0 && _joins.Count == 0;

        public IWhereBuilder Add(string clause, bool isOr = false)
        {
            if (clause.IsEmpty()) return this;
            var command = _where.Count > 0
                ? isOr ? "or" : "and"
                : "where";
            if (_upCase) command = command.ToUpper();
            _where.Add(new TypedClause(command, clause));
            return this;
        }

        public IWhereBuilder Clear()
        {
            _joins.Clear();
            _where.Clear();
            return this;
        }

        public IWhereBuilder Join(string command, string clause)
        {
            if (clause.IsEmpty()) return this;
            _joins.Add(_upCase
                ? new TypedClause(command.ToUpper(), clause)
                : new TypedClause(command, clause));
            return this;
        }

        public IWhereBuilder InnerJoin(string clause) => Join("inner join", clause);

        public IWhereBuilder InnerJoin(string source, string onClause)
        {
            onClause = onClause.Trim();
            var on = onClause.StartsWith("ON", StringComparison.OrdinalIgnoreCase)
                ? onClause
                : $"ON {onClause}";
            return Join("inner join", $"{source.Trim()}\n\t{on}");
        }

        public IWhereBuilder InnerJoin(string source, string onClause, params string[] extraClauses)
            => JoinWithExtras("inner join", source, onClause, extraClauses);

        public IWhereBuilder LeftOuterJoin(string source, string onClause, params string[] extraClauses)
            => JoinWithExtras("left outer join", source, onClause, extraClauses);

        public IWhereBuilder Replace(string target, string replacement)
        {
            InlineReplace(_joins);
            InlineReplace(_where);
            return this;

            void InlineReplace(List<TypedClause> list)
            {
                var replaced = list.Select(c => c.Replace(target, replacement)).ToList();
                list.Clear();
                list.AddRange(replaced);
            }
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var clause in _joins.Concat(_where))
                sb.AppendLine(clause.ToString().Replace("\n", $"\n{_indent}"));
            return sb.ToString().Trim();
        }

        private IWhereBuilder JoinWithExtras(string joinType, string source, string onClause, string[] extras)
        {
            onClause = onClause.Trim();
            var sb = new System.Text.StringBuilder();
            if (!onClause.StartsWith("ON", StringComparison.OrdinalIgnoreCase))
                sb.Append("ON ");
            sb.Append(onClause);
            foreach (var extra in extras.Select(e => e.Trim()))
            {
                if (!extra.StartsWith("AND", StringComparison.OrdinalIgnoreCase))
                    sb.Append(" AND ");
                sb.Append(extra);
            }
            return Join(joinType, $"{source.Trim()}\n\t{sb}");
        }
    }
}