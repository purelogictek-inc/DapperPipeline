namespace DapperPipeline.Interpolation;

/// <summary>
/// Static helpers for safe interpolation. Import with <c>using static DapperPipeline.Interpolation.Sql;</c>
/// to use <see cref="Identifier(string)"/> as a bare function in interpolation holes.
/// </summary>
public static class Sql
{
    /// <summary>
    /// Wraps a runtime <see cref="string"/> as a validated <see cref="SqlIdentifier"/> for use
    /// in identifier position inside an <c>Append($"...")</c> interpolation. Throws
    /// <see cref="ArgumentException"/> if the value is not a valid SQL identifier.
    /// </summary>
    /// <example>
    /// <code>
    /// using static DapperPipeline.Interpolation.Sql;
    ///
    /// string tableName = config["Tables:Orders"];
    /// builder.Append($"INSERT INTO {Identifier(tableName)} VALUES (...)");
    /// </code>
    /// </example>
    public static SqlIdentifier Identifier(string value) => new(value);

    /// <summary>
    /// Binds a runtime <see cref="string"/> as a SQL <em>parameter</em> in value position inside an
    /// <c>Append($"...")</c> interpolation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bare <see cref="string"/> in an interpolation hole is a compile error — that is what stops
    /// <c>$"WHERE Name = '{userInput}'"</c> from ever reaching the database. But string
    /// <em>values</em> are perfectly legitimate and extremely common, so they need a door: this is it.
    /// </para>
    /// <para>
    /// The value is <strong>bound as a parameter</strong>, never inlined into the SQL text. Wrapping
    /// is required only so the intent is explicit and greppable — <c>Text(...)</c> in a diff says
    /// "this is a value", where a bare interpolation would have said nothing at all.
    /// </para>
    /// <example>
    /// <code>
    /// using static DapperPipeline.Interpolation.Sql;
    ///
    /// builder.Append($"WHERE Name = {Text(userName)}");   // → WHERE Name = @p001_UserName
    /// builder.Append($"WHERE Name = {userName}");         // → compile error, as intended
    /// </code>
    /// </example>
    /// </remarks>
    public static SqlText Text(string? value) => new(value);
}

/// <summary>
/// A <see cref="string"/> value destined for a SQL parameter. Produced by <see cref="Sql.Text"/>.
/// </summary>
/// <remarks>
/// Consumers with their own typed wrappers (a <c>CustomerCode</c>, say) should implement
/// <see cref="Abstractions.ISqlBindable"/> directly — this exists so that plain strings do not
/// force everyone to invent the same wrapper.
/// </remarks>
public sealed record SqlText(string? Raw) : Abstractions.ISqlBindable
{
    /// <inheritdoc />
    public string? Value => Raw;
}
