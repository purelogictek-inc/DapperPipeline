namespace DapperPipeline.Abstractions;

/// <summary>
/// N rows of typed columns, rendered by the dialect as a derived table you can select from.
/// </summary>
/// <remarks>
/// <para>
/// A rowset is the dialect-agnostic answer to "insert / join against N rows". The consumer builds
/// one the same way on every database; only the rendering differs, which is the dialect's job:
/// </para>
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
/// Values are always <strong>bound</strong>, never concatenated into the SQL text.
/// </para>
/// </remarks>
public interface ISqlRowSet
{
    /// <summary>The table alias the SQL refers to (e.g. <c>entry</c> in <c>entry.source</c>).</summary>
    string Alias { get; }

    /// <summary>The columns, in declaration order.</summary>
    IReadOnlyList<RowSetColumn> Columns { get; }

    /// <summary>How many rows the set carries.</summary>
    int RowCount { get; }
}

/// <summary>
/// One column of a <see cref="ISqlRowSet"/> — its name, its CLR type, and one value per row.
/// </summary>
/// <param name="Name">The column name as the SQL will refer to it.</param>
/// <param name="ClrType">
/// The type the selector returned. The dialect maps this to its own SQL type
/// (e.g. <c>int</c> → <c>int4[]</c> on PostgreSQL, <c>int</c> on SQL Server).
/// </param>
/// <param name="Values">One entry per row, in row order. Length equals <c>ISqlRowSet.RowCount</c>.</param>
public sealed record RowSetColumn(string Name, Type ClrType, IReadOnlyList<object?> Values);
