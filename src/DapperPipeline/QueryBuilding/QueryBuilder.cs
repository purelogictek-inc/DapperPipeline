using System.Data;
using System.Dynamic;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using DapperPipeline.Interpolation;

namespace DapperPipeline.QueryBuilding;
using Abstractions;
using Utilities;

internal sealed partial class QueryBuilder(IParameterScanner scanner) : Pipeline.IQueryBuilderInternal
{
    private int _indents;
    private bool _insideCte;
    private StringBuilder _fullSql = new();
    private IDictionary<string, object?> _parameters = new ExpandoObject();

    // Per-command state — reset by BeginCommandScope()
    private readonly HashSet<string> _scopedParams = [];
    private readonly ParamNameRegistry _registry = new();

    // Pipeline-wide value index for cross-command bind-time deduplication.
    // Keyed by parameter value (using the value's own Equals); value is the parameter name (with @ prefix).
    private readonly Dictionary<object, string> _valueIndex = new(new ValueEqualityComparer());

    private int _scopeIndex;

    // -------------------------------------------------------------------------
    // Pipeline lifecycle (called by DapperPipeline, not by commands)
    // -------------------------------------------------------------------------

    void Pipeline.IQueryBuilderInternal.BeginCommandScope(int scopeIndex)
    {
        _scopeIndex = scopeIndex;
        _scopedParams.Clear();
        _registry.Reset();
    }

    // Keep internal accessor for tests (InternalsVisibleTo)
    internal void BeginCommandScope(int scopeIndex)
    {
        _scopeIndex = scopeIndex;
        _scopedParams.Clear();
        _registry.Reset();
    }

    // -------------------------------------------------------------------------
    // IQueryBuilder — parameters
    // -------------------------------------------------------------------------

    public IQueryBuilder Add(string paramName, object? paramValue)
    {
        var name = paramName.TrimStart('@');
        _scopedParams.Add(name);
        _parameters[$"{name}_{_scopeIndex}"] = paramValue;
        return this;
    }

    public IQueryBuilder Add(string command)
    {
        var processed = scanner.Process(command, _scopeIndex, _scopedParams);
        return UpdateSql(processed, !_insideCte, true);
    }

    public IQueryBuilder AppendRaw(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return this;
        return UpdateSql(sql, false, false);
    }

    public IQueryBuilder Replace(string key, object clause)
    {
        _fullSql = _fullSql.Replace($"%%{key.ToUpper()}%%", clause.ToString());
        return this;
    }

    // -------------------------------------------------------------------------
    // Handler-dispatch surface (called by SqlInterpolatedHandler, not by user code)
    // -------------------------------------------------------------------------

    public void AppendScannedLiteral(string literal)
    {
        if (string.IsNullOrEmpty(literal)) return;
        var processed = scanner.Process(literal, _scopeIndex, _scopedParams);
        _fullSql.Append(processed);
    }

    public void AppendIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return;
        _fullSql.Append(identifier);
    }

    public void BindAndEmit(object? value, string callerExpr)
    {
        // Cross-command value dedup — null doesn't dedupe (each null bind gets a fresh name)
        if (value is not null && _valueIndex.TryGetValue(value, out var existing))
        {
            _fullSql.Append(existing);
            return;
        }

        // Auto-name with tier escalation
        foreach (var sanitized in ParamNameSanitizer.GenerateCandidates(callerExpr))
        {
            var fullName = $"@p{_scopeIndex:D3}_{sanitized}";
            if (_registry.TryClaim(fullName, callerExpr))
            {
                if (_parameters.TryGetValue(fullName, out var bound) && !Equals(bound, value))
                    throw new InvalidOperationException(
                        $"Parameter '{fullName}' bound twice with different values for expression '{callerExpr}'.");

                _parameters[fullName] = value;
                if (value is not null) _valueIndex[value] = fullName;
                _fullSql.Append(fullName);
                return;
            }
            // Collision with different expression — try next tier
        }

        throw new InvalidOperationException(
            $"Cannot generate a unique parameter name for '{callerExpr}'; all candidates collide " +
            $"with already-claimed names in this command's scope. Rename the local variable.");
    }

    public void BindShared(object? value, string name)
    {
        var fullName = name.StartsWith('@') ? name : $"@{name}";

        // Lazy materialization — only add to _parameters on first reference
        if (!_parameters.ContainsKey(fullName))
        {
            _parameters[fullName] = value;
            if (value is not null) _valueIndex[value] = fullName;
        }
        else if (value is not null && _parameters.TryGetValue(fullName, out var existing) && !Equals(existing, value))
        {
            throw new InvalidOperationException(
                $"Shared parameter '{fullName}' bound twice with different values. " +
                $"Use distinct names across state POCOs and Bind calls.");
        }

        _fullSql.Append(fullName);
    }

    /// <summary>
    /// Equality comparer for the value-dedup index. Delegates to <c>x.Equals(y)</c>, which gives
    /// value equality for primitives and records, reference equality for plain classes — the
    /// right behavior for each.
    /// </summary>
    private sealed class ValueEqualityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y)
        {
            if (x is null || y is null) return ReferenceEquals(x, y);
            if (x.GetType() != y.GetType()) return false;
            return x.Equals(y);
        }

        public int GetHashCode(object obj) => obj?.GetHashCode() ?? 0;
    }

    // -------------------------------------------------------------------------
    // IQueryBuilder — table-valued parameters
    // -------------------------------------------------------------------------

    public IQueryBuilder MapTable<T>(string paramName, string tableType, IEnumerable<T> source,
        Action<IDataTableMapper<T>> setup)
    {
        var mapper = new ClassToDataTableMapper<T>();
        setup(mapper);
        var table = mapper.Fill(source);
        return Add(paramName, table.AsTableValuedParameter($"dbo.{tableType}"));
    }

    public IQueryBuilder AddTableParam(string paramName, DataTable table)
        => Add(paramName, table);

    public IQueryBuilder AddTableParam<T>(string paramName, IEnumerable<T> values, string columnName = "Id")
    {
        var table = new DataTable();
        table.Columns.Add(columnName, typeof(T));
        foreach (var v in values)
            table.Rows.Add(v);
        return Add(paramName, table);
    }

    // -------------------------------------------------------------------------
    // IQueryBuilder — conditional SQL
    // -------------------------------------------------------------------------

    public IQueryBuilder If(Action<IQueryBuilderDecisionBuilder> builder)
    {
        using var decision = new DecisionBuilder(this);
        builder.Invoke(decision);
        return this;
    }

    public IQueryBuilder If(string clause, Action<IQueryBuilder> ifBlock, Action<IQueryBuilder>? elseBlock = null)
        => IfWithElse($"IF ({clause}) BEGIN", ifBlock, elseBlock);

    public IQueryBuilder IfNotExists(string clause, Action<IQueryBuilder> ifBlock, Action<IQueryBuilder>? elseBlock = null)
        => IfWithElse($"IF NOT EXISTS ({clause}) BEGIN", ifBlock, elseBlock);

    // -------------------------------------------------------------------------
    // IQueryBuilder — WHERE / JOIN
    // -------------------------------------------------------------------------

    public IWhereBuilder Where(Action<IWhereBuilder>? builder = null, bool upCase = false)
    {
        var where = new WhereClauseBuilder(_indents, upCase: upCase);
        builder?.Invoke(where);
        return where;
    }

    public IWhereBuilder Where(IWhereBuilder source, Action<IWhereBuilder>? builder = null, bool upCase = false)
    {
        var where = new WhereClauseBuilder(_indents, source, upCase);
        builder?.Invoke(where);
        return where;
    }

    public IQueryBuilder Where(out IWhereBuilder where, Action<IWhereBuilder>? builder = null, bool upCase = false)
    {
        where = new WhereClauseBuilder(_indents, upCase: upCase);
        builder?.Invoke(where);
        return this;
    }

    public IWhereBuilder Where(IWhereBuilder source, string target, string replacement)
        => new WhereClauseBuilder(_indents, source, target, replacement);

    // -------------------------------------------------------------------------
    // IQueryBuilder — CTE
    // -------------------------------------------------------------------------

    public IQueryBuilder WithCte(string name, Action<IQueryBuilder> cte, string fields = "",
        string description = "", bool first = false, bool terminate = false)
    {
        _insideCte = true;

        if (!description.IsEmpty())
            AppendRaw($"--{description}");

        var with = first ? $"WITH {name}" : name;
        if (fields.IsEmpty())
            UpdateSql($"{with} AS (", false, true);
        else
        {
            fields = fields.Trim().Replace("(", "").Replace(")", "");
            UpdateSql($"{with} ({fields}) AS (", false, true);
        }

        AddIndent();
        cte.Invoke(this);
        RemoveIndent();
        UpdateSql(terminate ? ")" : "),", false, true);
        _insideCte = false;
        return this;
    }

    // -------------------------------------------------------------------------
    // IQueryBuilder — indentation
    // -------------------------------------------------------------------------

    public IQueryBuilder AddIndent() { _indents++; return this; }
    public IQueryBuilder RemoveIndent() { _indents--; return this; }

    // -------------------------------------------------------------------------
    // IQueryBuilder — inspection / pipeline use
    // -------------------------------------------------------------------------

    public bool HasQuery => _fullSql.Length > 0;
    public bool HasReplaceableStatements => Regex.IsMatch(_fullSql.ToString(), "%%(.*?)%%");
    public string Sql => _fullSql.ToString();
    public object Parameters => _parameters;

    public void Optimize()
    {
        var sql = _fullSql.ToString();
        var existing = _parameters;
        _parameters = new ExpandoObject();
        foreach (var kvp in existing.Where(kvp => sql.Contains(kvp.Key)))
            _parameters[kvp.Key] = kvp.Value;
    }

    public void Clear()
    {
        _indents = 0;
        _insideCte = false;
        _fullSql.Clear();
        _parameters = new ExpandoObject();
        _scopedParams.Clear();
        _registry.Reset();
        _valueIndex.Clear();
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    internal IQueryBuilder UpdateSql(string command, bool includeSemiColon, bool newLine)
    {
        if (command.IsEmpty()) return this;
        var c = command.Trim();
        if (includeSemiColon && !c.EndsWith(";")) c += ";";

        var indent = new string('\t', _indents);
        c = indent + c.Replace("\n", $"\n{indent}");

        if (HasQuery) _fullSql.AppendLine(indent);
        if (newLine)
            _fullSql.AppendLine(c);
        else
            _fullSql.Append(c);
        return this;
    }

    private IQueryBuilder IfWithElse(string statement, Action<IQueryBuilder>? ifBlock, Action<IQueryBuilder>? elseBlock)
    {
        if (ifBlock == null) return this;
        UpdateSql(statement, false, true).AddIndent();
        ifBlock(this);
        if (elseBlock != null)
        {
            RemoveIndent();
            UpdateSql("END ELSE BEGIN", false, true).AddIndent();
            elseBlock(this);
        }
        RemoveIndent();
        return UpdateSql("END", false, true);
    }
}
