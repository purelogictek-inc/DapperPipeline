using System.Data;
using Dapper;

namespace DapperPipeline.QueryBuilding;
using Abstractions;
using Utilities;

internal sealed class TableQueryBuilder(string tableType) : IQueryBuilderTableBuilder
{
    private readonly DataTable _table = new();

    /// <remarks>
    /// The type name is used verbatim. It used to be prefixed with <c>dbo.</c>, which made any other
    /// schema unreachable and turned an already-qualified name into <c>dbo.dbo.OrderLineType</c>.
    /// An unqualified name resolves against the connection's default schema, as SQL Server intends.
    /// </remarks>
    public SqlMapper.ICustomQueryParameter ToTableParam() =>
        _table.AsTableValuedParameter(tableType);

    public IQueryBuilderTableBuilder Fill<T>(string columnName, IEnumerable<T> cells)
    {
        cells?.ForEach((c, i) =>
        {
            var needNewRow = _table.Rows.Count <= i;
            var row = needNewRow ? _table.NewRow() : _table.Rows[i];
            row[columnName] = c == null ? DBNull.Value : c;
            if (needNewRow) _table.Rows.Add(row);
        });
        return this;
    }

    public IQueryBuilderTableBuilder Fill<T>(IEnumerable<T> rows, Func<T, object[]> convert)
    {
        foreach (var row in rows)
            _table.Rows.Add(convert(row));
        return this;
    }

    public IQueryBuilderTableBuilder WithColumn<T>(string name, bool makePrimary = false)
    {
        var col = new DataColumn(name, typeof(T));
        _table.Columns.Add(col);
        if (makePrimary)
            _table.PrimaryKey = [.. _table.PrimaryKey, col];
        return this;
    }
}