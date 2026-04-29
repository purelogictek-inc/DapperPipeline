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
}
