using System.Text.RegularExpressions;
using DapperPipeline.Abstractions;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.QueryBuilding;

/// <summary>
/// <c>ToDebug()</c> on SQL Server emits a <c>DECLARE</c>/<c>SET</c> preamble followed by the
/// parameterized query, so you can paste the whole thing into SSMS and run it.
/// </summary>
/// <remarks>
/// It emitted <c>DECLARE @@p001_OrderId</c> — a doubled <c>@</c>. In T-SQL that is the SYSTEM-variable
/// prefix (<c>@@ROWCOUNT</c>), so the preamble was invalid AND declared names the query body never
/// referenced. It looked right in a diff and failed the moment anyone actually pasted it into SSMS,
/// which is the only thing this feature is for.
/// </remarks>
public sealed class SqlServerDebugRendererTests
{
    private static string Debug(Action<IQueryBuilder> build)
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqlServerDialect("Server=x;Database=y;"));
        var qb = services.BuildServiceProvider().GetRequiredService<IQueryBuilder>();
        build(qb);
        return qb.ToDebug();
    }

    [Fact]
    public void The_preamble_never_uses_the_system_variable_prefix()
    {
        long orderId = 7;
        var sql = Debug(b => b.Append($"SELECT * FROM orders WHERE id = {orderId}"));

        Assert.DoesNotContain("@@", sql);
    }

    [Fact]
    public void Every_declared_name_is_a_name_the_query_actually_uses()
    {
        // The property that makes the output runnable. Anything declared but not referenced (or
        // referenced but not declared) means SSMS rejects the script.
        long orderId = 7;
        string customer = "O'Brien";
        var sql = Debug(b => b.Append(
            $"SELECT * FROM orders WHERE id = {orderId} AND customer = {customer.SqlParam()}"));

        var declared = Regex.Matches(sql, @"DECLARE\s+(@\w+)").Select(m => m.Groups[1].Value).ToHashSet();
        var body = sql[(sql.LastIndexOf("SELECT", StringComparison.Ordinal))..];
        var used = Regex.Matches(body, @"@\w+").Select(m => m.Value).ToHashSet();

        Assert.NotEmpty(declared);
        Assert.Equal(used, declared);          // exactly the same set, in both directions
    }

    [Fact]
    public void Values_are_SET_and_quotes_are_escaped()
    {
        string customer = "O'Brien";
        var sql = Debug(b => b.Append($"SELECT * FROM orders WHERE customer = {customer.SqlParam()}"));

        Assert.Contains("SET @p001_Customer = 'O''Brien'", sql);
    }
}
