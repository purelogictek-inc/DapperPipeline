using System.Collections;
using System.Globalization;
using System.Text;
using DapperPipeline.Abstractions;

namespace DapperPipeline.Debugging;

/// <summary>
/// Base for debug renderers that substitute each parameter into the SQL as a literal, producing
/// something you can paste straight into a client.
/// </summary>
/// <remarks>
/// <para>
/// The substitution itself is shared here because it has a trap in it — see <see cref="Render"/> —
/// while the <em>literal syntax</em> genuinely differs by engine (PostgreSQL wants <c>TRUE</c> and
/// <c>ARRAY[…]</c>; SQLite wants <c>1</c> and <c>x'…'</c>). Dialects override
/// <see cref="Literal"/> and nothing else.
/// </para>
/// <para>
/// ⚠️ Output is <strong>for reading, not for running</strong>. It is deliberately the concatenated
/// SQL this library exists to prevent; the pipeline executes the parameterized form instead.
/// </para>
/// </remarks>
public abstract class InlineDebugRendererBase : ISqlDebugRenderer
{
    /// <inheritdoc />
    public string Render(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(parameters);

        // Longest name first. @p001_Id is a prefix of @p001_Id__2, and loop-ordinal names make that
        // collision routine — substituting the short one first would corrupt the long one.
        var ordered = parameters.OrderByDescending(p => p.Key.Length);

        var sb = new StringBuilder(sql);
        foreach (var (name, value) in ordered)
            sb.Replace(name, Literal(value));

        return "-- Debug rendering: values inlined. Do not execute; the pipeline runs the parameterized form.\n"
             + sb;
    }

    /// <summary>Renders a value as a SQL literal for this engine.</summary>
    /// <remarks>
    /// Culture matters: <c>1.5m</c> formatted under de-DE is <c>"1,5"</c>, which silently turns one
    /// literal into two arguments. Always format invariantly.
    /// </remarks>
    protected virtual string Literal(object? value) => value switch
    {
        null => "NULL",
        string s => Quote(s),
        char c => Quote(c.ToString()),
        bool b => Boolean(b),
        Guid g => Quote(g.ToString()),
        DateTime dt => Quote(dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
        DateTimeOffset dto => Quote(dto.ToString("yyyy-MM-dd HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)),
        byte[] bytes => Blob(bytes),
        IEnumerable e and not string => Collection(e),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(value.ToString() ?? ""),
    };

    /// <summary>How this engine spells a boolean.</summary>
    protected abstract string Boolean(bool value);

    /// <summary>How this engine spells a byte array.</summary>
    protected abstract string Blob(byte[] value);

    /// <summary>
    /// How this engine spells a collection. PostgreSQL binds one array per rowset column, so this is
    /// the shape a rowset takes in debug output; engines with no array literal can only approximate.
    /// </summary>
    protected abstract string Collection(IEnumerable items);

    /// <summary>Renders each element with this renderer's own literal rules.</summary>
    protected string LiteralsOf(IEnumerable items) =>
        string.Join(", ", items.Cast<object?>().Select(Literal));

    /// <summary>Single-quotes a value, doubling any embedded quote.</summary>
    protected static string Quote(string s) => $"'{s.Replace("'", "''")}'";
}

/// <summary>
/// Generic fallback rendering, used by any dialect that does not supply its own.
/// </summary>
/// <remarks>
/// The default for <see cref="IDatabaseDialect.DebugRenderer"/>, so a custom dialect gets readable
/// debug output for free. The built-in dialects each override it: SQL Server emits a
/// <c>DECLARE</c>/<c>SET</c> preamble for SSMS, PostgreSQL and SQLite render literals their own way.
/// </remarks>
public sealed class InlineDebugRenderer : InlineDebugRendererBase
{
    /// <summary>The shared instance — the renderer is stateless.</summary>
    public static readonly InlineDebugRenderer Instance = new();

    private InlineDebugRenderer() { }

    /// <inheritdoc />
    protected override string Boolean(bool value) => value ? "TRUE" : "FALSE";

    /// <inheritdoc />
    protected override string Blob(byte[] value) => $"/* byte[{value.Length}] */";

    /// <inheritdoc />
    protected override string Collection(IEnumerable items) => $"({LiteralsOf(items)})";
}
