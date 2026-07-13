using System.Text;
using DapperPipeline.Abstractions;

namespace DapperPipeline.RowSets;

/// <summary>
/// Portable fallback renderer — emits <c>(SELECT @a AS c1, @b AS c2 UNION ALL SELECT @c, @d) AS alias</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the default for <see cref="IDatabaseDialect.RowSetRenderer"/>, so a custom dialect gets
/// working rowsets with <strong>zero</strong> extra implementation. The <c>SELECT ... UNION ALL</c> shape is
/// used rather than <c>VALUES (…),(…)</c> because the column-alias list on a <c>VALUES</c> derived
/// table (<c>AS t(a, b)</c>) is not portable — SQLite in particular does not accept it.
/// </para>
/// <para>
/// <strong>It binds one parameter per cell</strong> (<c>O(rows × columns)</c>), so it is bounded by
/// <see cref="MaxCells"/> and will throw rather than silently generate a statement the engine
/// rejects at its parameter cap. A dialect that can do better should — see the PostgreSQL
/// (<c>unnest</c>, one parameter per column) and SQL Server (<c>OPENJSON</c>, one parameter total)
/// renderers.
/// </para>
/// </remarks>
public sealed class ValuesRowSetRenderer : IRowSetRenderer
{
    /// <summary>The shared instance — the renderer is stateless.</summary>
    public static readonly ValuesRowSetRenderer Instance = new();

    /// <summary>
    /// The most cells (rows × columns) this renderer will bind. Chosen to stay clear of SQL Server's
    /// 2100-parameter limit, the tightest cap among the supported engines.
    /// </summary>
    public const int MaxCells = 2000;

    private ValuesRowSetRenderer() { }

    /// <inheritdoc />
    public string Render(ISqlRowSet rowSet, IRowSetBindContext bind)
    {
        ArgumentNullException.ThrowIfNull(rowSet);
        ArgumentNullException.ThrowIfNull(bind);

        var columns = rowSet.Columns;
        if (columns.Count == 0)
            throw new InvalidOperationException($"Rowset '{rowSet.Alias}' declares no columns.");

        // An empty rowset still has to be a selectable table of the right shape, or the surrounding
        // SQL (SELECT alias.col FROM ... , {rows}) would not even parse.
        if (rowSet.RowCount == 0)
            return EmptyTable(rowSet);

        var cells = (long)rowSet.RowCount * columns.Count;
        if (cells > MaxCells)
            throw new InvalidOperationException(
                $"Rowset '{rowSet.Alias}' needs {cells} parameters ({rowSet.RowCount} rows × {columns.Count} columns), " +
                $"over this dialect's limit of {MaxCells}. The default rowset renderer binds one parameter per cell. " +
                $"Use a dialect with a set-based renderer (PostgreSQL binds one parameter per column; SQL Server one in total), " +
                $"or insert in batches.");

        var sb = new StringBuilder("(");
        for (var row = 0; row < rowSet.RowCount; row++)
        {
            if (row > 0) sb.Append(" UNION ALL ");
            sb.Append("SELECT ");
            for (var col = 0; col < columns.Count; col++)
            {
                if (col > 0) sb.Append(", ");
                var name = bind.Bind(columns[col].Values[row], $"{rowSet.Alias}_{columns[col].Name}");
                sb.Append(name);
                // Only the first SELECT names the columns; the rest positionally align.
                if (row == 0) sb.Append(" AS ").Append(columns[col].Name);
            }
        }
        sb.Append(") AS ").Append(rowSet.Alias);
        return sb.ToString();
    }

    /// <summary>A zero-row table of the right shape: <c>(SELECT NULL AS c1 WHERE 1 = 0) AS alias</c>.</summary>
    private static string EmptyTable(ISqlRowSet rowSet)
    {
        var sb = new StringBuilder("(SELECT ");
        for (var i = 0; i < rowSet.Columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("NULL AS ").Append(rowSet.Columns[i].Name);
        }
        sb.Append(" WHERE 1 = 0) AS ").Append(rowSet.Alias);
        return sb.ToString();
    }
}
