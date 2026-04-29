using System.Linq.Expressions;
using System.Text;

namespace DapperPipeline.QueryBuilding;
using Abstractions;
using Utilities;

/// <summary>
/// Fluent builder for SQL <c>INSERT</c> statements.
/// Accumulates column/value pairs and emits either an <c>INSERT INTO ... VALUES</c>
/// or an <c>INSERT INTO ... SELECT</c> statement.
/// </summary>
public sealed class InsertBuilder
{
    private string? _prefix;
    private readonly Dictionary<string, object> _inserts = [];

    /// <summary>Creates a new <see cref="InsertBuilder"/>.</summary>
    public static InsertBuilder Start() => new();

    /// <summary>Returns the accumulated columns and values as separate strings.</summary>
    public (string insert, string values) Normalize()
    {
        var insert = new StringBuilder();
        var values = new StringBuilder();
        foreach (var kvp in _inserts)
        {
            insert.Append($", {kvp.Key}");
            values.Append($", {kvp.Value}");
        }
        if (insert.Length > 0)
        {
            insert.Remove(0, 2);
            values.Remove(0, 2);
        }
        return (insert.ToString(), values.ToString());
    }

    /// <summary>Emits <c>INSERT INTO <paramref name="table"/> (...) SELECT ...</c>.</summary>
    public string SelectInto(string table)
    {
        var (insert, values) = Normalize();
        return $"\ninsert into {table} ({insert})\nselect {values}";
    }

    /// <summary>Emits <c>INSERT INTO <paramref name="table"/> (...) VALUES (...)</c>.</summary>
    public string InsertInto(string table)
    {
        var (insert, values) = Normalize();
        return $"\ninsert into {table} ({insert})\nvalues ({values})";
    }

    /// <summary>
    /// Applies an action against a model, using a column name prefix for all fields added in that action.
    /// </summary>
    public InsertBuilder With<T>(string prefix, T model, Action<InsertBuilder, T> action)
    {
        _prefix = prefix;
        With(model, action);
        _prefix = null;
        return this;
    }

    /// <summary>Applies an action against a model.</summary>
    public InsertBuilder With<T>(T model, Action<InsertBuilder, T> action)
    {
        if (model != null) action(this, model);
        return this;
    }

    /// <summary>Adds a raw literal value column (no parameter binding).</summary>
    public InsertBuilder AddField<T>(string name, T? val, Func<T, string>? formatter = null)
    {
        if (val == null) return this;
        _inserts[CleanName(name)] = formatter == null ? val : formatter(val);
        return this;
    }

    /// <summary>
    /// Registers <paramref name="val"/> as a Dapper parameter on <paramref name="qb"/>
    /// and records the column binding for this builder.
    /// </summary>
    public InsertBuilder AddField<T>(IQueryBuilder qb, string name, T? val)
    {
        if (val == null) return this;
        var colName = CleanName(name);
        qb.Add(colName, val);
        _inserts[colName] = $"@{colName}";
        return this;
    }

    /// <summary>
    /// Registers the value from <paramref name="getVal"/> as a Dapper parameter,
    /// using the property name as the column name.
    /// </summary>
    public InsertBuilder AddField<T>(IQueryBuilder qb, Expression<Func<T>>? getVal)
    {
        if (getVal == null) return this;
        var body = getVal.Body as MemberExpression ??
                   ((UnaryExpression)getVal.Body).Operand as MemberExpression;
        var name = body?.Member.Name
            ?? throw new ArgumentException("Expression must be a member access.", nameof(getVal));
        var val = getVal.Compile().Invoke();
        AddField(qb, name, val);
        return this;
    }

    private string CleanName(string name) =>
        _prefix.IsEmpty() ? name : $"{_prefix}{name}";
}