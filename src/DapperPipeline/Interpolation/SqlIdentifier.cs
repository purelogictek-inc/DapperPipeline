using System.Text.RegularExpressions;
using DapperPipeline.Abstractions;

namespace DapperPipeline.Interpolation;

/// <summary>
/// Validated wrapper for a runtime-string SQL identifier (table name, column, schema). Use
/// <c>Sql.Identifier(string)</c> to construct one — the constructor validates against the
/// SQL identifier alphabet and throws <see cref="ArgumentException"/> on rejection.
/// </summary>
/// <remarks>
/// Validation rule: must match <c>[A-Za-z_][A-Za-z0-9_]*</c> — i.e. start with a letter or
/// underscore, contain only letters / digits / underscores, no whitespace, no dots, no
/// brackets, no SQL keywords-with-special-characters. This rejects every classical SQL
/// injection vector at the boundary.
/// </remarks>
public sealed partial class SqlIdentifier : ISqlIdentifier
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex IdentifierPattern();

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>
    /// Constructs a validated <see cref="SqlIdentifier"/>. Throws
    /// <see cref="ArgumentException"/> if <paramref name="value"/> doesn't match
    /// <c>[A-Za-z_][A-Za-z0-9_]*</c>.
    /// </summary>
    public SqlIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (!IdentifierPattern().IsMatch(value))
            throw new ArgumentException(
                $"'{value}' is not a valid SQL identifier. Must match [A-Za-z_][A-Za-z0-9_]* — " +
                $"no whitespace, dots, brackets, or other special characters.",
                nameof(value));

        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
