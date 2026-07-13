using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.RowSets;

namespace DapperPipeline.Dialects.SqlServer;

/// <summary>
/// Renders a rowset for SQL Server — <c>OPENJSON</c> by default, or a TVP, or portable values.
/// </summary>
/// <remarks>
/// <para>
/// <c>OPENJSON</c> is the default because it binds <strong>one</strong> parameter for any number of
/// rows and requires nothing installed in the database. The rowset's shape is declared inline in the
/// <c>WITH</c> clause, so it needs no user-defined table type — which is what makes the agnostic
/// <c>RowSet</c> API work on SQL Server with zero setup.
/// </para>
/// <para>
/// A TVP is available for consumers who already have table types, or who need TVP throughput on very
/// large sets. It cannot be the default: a TVP requires a matching user-defined table type to exist
/// in the database, and one cannot be conjured for an arbitrary rowset shape at query time.
/// </para>
/// </remarks>
internal sealed class SqlServerRowSetRenderer(SqlServerRowSetStrategy strategy, string? tableType)
    : IRowSetRenderer
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
            SqlServerRowSetStrategy.OpenJson => RenderOpenJson(rowSet, bind),
            SqlServerRowSetStrategy.TableValuedParameter => RenderTvp(rowSet, bind),
            SqlServerRowSetStrategy.Values => ValuesRowSetRenderer.Instance.Render(rowSet, bind),
            _ => throw new InvalidOperationException($"Unknown rowset strategy '{strategy}'."),
        };
    }

    // ---------------------------------------------------------------- OPENJSON

    private static string RenderOpenJson(ISqlRowSet rowSet, IRowSetBindContext bind)
    {
        var json = ToJson(rowSet);
        var param = bind.Bind(json, $"{rowSet.Alias}_json");

        var sb = new StringBuilder("OPENJSON(").Append(param).Append(") WITH (");
        for (var i = 0; i < rowSet.Columns.Count; i++)
        {
            var col = rowSet.Columns[i];
            if (i > 0) sb.Append(", ");
            sb.Append(col.Name).Append(' ').Append(SqlTypeFor(col.ClrType))
              .Append(" '$.").Append(col.Name).Append('\'');
        }
        sb.Append(") AS ").Append(rowSet.Alias);
        return sb.ToString();
    }

    /// <summary>Serializes the rows as a JSON array of objects — <c>[{"source":"espn","id":1}]</c>.</summary>
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

    /// <summary>Maps a CLR type to the SQL type OPENJSON's WITH clause declares.</summary>
    private static string SqlTypeFor(Type clr)
    {
        var t = Nullable.GetUnderlyingType(clr) ?? clr;
        return t switch
        {
            _ when t == typeof(string) => "nvarchar(max)",
            _ when t == typeof(bool) => "bit",
            _ when t == typeof(byte) => "tinyint",
            _ when t == typeof(short) => "smallint",
            _ when t == typeof(int) => "int",
            _ when t == typeof(long) => "bigint",
            _ when t == typeof(decimal) => "decimal(38, 10)",
            _ when t == typeof(double) => "float",
            _ when t == typeof(float) => "real",
            _ when t == typeof(Guid) => "uniqueidentifier",
            _ when t == typeof(DateTime) => "datetime2",
            _ when t == typeof(DateTimeOffset) => "datetimeoffset",
            _ => "nvarchar(max)",
        };
    }

    // --------------------------------------------------------------------- TVP

    /// <remarks>
    /// The user-defined table type must declare the <strong>same columns, in the same order</strong>,
    /// as the rowset — SQL Server matches TVP columns positionally, and the consumer's SQL refers to
    /// them by the rowset's names (<c>entry.source</c>). That coupling is inherent to TVPs and is why
    /// this strategy is opt-in rather than the default.
    /// </remarks>
    private string RenderTvp(ISqlRowSet rowSet, IRowSetBindContext bind)
    {
        if (string.IsNullOrWhiteSpace(tableType))
            throw new InvalidOperationException(
                $"Rowset '{rowSet.Alias}' cannot use the TableValuedParameter strategy: no table type was " +
                $"configured. Set SqlServerDialect.RowSetTableType to a user-defined table type whose columns " +
                $"match ({string.Join(", ", rowSet.Columns.Select(c => c.Name))}), or use the default OpenJson " +
                $"strategy, which needs no database setup.");

        var table = new DataTable();
        foreach (var col in rowSet.Columns)
            table.Columns.Add(col.Name, Nullable.GetUnderlyingType(col.ClrType) ?? col.ClrType);

        for (var row = 0; row < rowSet.RowCount; row++)
        {
            var values = new object?[rowSet.Columns.Count];
            for (var col = 0; col < rowSet.Columns.Count; col++)
                values[col] = rowSet.Columns[col].Values[row] ?? DBNull.Value;
            table.Rows.Add(values);
        }

        // Same binding path the existing MapTable/AddTableParam use — Dapper sends an
        // ICustomQueryParameter as a structured parameter.
        var param = bind.Bind(table.AsTableValuedParameter(tableType), $"{rowSet.Alias}_tvp");
        return $"(SELECT * FROM {param}) AS {rowSet.Alias}";
    }
}
