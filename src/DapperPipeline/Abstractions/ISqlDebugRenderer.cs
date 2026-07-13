namespace DapperPipeline.Abstractions;

/// <summary>
/// Renders a built query and its bound parameters as SQL a human can paste into a client.
/// </summary>
/// <remarks>
/// <para>
/// Engine-specific, so it belongs to the dialect: the original renderer emitted T-SQL
/// (<c>DECLARE @p AS nvarchar(max)</c> / <c>SET</c>), which is meaningless in psql or the sqlite3
/// shell. The default is a portable renderer that inlines the values as literals.
/// </para>
/// <para>
/// ⚠️ <strong>Debug output is for humans, never for execution.</strong> Inlining values produces
/// exactly the concatenated SQL this library exists to prevent. The pipeline never executes it —
/// it executes the parameterized SQL — and neither should you.
/// </para>
/// </remarks>
public interface ISqlDebugRenderer
{
    /// <summary>
    /// Returns a runnable-looking rendering of <paramref name="sql"/> with
    /// <paramref name="parameters"/> made visible.
    /// </summary>
    string Render(string sql, IReadOnlyDictionary<string, object?> parameters);
}
