namespace DapperPipeline.Abstractions;

/// <summary>
/// Renders an <see cref="ISqlRowSet"/> as a dialect-specific table expression.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that keeps the consumer API identical across databases. The same
/// <c>builder.RowSet(...)</c> call becomes <c>unnest(...)</c> on PostgreSQL, <c>OPENJSON(...)</c>
/// on SQL Server, and a portable <c>SELECT ... UNION ALL</c> anywhere else — and the SQL the
/// consumer writes around it (<c>entry.source</c>) does not change.
/// </para>
/// <para>
/// <strong>Parameter budget.</strong> A renderer should aim for <c>O(columns)</c> parameters rather
/// than <c>O(rows)</c>. That is what keeps a large import under the engine's parameter cap
/// (65535 on PostgreSQL, 2100 on SQL Server). The default renderer is <c>O(cells)</c> and therefore
/// caps its row count — see <c>ValuesRowSetRenderer</c>.
/// </para>
/// </remarks>
public interface IRowSetRenderer
{
    /// <summary>
    /// Returns a table expression for <paramref name="rowSet"/> — e.g.
    /// <c>unnest(@p001_entry_0, @p001_entry_1) AS entry(source, external_id)</c> — binding every
    /// value it needs through <paramref name="bind"/>.
    /// </summary>
    /// <remarks>
    /// The expression must expose the rowset's <see cref="ISqlRowSet.Alias"/> and
    /// <see cref="RowSetColumn.Name"/>s, so that consumer SQL referring to <c>alias.column</c>
    /// works identically on every dialect. Values must be bound, never inlined into the text.
    /// </remarks>
    string Render(ISqlRowSet rowSet, IRowSetBindContext bind);
}

/// <summary>
/// The parameter-binding surface handed to an <see cref="IRowSetRenderer"/>.
/// </summary>
/// <remarks>
/// Deliberately tiny: a renderer's only privilege is to bind values and learn the names they were
/// bound under. It cannot write to the SQL buffer — it returns a fragment and the builder places it.
/// </remarks>
public interface IRowSetBindContext
{
    /// <summary>
    /// Binds <paramref name="value"/> as a parameter and returns the name it was bound under,
    /// already scoped to the current command (e.g. <c>@p001_entry_source</c>).
    /// </summary>
    /// <param name="value">
    /// The value — may be a whole array (PostgreSQL binds <c>int[]</c> natively) or a single scalar.
    /// </param>
    /// <param name="nameHint">
    /// A short hint used to build a readable, unique parameter name (e.g. the column name).
    /// </param>
    string Bind(object? value, string nameHint);
}
