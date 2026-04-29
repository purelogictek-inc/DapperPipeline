using System.Data;
using Dapper;

namespace DapperPipeline.QueryBuilding;
using Abstractions;
using Utilities;

internal sealed class TableQueryBuilder(string tableType) : IQueryBuilderTableBuilder
{
    private readonly DataTable _table = new();

    public SqlMapper.ICustomQueryParameter ToTableParam() =>
        _table.AsTableValuedParameter($"dbo.{tableType}");

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