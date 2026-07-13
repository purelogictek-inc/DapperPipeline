using DapperPipeline.Interpolation;

// Deliberately in DapperPipeline.Abstractions, NOT DapperPipeline.Interpolation. A command file
// already has `using DapperPipeline.Abstractions;` — it needs it for IQueryBuilder — so these appear
// in IntelliSense the moment someone types `customer.` inside a hole, with no extra using and
// nothing to know in advance. An extension nobody can find is an extension nobody uses, and
// discoverability is the entire point of these two.
namespace DapperPipeline.Abstractions;

/// <summary>
/// The two doors a raw <see cref="string"/> can take into SQL. A string must pick one; every other
/// type is bound automatically.
/// </summary>
/// <remarks>
/// <para>
/// <c>int</c>, <c>Guid</c>, <c>DateTime</c> and any <see cref="ISqlBindable"/> go straight into an
/// interpolation hole — there is nothing to ask, because none of them could ever be a table name.
/// <strong>A string is the one type that is genuinely ambiguous</strong>: <c>{customer}</c> could
/// mean the value <c>'Contoso'</c> or the column <c>customer</c>, and those are not interchangeable.
/// Guessing is how injections happen, so the compiler makes you say which.
/// </para>
/// <code>
/// builder.Append($"WHERE Name = {customer.SqlParam()}");        // a VALUE  → bound parameter
/// builder.Append($"ORDER BY {sortColumn.SqlIdentifier()}");     // a NAME   → validated, raw
/// </code>
/// <para>
/// Both are exposed, and that is on purpose. Hiding the identifier door does not stop someone who
/// needs an identifier — it pushes them to <c>AppendRaw</c>, which validates <em>nothing</em>. A
/// discoverable, validated door is safer than an undiscoverable one.
/// </para>
/// </remarks>
public static class StringSqlExtensions
{
    /// <summary>
    /// Binds this string as a SQL <strong>parameter</strong> — the safe door, and the one you want
    /// almost every time. <c>null</c> binds as SQL <c>NULL</c>.
    /// </summary>
    /// <remarks>
    /// Identical to <see cref="Sql.Text(string?)"/>; this spelling is simply findable from the
    /// variable. The value is never concatenated into the SQL text, so a payload like
    /// <c>'; DROP TABLE Orders;--</c> is inert — it arrives at the database as data.
    /// </remarks>
    /// <param name="value">The string value. May be null.</param>
    public static SqlText SqlParam(this string? value) => new(value);

    /// <summary>
    /// Emits this string as a SQL <strong>identifier</strong> — a table, column or schema name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Identical to <see cref="Sql.Identifier(string)"/>. The name is emitted <em>raw</em>, because
    /// that is the only way an identifier can work — you cannot parameterize a table name. It is
    /// therefore <strong>validated</strong> against <c>[A-Za-z_][A-Za-z0-9_]*</c> and throws on
    /// anything else: no quotes, no whitespace, no semicolons, no dots. Every classical injection
    /// vector dies at that boundary.
    /// </para>
    /// <para>
    /// Use it for a column name chosen at runtime — a sort column, a dynamic table. Do <em>not</em>
    /// reach for it to silence a compile error on a value: pass a value through
    /// <see cref="SqlParam"/> instead.
    /// </para>
    /// </remarks>
    /// <param name="value">The identifier. Must match <c>[A-Za-z_][A-Za-z0-9_]*</c>.</param>
    /// <exception cref="ArgumentException">The string is not a valid identifier.</exception>
    public static SqlIdentifier SqlIdentifier(this string value) => new(value);
}
