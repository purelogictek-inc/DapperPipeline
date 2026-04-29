namespace DapperPipeline.Abstractions;

/// <summary>
/// Scans a SQL string, registers declared variables into the scoped param set,
/// and rewrites all scoped <c>@param</c> tokens with a per-command scope suffix to prevent
/// name collisions when multiple commands are batched into a single SQL string.
/// </summary>
/// <remarks>
/// <para>
/// Parameter names are stored without the leading <c>@</c> prefix.
/// A token is classified as follows (in priority order):
/// </para>
/// <list type="bullet">
/// <item><description><c>@@Word</c> — system variable, left as-is.</description></item>
/// <item><description>Quoted string or comment — skipped entirely.</description></item>
/// <item><description><c>DECLARE @Word</c> — registered in scoped params and rewritten.</description></item>
/// <item><description><c>@Word</c> in shared params — left as-is (shared across commands).</description></item>
/// <item><description><c>@Word</c> in scoped params — rewritten to <c>@Word_{scopeIndex}</c>.</description></item>
/// <item><description><c>@Word</c> not in either set — <see cref="InvalidOperationException"/> thrown.</description></item>
/// </list>
/// </remarks>
public interface IParameterScanner
{
    /// <summary>
    /// Processes <paramref name="sql"/>, registers any declared variables into
    /// <paramref name="scopedParams"/>, and returns the rewritten SQL with all
    /// scoped parameters suffixed by <c>_{scopeIndex}</c>.
    /// </summary>
    /// <param name="sql">The raw SQL string from a command's <c>Build</c> method.</param>
    /// <param name="scopeIndex">The index of the current command in the pipeline batch.</param>
    /// <param name="scopedParams">
    /// Mutable set of parameter names (without <c>@</c>) registered for the current command scope.
    /// The scanner adds DECLARE'd variable names to this set.
    /// </param>
    /// <param name="sharedParams">
    /// Read-only set of shared parameter names (without <c>@</c>) from <c>IPipelineState</c>.
    /// These are never rewritten.
    /// </param>
    string Process(string sql, int scopeIndex, ISet<string> scopedParams, IReadOnlySet<string> sharedParams);
}