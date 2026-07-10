using System.Text;
using System.Text.RegularExpressions;
using DapperPipeline.Abstractions;

namespace DapperPipeline.Dialects.Sqlite;

/// <summary>
/// Parameter scanner for SQLite.
/// Supports <c>@Word</c>, <c>$Word</c>, and <c>:Word</c> parameter styles.
/// No DECLARE detection — SQLite has no table variables; use CTEs instead.
/// </summary>
internal sealed partial class SqliteParameterScanner : IParameterScanner
{
    // Skips quoted strings and comments; captures @Word / $Word / :Word params.
    // No DECLARE group — SQLite has no variable declaration syntax.
    private static readonly Regex TokenPattern = MyRegex();

    [GeneratedRegex(@"'[^']*'|--[^\r\n]*|/\*.*?\*/|(@\w+|\$\w+|:\w+)", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

    /// <inheritdoc />
    public string Process(string sql, int scopeIndex, ISet<string> scopedParams)
    {
        if (string.IsNullOrEmpty(sql)) return sql;

        var sb = new StringBuilder(sql.Length);
        var lastIndex = 0;

        foreach (Match match in TokenPattern.Matches(sql))
        {
            sb.Append(sql, lastIndex, match.Index - lastIndex);
            lastIndex = match.Index + match.Length;

            if (!match.Groups[1].Success)
            {
                // Quoted string or comment — append verbatim
                sb.Append(match.Value);
                continue;
            }

            var paramToken = match.Groups[1].Value;    // e.g. "@BranchId", "$id", ":name"
            var paramName = paramToken[1..];           // strip prefix char

            if (scopedParams.Contains(paramName))
            {
                sb.Append($"@p{scopeIndex:D3}_{paramName}");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unknown parameter '{paramToken}' in SQL literal. " +
                    $"Pass the value through an interpolation hole instead (e.g. Append($\"... = {{value}}\")). " +
                    $"Raw @Word references in literal SQL are only valid for DECLARE'd variables.");
            }
        }

        sb.Append(sql, lastIndex, sql.Length - lastIndex);
        return sb.ToString();
    }
}
