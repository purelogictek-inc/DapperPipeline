namespace DapperPipeline.Dialects.SqlServer;

/// <summary>
/// How <c>SqlServerDialect</c> renders a <c>RowSet</c>. Set once on the dialect at startup —
/// command code is identical whichever you pick.
/// </summary>
public enum SqlServerRowSetStrategy
{
    /// <summary>
    /// <c>OPENJSON(@json) WITH (...)</c> — the default. Binds <strong>one</strong> parameter
    /// regardless of row count, and needs nothing installed in the database.
    /// Requires SQL Server 2016+ (compatibility level 130+).
    /// </summary>
    OpenJson = 0,

    /// <summary>
    /// A table-valued parameter. Binds one parameter, and is the fastest option for very large sets —
    /// but requires a user-defined table type in the database whose columns match the rowset, named
    /// via <c>SqlServerDialect.RowSetTableType</c>.
    /// </summary>
    /// <remarks>
    /// Because the table type must already exist and match the rowset's shape, this cannot be
    /// inferred — it is opt-in. Use it when you already have TVP types (or need TVP throughput);
    /// otherwise <see cref="OpenJson"/> is equivalent and needs no setup.
    /// </remarks>
    TableValuedParameter = 1,

    /// <summary>
    /// Portable <c>SELECT … UNION ALL</c>, binding one parameter per cell. For SQL Server 2012/2014,
    /// which have no <c>OPENJSON</c>. Bounded by the 2100-parameter cap, so it is only for small sets.
    /// </summary>
    Values = 2,
}
