namespace DapperPipeline.Abstractions;

/// <summary>
/// Marker contract for typed domain wrappers that should be emitted as a raw SQL identifier
/// (table name, column, schema, table-variable name) when used in an interpolated
/// <c>Append($"...")</c> string. The wrapper's value is written verbatim into the SQL — never
/// parameterized.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Implementations must validate <see cref="Value"/> at construction time.</strong>
/// The interpolation handler trusts that any <see cref="ISqlIdentifier"/> instance contains a
/// safe SQL identifier and emits its <see cref="Value"/> with no further checks. Allowing
/// invalid characters into <see cref="Value"/> defeats the compile-time injection-prevention
/// guarantee.
/// </para>
/// <para>
/// A safe baseline validation is to reject anything outside <c>[A-Za-z_][A-Za-z0-9_]*</c>.
/// Consumers may use stricter rules (e.g. an allow-list of known table names) if they prefer.
/// </para>
/// <para>
/// For ad-hoc raw strings determined at runtime, use <c>Sql.Identifier(string)</c> instead of
/// implementing this interface — it constructs a validated <c>SqlIdentifier</c> for you.
/// </para>
/// </remarks>
public interface ISqlIdentifier
{
    /// <summary>The validated SQL identifier text. Emitted verbatim into the SQL string.</summary>
    string Value { get; }
}
