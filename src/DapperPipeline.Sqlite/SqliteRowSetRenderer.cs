using System.Text;
using System.Text.Json;
using DapperPipeline.Abstractions;
using DapperPipeline.RowSets;

namespace DapperPipeline.Dialects.Sqlite;

/// <summary>
/// Renders a rowset as <c>json_each(@json)</c> — one parameter, any number of rows.
/// </summary>
/// <remarks>
/// <para>
/// SQLite's JSON functions are the equivalent of SQL Server's <c>OPENJSON</c>: the rows travel as a
/// single JSON string parameter and <c>json_each</c> expands them. Row count therefore never touches
/// the parameter budget (SQLite's cap is 32766 variables since 3.32, 999 before that).
/// </para>
/// <para>
/// JSON is built into SQLite as of 3.38 and is compiled into the <c>e_sqlite3</c> builds that
/// Microsoft.Data.Sqlite ships, so this needs nothing installed. For an unusual build without JSON,
/// fall back with <see cref="SqliteRowSetStrategy.Values"/>.
/// </para>
/// </remarks>
internal sealed class SqliteRowSetRenderer(SqliteRowSetStrategy strategy) : IRowSetRenderer
{
    /// <inheritdoc />
    public string Render(ISqlRowSet rowSet, IRowSetBindContext bind)
    {
        ArgumentNullException.ThrowIfNull(rowSet);
        ArgumentNullException.ThrowIfNull(bind);

        if (rowSet.Columns.Count == 0)
            throw new InvalidOperationException($"Rowset '{rowSet.Alias}' declares no columns.");

        return strategy switch
        {
            SqliteRowSetStrategy.JsonEach => RenderJsonEach(rowSet, bind),
            SqliteRowSetStrategy.Values => ValuesRowSetRenderer.Instance.Render(rowSet, bind),
            _ => throw new InvalidOperationException($"Unknown rowset strategy '{strategy}'."),
        };
    }

    private static string RenderJsonEach(ISqlRowSet rowSet, IRowSetBindContext bind)
    {
        var param = bind.Bind(ToJson(rowSet), $"{rowSet.Alias}_json");

        // json_each yields one row per array element, exposing it as `value`; json_extract pulls the
        // columns back out. SQLite is dynamically typed, so a JSON number arrives as INTEGER/REAL and
        // a JSON bool as 1/0 — no casts needed.
        var sb = new StringBuilder("(SELECT ");
        for (var i = 0; i < rowSet.Columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var col = rowSet.Columns[i];
            sb.Append("json_extract(je.value, '$.").Append(col.Name).Append("') AS ").Append(col.Name);
        }
        sb.Append(" FROM json_each(").Append(param).Append(") AS je) AS ").Append(rowSet.Alias);
        return sb.ToString();
    }

    /// <summary>Serializes the rows as a JSON array of objects.</summary>
    private static string ToJson(ISqlRowSet rowSet)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartArray();
            for (var row = 0; row < rowSet.RowCount; row++)
            {
                w.WriteStartObject();
                foreach (var col in rowSet.Columns)
                {
                    w.WritePropertyName(col.Name);
                    WriteValue(w, col.Values[row]);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case string s: w.WriteStringValue(s); break;
            case bool b: w.WriteBooleanValue(b); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case short sh: w.WriteNumberValue(sh); break;
            case byte by: w.WriteNumberValue(by); break;
            case decimal d: w.WriteNumberValue(d); break;
            case double db: w.WriteNumberValue(db); break;
            case float f: w.WriteNumberValue(f); break;
            case Guid g: w.WriteStringValue(g); break;
            case DateTime dt: w.WriteStringValue(dt); break;
            case DateTimeOffset dto: w.WriteStringValue(dto); break;
            default: w.WriteStringValue(value.ToString()); break;
        }
    }
}
