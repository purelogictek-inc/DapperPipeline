using System.Collections;
using DapperPipeline.Debugging;

namespace DapperPipeline.Dialects.PostgreSql;

/// <summary>
/// Renders debug output as PostgreSQL literals — paste-and-run in psql.
/// </summary>
/// <remarks>
/// <para>
/// The differences from the generic rendering are real, not cosmetic:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>Arrays.</strong> PostgreSQL is the one dialect that binds a real array per rowset column
/// (<c>unnest(@a, @b)</c>), so a rowset's parameters <em>are</em> arrays. They must render as
/// <c>ARRAY[…]</c>, or the debug output for the library's own bulk-insert path is unusable.
/// </description></item>
/// <item><description>
/// <strong>Booleans.</strong> <c>TRUE</c>/<c>FALSE</c>; PostgreSQL will not take <c>1</c>/<c>0</c>
/// for a boolean column.
/// </description></item>
/// <item><description><strong>Bytes.</strong> The <c>'\x…'</c> bytea hex form.</description></item>
/// </list>
/// </remarks>
internal sealed class PostgreSqlDebugRenderer : InlineDebugRendererBase
{
    public static readonly PostgreSqlDebugRenderer Instance = new();

    private PostgreSqlDebugRenderer() { }

    /// <inheritdoc />
    protected override string Boolean(bool value) => value ? "TRUE" : "FALSE";

    /// <inheritdoc />
    protected override string Blob(byte[] value) => $"'\\x{Convert.ToHexString(value)}'";

    /// <inheritdoc />
    protected override string Collection(IEnumerable items) => $"ARRAY[{LiteralsOf(items)}]";
}
