using System.Data;
using System.Dynamic;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;

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

    // Shared params from IPipelineState — always empty in v1; reserved for future use
    private IReadOnlySet<string> _sharedParams = new HashSet<string>();

    private int _scopeIndex;

    // -------------------------------------------------------------------------
    // Pipeline lifecycle (called by DapperPipeline, not by commands)
    // -------------------------------------------------------------------------

    void Pipeline.IQueryBuilderInternal.BeginCommandScope(int scopeIndex)
    {
        _scopeIndex = scopeIndex;
        _scopedParams.Clear();
    }

    void Pipeline.IQueryBuilderInternal.SetSharedParams(IReadOnlySet<string> sharedParams) =>
        _sharedParams = sharedParams;

    // Keep internal accessor for tests (InternalsVisibleTo)
    internal void BeginCommandScope(int scopeIndex)
    {
        _scopeIndex = scopeIndex;
        _scopedParams.Clear();
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
        var processed = scanner.Process(command, _scopeIndex, _scopedParams, _sharedParams);
        return UpdateSql(processed, !_insideCte, true);
    }

    public IQueryBuilder Append(string command)
    {
        var processed = scanner.Process(command, _scopeIndex, _scopedParams, _sharedParams);
        return UpdateSql(processed, false, false);
    }

    public IQueryBuilder Replace(string key, object clause)
    {
        _fullSql = _fullSql.Replace($"%%{key.ToUpper()}%%", clause.ToString());
        return this;
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
            Append($"--{description}");

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
