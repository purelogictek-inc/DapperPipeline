using System.ComponentModel;
using System.Data;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Fluent SQL builder. Commands call this in their <c>Build</c> method to compose SQL via
/// <c>Append($"...")</c> interpolation (the primary API) or <c>AppendRaw(string)</c> (the
/// escape hatch). The pipeline batches all commands into a single SQL string and executes
/// them in one round-trip within a transaction.
/// </summary>
/// <remarks>
/// Parameter binding is implicit — values flow through interpolation holes, where the
/// handler auto-parameterizes them with names derived from the caller expression. Per-command
/// scope prefixes (<c>@p001_</c>, <c>@p002_</c>) prevent name collisions between batched
/// commands. Cross-command value deduplication ensures identical values bind only once.
/// </remarks>
public interface IQueryBuilder
{
    // -------------------------------------------------------------------------
    // SQL composition
    // -------------------------------------------------------------------------

    /// <summary>
    /// Appends a SQL fragment verbatim, bypassing both the interpolation handler and the
    /// parameter scanner. The explicit, code-reviewable escape hatch for SQL that genuinely
    /// cannot be parameterized (DDL keywords, dynamic ORDER BY, etc.). For typed-safe SQL
    /// composition with parameter binding, use the <c>Append($"...")</c> extension method.
    /// </summary>
    IQueryBuilder AppendRaw(string sql);

    /// <summary>Replaces a placeholder token in the accumulated SQL.</summary>
    IQueryBuilder Replace(string key, object clause);

    // -------------------------------------------------------------------------
    // Handler-dispatch surface (called by SqlInterpolatedHandler, not by user code)
    // Hidden from IntelliSense via [EditorBrowsable(Never)].
    // -------------------------------------------------------------------------

    /// <summary>Routes a literal SQL portion through the dialect's parameter scanner and appends it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    void AppendScannedLiteral(string literal);

    /// <summary>Appends a pre-validated SQL identifier verbatim (no quoting, no scanning).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    void AppendIdentifier(string identifier);

    /// <summary>
    /// Binds a value with an auto-generated parameter name derived from <paramref name="callerExpr"/>,
    /// then appends a parameter reference (e.g. <c>@p001_OrderId</c>) to the SQL. If the same
    /// value is already bound (anywhere in the pipeline), reuses the existing parameter name.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    void BindAndEmit(object? value, string callerExpr);

    /// <summary>
    /// Binds a value under an explicit name (from a <c>BoundParam&lt;T&gt;</c> or
    /// <c>pipeline.Bind</c> registration) and appends the parameter reference to the SQL.
    /// Lazy: the value enters the parameter dictionary only on first reference.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    void BindShared(object? value, string name);

    /// <summary>
    /// Renders a rowset through the dialect and appends the resulting table expression,
    /// binding the rowset's values as parameters.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    void EmitRowSet(ISqlRowSet rowSet);

    // -------------------------------------------------------------------------
    // Rowsets — the dialect-agnostic way to pass N rows
    // -------------------------------------------------------------------------

    /// <summary>
    /// Declares N rows of typed columns as a derived table, and returns a marker you interpolate
    /// into the SQL. Works identically on every dialect — the dialect decides the rendering.
    /// </summary>
    /// <param name="alias">The table alias the SQL will use (e.g. <c>entry</c>).</param>
    /// <param name="rows">The rows. Enumerated once, immediately.</param>
    /// <param name="map">Declares the columns; each column's SQL type is inferred.</param>
    /// <remarks>
    /// <code>
    /// var entries = builder.RowSet("entry", externalIds, map =>
    /// {
    ///     map.Column("source",      x => x.Source);
    ///     map.Column("external_id", x => x.ExternalId);
    /// });
    ///
    /// builder.Append($"""
    ///     INSERT INTO team_external_id (team_version_id, source, external_id)
    ///     SELECT v.id, entry.source, entry.external_id
    ///     FROM   new_version v, {entries}
    ///     """);
    /// </code>
    /// <para>
    /// Values are bound, never concatenated. PostgreSQL binds one parameter per <em>column</em>
    /// (<c>unnest</c>) and SQL Server one in total (<c>OPENJSON</c>), so row count does not consume
    /// the parameter budget. The portable fallback binds one per cell and caps the row count.
    /// </para>
    /// </remarks>
    ISqlRowSet RowSet<T>(string alias, IEnumerable<T> rows, Action<IRowSetColumnBuilder<T>> map);

    // -------------------------------------------------------------------------
    // Table-valued parameters (SQL Server)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a collection of <typeparamref name="T"/> to a SQL table-valued parameter
    /// using the fluent <see cref="IDataTableMapper{T}"/> setup action.
    /// </summary>
    /// <remarks>
    /// SQL Server specific — requires a matching user-defined table type in the database. For
    /// portable code use <see cref="RowSet{T}"/>, which needs no database setup.
    /// </remarks>
    IQueryBuilder MapTable<T>(string paramName, string tableType, IEnumerable<T> source,
        Action<IDataTableMapper<T>> setup);

    /// <summary>Adds a pre-built <see cref="DataTable"/> as a table-valued parameter.</summary>
    IQueryBuilder AddTableParam(string paramName, DataTable table);

    /// <summary>
    /// Adds a simple scalar collection as a table-valued parameter with a single column.
    /// </summary>
    IQueryBuilder AddTableParam<T>(string paramName, IEnumerable<T> values, string columnName = "Id");

    // -------------------------------------------------------------------------
    // Conditional SQL — see DapperPipeline.SqlServer
    // -------------------------------------------------------------------------
    //
    // If / IfNotExists emitted T-SQL procedural control flow (IF ... BEGIN ... END ELSE ...),
    // which is invalid on PostgreSQL and SQLite. They now ship as extension methods in the
    // DapperPipeline.SqlServer package, so they exist only where they actually work.
    //
    // For portable conditionals, put the condition in a predicate rather than a procedural block:
    //     INSERT INTO t (a) SELECT {a} WHERE NOT EXISTS (SELECT 1 FROM t WHERE k = {k})
    // and when the condition is known in your app, just branch in C# inside Build().

    // -------------------------------------------------------------------------
    // WHERE / JOIN
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a WHERE / JOIN clause. The returned <see cref="IWhereBuilder"/> can be embedded
    /// in a subsequent <c>Append($"...")</c> call via its <c>ToString()</c>.
    /// </summary>
    IWhereBuilder Where(Action<IWhereBuilder>? builder = null, bool upCase = false);

    /// <summary>
    /// Builds a WHERE clause starting from an existing <paramref name="source"/> builder.
    /// </summary>
    IWhereBuilder Where(IWhereBuilder source, Action<IWhereBuilder>? builder = null, bool upCase = false);

    /// <summary>
    /// Builds a WHERE clause and exposes it via <paramref name="where"/> for later embedding,
    /// while returning <c>this</c> for fluent chaining.
    /// </summary>
    IQueryBuilder Where(out IWhereBuilder where, Action<IWhereBuilder>? builder = null, bool upCase = false);

    /// <summary>Returns a new <see cref="IWhereBuilder"/> cloned from <paramref name="source"/>
    /// with <paramref name="target"/> replaced by <paramref name="replacement"/>.</summary>
    IWhereBuilder Where(IWhereBuilder source, string target, string replacement);

    // -------------------------------------------------------------------------
    // CTEs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a Common Table Expression block.
    /// SQL inside the CTE action is appended without semicolons.
    /// </summary>
    IQueryBuilder WithCte(string name, Action<IQueryBuilder> cte, string fields = "",
        string description = "", bool first = false, bool terminate = false);

    // -------------------------------------------------------------------------
    // Indentation (used internally by If/Where/CTE blocks)
    // -------------------------------------------------------------------------

    /// <summary>Increases the current indentation level.</summary>
    IQueryBuilder AddIndent();

    /// <summary>Decreases the current indentation level.</summary>
    IQueryBuilder RemoveIndent();

    // -------------------------------------------------------------------------
    // Inspection / pipeline use
    // -------------------------------------------------------------------------

    /// <summary>Returns <c>true</c> if any SQL has been added.</summary>
    bool HasQuery { get; }

    /// <summary>Returns <c>true</c> if the SQL contains unreplaced placeholder tokens.</summary>
    bool HasReplaceableStatements { get; }

    /// <summary>The accumulated SQL string.</summary>
    string Sql { get; }

    /// <summary>The accumulated Dapper parameters.</summary>
    object Parameters { get; }

    /// <summary>Removes unused parameters from the parameter bag (substring filter).</summary>
    void Optimize();

    /// <summary>Clears all SQL and parameters.</summary>
    void Clear();

    /// <summary>Returns a debug-friendly SQL string with DECLARE/SET statements for all parameters.</summary>
    string ToDebug();
}