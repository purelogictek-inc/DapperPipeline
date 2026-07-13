using System.Text;
using DapperPipeline.Abstractions;

namespace DapperPipeline.Dialects.PostgreSql;

/// <summary>
/// Renders a rowset as <c>unnest(@a, @b) AS alias(col_a, col_b)</c> — one parameter per column.
/// </summary>
/// <remarks>
/// <para>
/// Npgsql binds CLR arrays (<c>int[]</c>, <c>string[]</c>, <c>Guid[]</c>, …) natively, so each
/// column travels as a single array parameter and <c>unnest</c> expands them into rows in lockstep.
/// </para>
/// <para>
/// <strong>The row count never touches the parameter budget</strong> — a 50,000-row import binds the
/// same two parameters as a two-row one. That is what makes this safe against PostgreSQL's
/// 65535-parameter cap.
/// </para>
/// </remarks>
internal sealed class PostgreSqlRowSetRenderer : IRowSetRenderer
{
    public static readonly PostgreSqlRowSetRenderer Instance = new();

    private PostgreSqlRowSetRenderer() { }

    /// <inheritdoc />
    public string Render(ISqlRowSet rowSet, IRowSetBindContext bind)
    {
        ArgumentNullException.ThrowIfNull(rowSet);
        ArgumentNullException.ThrowIfNull(bind);

        var columns = rowSet.Columns;
        if (columns.Count == 0)
            throw new InvalidOperationException($"Rowset '{rowSet.Alias}' declares no columns.");

        // unnest() of empty arrays yields zero rows, which is exactly the degenerate case we want —
        // the surrounding SELECT still parses and simply matches nothing. No special-casing needed.
        var sb = new StringBuilder("unnest(");

        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var array = ToTypedArray(columns[i]);
            sb.Append(bind.Bind(array, $"{rowSet.Alias}_{columns[i].Name}"));
        }

        sb.Append(") AS ").Append(rowSet.Alias).Append('(');
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(columns[i].Name);
        }
        sb.Append(')');

        return sb.ToString();
    }

    /// <summary>
    /// Packs a column's values into an array of its actual CLR type (<c>int[]</c>, not
    /// <c>object[]</c>), which is what Npgsql needs to infer the PostgreSQL array type.
    /// </summary>
    private static Array ToTypedArray(RowSetColumn column)
    {
        var array = Array.CreateInstance(column.ClrType, column.Values.Count);
        for (var i = 0; i < column.Values.Count; i++)
            array.SetValue(column.Values[i], i);
        return array;
    }
}
