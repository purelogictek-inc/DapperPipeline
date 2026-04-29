using System.Data;

namespace DapperPipeline.QueryBuilding;
using Abstractions;
using Utilities;

internal sealed class ClassToDataTableMapper<T> : IDataTableMapper<T>
{
    private readonly Dictionary<string, Func<T, object?>> _mapping = [];
    private readonly DataTable _table = new();
    private long _uniqueId = 1;

    public IDataTableMapper<T> Map<V>(string colName, Func<T, V> getVal, bool? allowNull = null)
    {
        var col = NewColumn(colName, getVal);
        if (allowNull.HasValue) col.AllowDBNull = allowNull.Value;
        return this;
    }

    public IDataTableMapper<T> String(string colName, int maxLength, Func<T, string?> getVal, bool? allowNull = null)
    {
        NewStringColumn(colName, maxLength, getVal, allowNull ?? false);
        return this;
    }

    public IDataTableMapper<T> Primary<V>(string colName, Func<T, V> getVal)
    {
        var col = NewColumn(colName, getVal);
        _table.PrimaryKey = [.. _table.PrimaryKey, col];
        return this;
    }

    public IDataTableMapper<T> Primary(string colName) => Primary(colName, _ => _uniqueId++);

    public IDataTableMapper<T> Primary(string colName, int maxLength, Func<T, string?> getVal)
    {
        var col = NewStringColumn(colName, maxLength, getVal, allowNull: false);
        _table.PrimaryKey = [.. _table.PrimaryKey, col];
        return this;
    }

    internal DataTable Fill(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            var row = _table.NewRow();
            foreach (var colName in _mapping.Keys)
            {
                var val = _mapping[colName](item);
                row[colName] = val ?? DBNull.Value;
            }
            _table.Rows.Add(row);
        }
        return _table;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private DataColumn NewColumn<V>(string colName, Func<T, V> getVal)
    {
        var dbType = typeof(V);
        var nonNullableType = Nullable.GetUnderlyingType(dbType);
        var trueType = nonNullableType ?? dbType;

        // SQL Server has no native bool — store as int (0/1)
        var columnType = trueType == typeof(bool) ? typeof(int) : trueType;

        var col = new DataColumn(colName, columnType)
        {
            AllowDBNull = nonNullableType != null || trueType.IsArray
        };
        _table.Columns.Add(col);

        if (trueType == typeof(bool))
        {
            if (col.AllowDBNull)
                _mapping[colName] = x =>
                {
                    var val = getVal(x);
                    return val != null ? Convert.ToBoolean(val) ? 1 : 0 : null;
                };
            else
                _mapping[colName] = x => Convert.ToBoolean(getVal(x)) ? 1 : 0;
        }
        else
        {
            _mapping[colName] = x => getVal(x);
        }

        return col;
    }

    private DataColumn NewStringColumn(string colName, int maxLength, Func<T, string?> getVal, bool allowNull)
    {
        var col = new DataColumn(colName, typeof(string))
        {
            AllowDBNull = allowNull,
            MaxLength = maxLength
        };
        _table.Columns.Add(col);

        if (allowNull)
            _mapping[colName] = x => getVal(x)?.Truncate(maxLength);
        else
            _mapping[colName] = x => (getVal(x) ?? "").Truncate(maxLength);

        return col;
    }
}