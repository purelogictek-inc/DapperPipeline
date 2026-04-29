using System.Text;
using System.Text.RegularExpressions;
using DapperPipeline.Abstractions;

namespace DapperPipeline.Dialects.SqlServer;

/// <summary>
/// Parameter scanner for SQL Server / T-SQL.
/// Detects <c>@Word</c> parameters and <c>DECLARE @Word</c> / <c>DECLARE @Word TABLE</c> variables.
/// </summary>
internal sealed partial class SqlServerParameterScanner : IParameterScanner
{
    // Match priority (left-to-right alternation):
    //   'quoted string'          → skip
    //   -- line comment          → skip
    //   /* block comment */      → skip
    //   DECLARE @Word            → group 1 — register in scope + rewrite
    //   @@Word                   → group 2 — system variable, skip
    //   @Word                    → group 3 — classify and rewrite
    private static readonly Regex TokenPattern = MyRegex();

    [GeneratedRegex(@"'[^']*'|--[^\r\n]*|/\*.*?\*/|DECLARE\s+(@\w+)|(@@\w+)|(@\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline, "en-US")]
    private static partial Regex MyRegex();

    /// <inheritdoc />
    public string Process(string sql, int scopeIndex, ISet<string> scopedParams, IReadOnlySet<string> sharedParams)
    {
        if (string.IsNullOrEmpty(sql)) return sql;

        var sb = new StringBuilder(sql.Length);
        var lastIndex = 0;

        foreach (Match match in TokenPattern.Matches(sql))
        {
            // Append text between last match and this one verbatim
            sb.Append(sql, lastIndex, match.Index - lastIndex);
            lastIndex = match.Index + match.Length;

            if (match.Groups[1].Success)
            {
                // DECLARE @Word — register in scope and rewrite
                var paramToken = match.Groups[1].Value;        // e.g. "@Identifiers"
                var paramName = paramToken[1..];               // e.g. "Identifiers"
                scopedParams.Add(paramName);
                var rewritten = $"@{paramName}_{scopeIndex}";
                // Replace only the @Word part within the full DECLARE match
                sb.Append(match.Value.Replace(paramToken, rewritten));
            }
            else if (match.Groups[2].Success)
            {
                // @@Word — SQL Server system variable, always leave as-is
                sb.Append(match.Value);
            }
            else if (match.Groups[3].Success)
            {
                // @Word — classify
                var paramToken = match.Groups[3].Value;        // e.g. "@BranchId"
                var paramName = paramToken[1..];               // e.g. "BranchId"

                if (sharedParams.Contains(paramName))
                    sb.Append(paramToken); // shared — leave as-is
                else if (scopedParams.Contains(paramName))
                    sb.Append($"@{paramName}_{scopeIndex}"); // scoped — rewrite
                else
                    throw new InvalidOperationException(
                        $"Unknown parameter '{paramToken}' in SQL. " +
                        $"Register it with builder.Add(\"{paramToken}\", value) before using it in SQL, " +
                        $"or add it to IPipelineState if it should be shared across commands.");
            }
            else
            {
                // Quoted string or comment — append verbatim
                sb.Append(match.Value);
            }
        }

        // Append any remaining text after the last match
        sb.Append(sql, lastIndex, sql.Length - lastIndex);
        return sb.ToString();
    }

}