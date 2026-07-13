using DapperPipeline.Abstractions;

namespace DapperPipeline.RowSets;

/// <summary>Materialized <see cref="ISqlRowSet"/> produced by <c>IQueryBuilder.RowSet</c>.</summary>
internal sealed class RowSet(string alias, IReadOnlyList<RowSetColumn> columns, int rowCount) : ISqlRowSet
{
    public string Alias { get; } = alias;
    public IReadOnlyList<RowSetColumn> Columns { get; } = columns;
    public int RowCount { get; } = rowCount;
}

/// <summary>Collects column declarations and projects each one over the rows.</summary>
internal sealed class RowSetColumnBuilder<T>(IReadOnlyList<T> rows) : IRowSetColumnBuilder<T>
{
    private readonly List<RowSetColumn> _columns = [];

    public IRowSetColumnBuilder<T> Column<TValue>(string name, Func<T, TValue> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);

        if (_columns.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Rowset column '{name}' declared more than once.");

        // Project eagerly — the rows are captured now, so a lazy source can't shift underneath us.
        var values = new object?[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            values[i] = selector(rows[i]);

        _columns.Add(new RowSetColumn(name, typeof(TValue), values));
        return this;
    }

    public IReadOnlyList<RowSetColumn> Build() => _columns;
}
