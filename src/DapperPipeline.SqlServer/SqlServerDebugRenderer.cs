using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dapper;

using DapperPipeline.Abstractions;

namespace DapperPipeline.Dialects.SqlServer;

/// <summary>
/// Renders debug output as T-SQL: a <c>DECLARE</c>/<c>SET</c> preamble for every parameter, followed
/// by the SQL — paste-and-run in SSMS.
/// </summary>
/// <remarks>
/// This lived in the core, where it was wrong: <c>DECLARE @p AS nvarchar(max)</c> means nothing to
/// psql or the sqlite3 shell. It belongs to the dialect that actually speaks it. Other engines get
/// the portable <c>InlineDebugRenderer</c>.
/// </remarks>
internal sealed class SqlServerDebugRenderer : ISqlDebugRenderer
{
    public static readonly SqlServerDebugRenderer Instance = new();

    private SqlServerDebugRenderer() { }

    /// <inheritdoc />
    public string Render(string sqlText, IReadOnlyDictionary<string, object?> parameters)
    {
        var sb = new StringBuilder();
        var declarations = new StringBuilder();
        var setters = new StringBuilder();
        var sql = new StringBuilder(sqlText);

        {
            foreach (var par in parameters.Select(ParamToDebug))
            {
                if (par.StartsWith("REPLACE"))
                {
                    var p = par["REPLACE".Length..].Split('=', 2);
                    sql = sql.Replace(p[0], p[1]);
                }
                else
                {
                    var p = par.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries);
                    declarations.AppendLine(p[0]);
                    if (p.Length <= 1) continue;
                    if (p[1].StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
                        foreach (var ln in p.Skip(1)) setters.AppendLine(ln);
                    else
                        setters.AppendLine(p[1]);
                }
            }
        }

        sb.Append(declarations).Append(setters);
        if (sb.Length > 0) sb.AppendLine();
        sb.Append(sql);

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Debug helpers
    // -------------------------------------------------------------------------

    private static bool IsTableType(Type type) =>
        typeof(SqlMapper.ICustomQueryParameter).IsAssignableFrom(type) &&
        type.FullName == "Dapper.TableValuedParameter";

    private static Type? GetListElementType(Type type) =>
        type.GetInterfaces()
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>))
            .Select(t => t.GetGenericArguments()[0])
            .FirstOrDefault();

    private static string ParamToDebug(KeyValuePair<string, object?> kvp)
    {
        var val = kvp.Value;
        var key = kvp.Key;

        if (val == null)
            return $"DECLARE @{key} AS nvarchar(max)\r\nSET @{key} = NULL\r\n";

        var sourceType = val.GetType();
        var innerType = sourceType.IsArray ? sourceType.GetElementType() : GetListElementType(sourceType);

        if (innerType != null)
        {
            var enumeration = ((IEnumerable)val).Cast<object>().ToList();
            if (innerType == typeof(byte))
                return $"REPLACE @{key}=BYTE[{enumeration.Count}]";
            var listVal = string.Join(", ", enumeration.Select(v => ValToDbSafeVal(innerType, v)));
            return $"REPLACE @{key}= ({listVal})";
        }

        var dbType = TypeToDbType(sourceType, val);

        if (IsTableType(sourceType))
        {
            var tableField = sourceType.GetField("table", BindingFlags.Instance | BindingFlags.NonPublic);
            if (tableField?.GetValue(val) is System.Data.DataTable dt)
                return $"DECLARE @{key} AS {dbType}\r\n{ToInsert(dt, $"@{key}")}";
        }
        else if (string.IsNullOrEmpty(dbType))
            return $"-- Unknown! [Type = {sourceType.Name}]";

        return $"DECLARE @{key} AS {dbType}\r\nSET @{key} = {ValToDbSafeVal(sourceType, val)}\r\n";
    }

    private static string TypeToDbType(Type sourceType, object source)
    {
        if (sourceType == typeof(string)) return "nvarchar(max)";
        if (sourceType == typeof(long) || sourceType == typeof(ulong) || sourceType == typeof(uint)) return "bigint";
        if (sourceType.IsEnum || sourceType == typeof(int) || sourceType == typeof(ushort)) return "int";
        if (sourceType == typeof(short) || sourceType == typeof(byte)) return "smallint";
        if (sourceType == typeof(DateTimeOffset)) return "datetimeoffset(7)";
        if (sourceType == typeof(DateTime)) return "datetime";
        if (sourceType == typeof(bool)) return "bit";
        if (sourceType == typeof(decimal)) return "numeric(19,5)";
        if (sourceType == typeof(double)) return "decimal(10,2)";
        if (sourceType == typeof(Guid)) return "uniqueidentifier";
        if (!IsTableType(sourceType)) return "";
        var typeNameField = sourceType.GetField("typeName", BindingFlags.Instance | BindingFlags.NonPublic);
        return typeNameField?.GetValue(source)?.ToString() ?? "";
    }

    private static string ValToDbSafeVal(Type sourceType, object val) => sourceType switch
    {
        _ when sourceType == typeof(string) => $"'{(val as string ?? "").Replace("'", "''")}'",
        _ when sourceType == typeof(char) => $"'{val}'",
        _ when sourceType == typeof(long) => $"{(long)val}",
        _ when sourceType.IsEnum || sourceType == typeof(int) => $"{(int)val}",
        _ when sourceType == typeof(ushort) || sourceType == typeof(uint) || sourceType == typeof(ulong) => $"{(ulong)Convert.ChangeType(val, typeof(ulong))}",
        _ when sourceType == typeof(short) => $"{(short)val}",
        _ when sourceType == typeof(byte) => $"{val}",
        _ when sourceType == typeof(DateTimeOffset) => $"ToDateTimeOffset('{(DateTimeOffset)val:yyyy-MM-dd HH:mm:ss.fff}', '{(DateTimeOffset)val:zzz}')",
        _ when sourceType == typeof(DateTime) => $"'{(DateTime)val:yyyy-MM-dd HH:mm:ss}'",
        _ when sourceType == typeof(bool) => (bool)val ? "1" : "0",
        _ when sourceType == typeof(decimal) => ((decimal)val).ToString("0.#####", CultureInfo.InvariantCulture),
        _ when sourceType == typeof(double) => ((double)val).ToString("0.##", CultureInfo.InvariantCulture),
        _ when val is Guid guid => $"'{guid}'",
        _ => ""
    };

    private static void RenderRow(StringBuilder values, System.Data.DataColumnCollection columns, System.Data.DataRow row)
    {
        values.AppendLine().Append("  (");
        foreach (System.Data.DataColumn column in columns)
        {
            var val = row[column];
            if ((column.AllowDBNull && val == null) || val is DBNull)
                values.Append("null, ");
            else if (column.DataType == typeof(string))
                values.Append($"'{(val as string ?? "").Replace("'", "''")}', ");
            else if (column.DataType == typeof(bool))
                values.Append($"{((bool)val ? 1 : 0)}, ");
            else if (column.DataType == typeof(DateTimeOffset))
                values.Append($"'{val}', ");
            else if (column.DataType == typeof(Guid))
                values.Append($"'{val}', ");
            else
                values.Append($"{val}, ");
        }
        values.Length -= 2;
        values.Append("),");
    }

    private static string ToInsert(System.Data.DataTable source, string tableName)
    {
        if (source.Rows.Count == 0) return "";

        var columns = string.Join(", ", source.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
        var sqlOut = new StringBuilder($"\nINSERT INTO {tableName} ({columns})\nVALUES ");
        var values = new StringBuilder();

        for (var i = 0; i < source.Rows.Count; i++)
        {
            RenderRow(values, source.Columns, source.Rows[i]);
            if (i == 0 || i % 999 != 0) continue;
            if (values.Length > 1) values.Length -= 1;
            sqlOut.Append(values);
            values.Clear();
            sqlOut.Append($"\nINSERT INTO {tableName} ({columns})\nVALUES ");
        }

        if (values.Length > 1) values.Length -= 1;
        sqlOut.Append(values);
        return sqlOut.ToString();
    }
}