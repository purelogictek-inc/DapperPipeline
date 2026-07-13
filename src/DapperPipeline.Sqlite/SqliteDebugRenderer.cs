using System.Collections;
using DapperPipeline.Debugging;

namespace DapperPipeline.Dialects.Sqlite;

/// <summary>
/// Renders debug output as SQLite literals — paste-and-run in the <c>sqlite3</c> shell.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no boolean type: it stores <c>1</c> / <c>0</c>. Rendering <c>TRUE</c> is accepted by
/// modern builds as a keyword but compares against stored integers in a way that surprises people,
/// so the debug output shows what is actually in the column.
/// </para>
/// <para>
/// Blobs use the <c>x'…'</c> form. Collections are not a native SQLite concept — a rowset binds a
/// single JSON string via <c>json_each</c>, so an array parameter should not normally appear here at
/// all; it is rendered as a parenthesised list for readability rather than pretending to be valid.
/// </para>
/// </remarks>
internal sealed class SqliteDebugRenderer : InlineDebugRendererBase
{
    public static readonly SqliteDebugRenderer Instance = new();

    private SqliteDebugRenderer() { }

    /// <inheritdoc />
    protected override string Boolean(bool value) => value ? "1" : "0";

    /// <inheritdoc />
    protected override string Blob(byte[] value) => $"x'{Convert.ToHexString(value)}'";

    /// <inheritdoc />
    protected override string Collection(IEnumerable items) => $"({LiteralsOf(items)})";
}
