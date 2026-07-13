namespace DapperPipeline.Dialects.Sqlite;

/// <summary>
/// How <c>SqliteDialect</c> renders a <c>RowSet</c>. Set once on the dialect at startup — command
/// code is identical whichever you pick.
/// </summary>
public enum SqliteRowSetStrategy
{
    /// <summary>
    /// <c>json_each(@json)</c> — the default. Binds <strong>one</strong> parameter regardless of row
    /// count, so a large insert never approaches SQLite's variable cap. JSON is built into SQLite
    /// (3.38+) and is compiled into the builds Microsoft.Data.Sqlite ships, so it needs no setup.
    /// </summary>
    JsonEach = 0,

    /// <summary>
    /// Portable <c>SELECT … UNION ALL</c>, binding one parameter per cell. Only needed for an
    /// unusual SQLite build compiled without JSON support. Bounded — see <c>ValuesRowSetRenderer</c>.
    /// </summary>
    Values = 1,
}
