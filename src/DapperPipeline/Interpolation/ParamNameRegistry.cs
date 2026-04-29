namespace DapperPipeline.Interpolation;

/// <summary>
/// Per-command record of which parameter names have been claimed by which caller expressions
/// in the current scope. Used by <c>QueryBuilder.BindAndEmit</c> to detect collisions across
/// distinct expressions sanitizing to the same name (so it can escalate to the next tier).
/// </summary>
/// <remarks>
/// <para>
/// The pipeline calls <see cref="Reset"/> before each command's <c>Build</c> so claims from
/// the previous command do not leak forward.
/// </para>
/// <para>
/// Pure name-claim tracking — does not store values, does not interact with the parameter
/// dictionary, does not know about scope prefixes. Operates on whatever string the caller
/// passes as <c>fullName</c>.
/// </para>
/// </remarks>
internal sealed class ParamNameRegistry
{
    private readonly Dictionary<string, string> _claims = new();

    /// <summary>Clear all claims. Called on <c>BeginCommandScope</c>.</summary>
    public void Reset() => _claims.Clear();

    /// <summary>
    /// Attempts to claim <paramref name="fullName"/> for <paramref name="callerExpr"/>.
    /// Returns <c>true</c> if the name was free or was already claimed by the same caller
    /// expression (idempotent re-claim). Returns <c>false</c> on collision with a different
    /// caller expression — the caller should try the next sanitizer tier.
    /// </summary>
    public bool TryClaim(string fullName, string callerExpr)
    {
        if (!_claims.TryGetValue(fullName, out var existing))
        {
            _claims[fullName] = callerExpr;
            return true;
        }
        return existing == callerExpr;
    }

    /// <summary>For diagnostics only: how many names have been claimed in the current scope.</summary>
    internal int ClaimCount => _claims.Count;
}
