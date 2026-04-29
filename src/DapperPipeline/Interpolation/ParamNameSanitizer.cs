namespace DapperPipeline.Interpolation;

/// <summary>
/// Converts <see cref="System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"/>
/// text into a small ordered set of debug-friendly SQL parameter name candidates.
/// </summary>
/// <remarks>
/// <para>
/// The sanitizer never emits a leading <c>@</c> or scope prefix — those are added by the
/// caller (typically <c>QueryBuilder.BindAndEmit</c>). It returns just the "name" portion.
/// </para>
/// <para>
/// Candidates are produced in escalating order. The caller iterates and uses the first one
/// that's not already claimed by a different expression in the current command scope.
/// </para>
/// </remarks>
internal static class ParamNameSanitizer
{
    private const int MaxLength = 32;

    /// <summary>
    /// Generates an ordered set of distinct candidate names for the given caller expression.
    /// Returns an empty sequence for null / whitespace / unprocessable input.
    /// </summary>
    /// <param name="callerExpr">
    /// The raw expression text from <c>[CallerArgumentExpression]</c> — e.g.,
    /// <c>"customerId"</c>, <c>"dto.LocationId"</c>, <c>"GetUser(id).Name"</c>.
    /// </param>
    public static IEnumerable<string> GenerateCandidates(string? callerExpr)
    {
        if (string.IsNullOrWhiteSpace(callerExpr)) yield break;

        // Whole-expression preprocessing
        var s = callerExpr.Trim();
        if (s.StartsWith('_')) s = s[1..];                // strip leading _ (field convention)
        if (s.EndsWith(".Value")) s = s[..^6];            // strip trailing .Value (typed unwrap)

        // Split on . then preprocess each segment
        var rawSegments = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Length == 0) yield break;

        var segments = new (string Sanitized, bool IsFunctionCall)[rawSegments.Length];
        for (var i = 0; i < rawSegments.Length; i++)
        {
            var raw = rawSegments[i];
            var hasParens = raw.Contains('(') || raw.Contains(')');
            var sanitized = raw.Replace('(', '_').Replace(')', '_');
            sanitized = PascalCase(sanitized);
            segments[i] = (sanitized, hasParens);
        }

        var seen = new HashSet<string>();

        if (segments.Length == 1)
        {
            // Single token — all tiers collapse to one candidate
            var only = Cap(segments[0].Sanitized);
            if (only.Length > 0 && seen.Add(only))
                yield return only;
            yield break;
        }

        // Tier 1: drop-root (only if the root segment is NOT a function call)
        if (!segments[0].IsFunctionCall)
        {
            var dropped = Cap(string.Concat(segments.Skip(1).Select(t => t.Sanitized)));
            if (dropped.Length > 0 && seen.Add(dropped))
                yield return dropped;
        }

        // Tier 2: keep-root (include all segments)
        var keepRoot = Cap(string.Concat(segments.Select(t => t.Sanitized)));
        if (keepRoot.Length > 0 && seen.Add(keepRoot))
            yield return keepRoot;

        // Tier 3 reserved for future transformation; currently identical to tier 2
        // (would be deduplicated by the seen set if added)
    }

    private static string PascalCase(string segment)
    {
        if (string.IsNullOrEmpty(segment)) return segment;
        if (char.IsUpper(segment[0])) return segment;
        return char.ToUpper(segment[0]) + segment[1..];
    }

    private static string Cap(string s) =>
        s.Length <= MaxLength ? s : s[..MaxLength];
}
