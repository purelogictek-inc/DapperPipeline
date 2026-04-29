using System.Text;
using System.Text.RegularExpressions;
using DapperPipeline.Abstractions;

namespace DapperPipeline.Dialects.PostgreSql;

/// <summary>
/// Parameter scanner for PostgresSQL via Npgsql named parameter mode (<c>@Word</c> style).
/// No DECLARE detection — PostgresSQL has no table variables; use CTEs instead.
/// </summary>
internal sealed partial class PostgresSqlParameterScanner : IParameterScanner
{
    // Npgsql supports @Word named parameters as a convenience mapping.
    // No DECLARE @Var TABLE equivalent exists in PostgreSQL.
    private static readonly Regex TokenPattern = MyRegex();

    [GeneratedRegex(@"'[^']*'|--[^\r\n]*|/\*.*?\*/|(@\w+)", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

    /// <inheritdoc />
    public string Process(string sql, int scopeIndex, ISet<string> scopedParams, IReadOnlySet<string> sharedParams)
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
                sb.Append(match.Value);
                continue;
            }

            var paramToken = match.Groups[1].Value; // e.g. "@BranchId"
            var paramName = paramToken[1..]; // e.g. "BranchId"

            if (sharedParams.Contains(paramName))
                sb.Append(paramToken);
            else if (scopedParams.Contains(paramName))
                sb.Append($"@{paramName}_{scopeIndex}");
            else
                throw new InvalidOperationException(
                    $"Unknown parameter '{paramToken}' in SQL. " +
                    $"Register it with builder.Add(\"{paramToken}\", value) before using it in SQL, " +
                    $"or add it to IPipelineState if it should be shared across commands.");
        }

        sb.Append(sql, lastIndex, sql.Length - lastIndex);
        return sb.ToString();
    }
}