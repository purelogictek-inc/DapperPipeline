namespace DapperPipeline.Abstractions;

/// <summary>
/// Scans the literal portions of a SQL string emitted by <c>IQueryBuilder.Append</c>'s
/// interpolation handler, registers DECLARE'd variables into the scoped param set, and rewrites
/// all scoped <c>@param</c> tokens with a per-command scope prefix to prevent name collisions
/// when multiple commands are batched into a single SQL string.
/// </summary>
/// <remarks>
/// <para>
/// Parameter names are stored without the leading <c>@</c> prefix.
/// A token is classified as follows (in priority order):
/// </para>
/// <list type="bullet">
/// <item><description><c>@@Word</c> — system variable, left as-is.</description></item>
/// <item><description>Quoted string or comment — skipped entirely.</description></item>
/// <item><description><c>DECLARE @Word</c> — registered in scoped params and rewritten to <c>@p{NNN}_Word</c>.</description></item>
/// <item><description><c>@Word</c> in scoped params — rewritten to <c>@p{NNN}_Word</c>.</description></item>
/// <item><description><c>@Word</c> not registered — <see cref="InvalidOperationException"/> thrown.</description></item>
/// </list>
/// <para>
/// The scope prefix is <c>p</c> followed by the zero-padded three-digit command index
/// (e.g. <c>p001</c>, <c>p002</c>). This format is shared with parameter names emitted directly
/// by the interpolation handler from <c>{value}</c> holes, producing one consistent naming scheme
/// across DECLARE'd variables and auto-bound interpolation values.
/// </para>
/// <para>
/// Cross-command parameter sharing is handled at the <see cref="IQueryBuilder"/> level via
/// bind-time value deduplication — when an interpolation hole's value already exists in the
/// parameter dictionary, the existing parameter name is reused. The scanner does not need a
/// separate "shared" name set; it operates only on per-command scope.
/// </para>
/// </remarks>
public interface IParameterScanner
{
    /// <summary>
    /// Processes <paramref name="sql"/>, registers any declared variables into
    /// <paramref name="scopedParams"/>, and returns the rewritten SQL with all
    /// scoped parameters prefixed by <c>p{NNN}_</c>.
    /// </summary>
    /// <param name="sql">The literal portion of a SQL string emitted by the interpolation handler.</param>
    /// <param name="scopeIndex">The index of the current command in the pipeline batch (1-based).</param>
    /// <param name="scopedParams">
    /// Mutable set of parameter names (without <c>@</c>) registered for the current command scope.
    /// The scanner adds DECLARE'd variable names to this set.
    /// </param>
    string Process(string sql, int scopeIndex, ISet<string> scopedParams);
}