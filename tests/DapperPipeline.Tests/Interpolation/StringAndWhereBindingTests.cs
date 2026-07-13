using DapperPipeline.Abstractions;
using DapperPipeline.Debugging;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.Interpolation;
using DapperPipeline.QueryBuilding;
using DapperPipeline.RowSets;
using static DapperPipeline.Interpolation.Sql;

namespace DapperPipeline.Tests.Interpolation;

/// <summary>
/// Two holes in the library's headline guarantee, both surfaced by compiling the README:
/// a plain <c>string</c> value could not be bound at all, and the WHERE builder took raw strings —
/// so <c>w.Add($"name = '{userInput}'")</c> compiled and injected.
/// </summary>
public sealed class StringAndWhereBindingTests
{
    private static QueryBuilder NewBuilder()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);
        return qb;
    }

    private static IDictionary<string, object?> Params(QueryBuilder qb) =>
        (IDictionary<string, object?>)qb.Parameters;

    // ------------------------------------------------------------- Sql.Text

    [Fact]
    public void Text_binds_a_string_as_a_parameter_never_inlining_it()
    {
        var qb = NewBuilder();
        var userName = "o'brien";   // an apostrophe would break naive concatenation outright

        qb.Append($"SELECT * FROM Users WHERE Name = {Text(userName)}");

        // The value must not appear in the SQL text at all.
        Assert.DoesNotContain("o'brien", qb.Sql);
        Assert.Contains("@p001_", qb.Sql);
        Assert.Equal("o'brien", Assert.Single(Params(qb)).Value);
    }

    [Fact]
    public void Text_names_the_parameter_from_the_caller_expression()
    {
        var qb = NewBuilder();
        var userName = "alice";

        qb.Append($"WHERE Name = {Text(userName)}");

        Assert.Contains(Params(qb).Keys, k => k.Contains("UserName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Text_of_null_binds_sql_null_not_an_empty_string()
    {
        var qb = NewBuilder();
        string? missing = null;

        qb.Append($"WHERE Name = {Text(missing)}");

        // NULL and '' are different values in SQL; collapsing one into the other would be a silent bug.
        Assert.Null(Assert.Single(Params(qb)).Value);
    }

    // -------------------------------------------------------- WHERE binding

    [Fact]
    public void Where_append_binds_values_rather_than_inlining_them()
    {
        var qb = NewBuilder();
        var branchId = 42L;

        var where = qb.Where(w => w.Append($"o.BranchId = {branchId}"));
        qb.Append($"SELECT * FROM Orders o {where}");

        Assert.DoesNotContain("42", qb.Sql);
        Assert.Contains("@p001_BranchId", qb.Sql);
        Assert.Equal(42L, Params(qb)["@p001_BranchId"]);
    }

    [Fact]
    public void Where_append_binds_strings_through_Text()
    {
        var qb = NewBuilder();
        var status = "pending";

        var where = qb.Where(w => w.Append($"o.Status = {Text(status)}"));
        qb.Append($"SELECT * FROM Orders o {where}");

        Assert.DoesNotContain("pending", qb.Sql);
        Assert.Equal("pending", Assert.Single(Params(qb)).Value);
    }

    [Fact]
    public void Where_append_composes_multiple_clauses_and_binds_each()
    {
        var qb = NewBuilder();
        var branchId = 42L;
        var statusId = 7;

        var where = qb.Where(w =>
        {
            w.Append($"o.BranchId = {branchId}");
            w.Append($"o.StatusId = {statusId}");
        });
        qb.Append($"SELECT * FROM Orders o {where}");

        Assert.Contains("where", qb.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and", qb.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Params(qb).Count);
        Assert.Equal(42L, Params(qb)["@p001_BranchId"]);
        Assert.Equal(7, Params(qb)["@p001_StatusId"]);
    }

    [Fact]
    public void Where_append_supports_or()
    {
        var qb = NewBuilder();
        var a = 1;
        var b = 2;

        var where = qb.Where(w =>
        {
            w.Append($"x = {a}");
            w.Append($"y = {b}", isOr: true);
        });
        qb.Append($"SELECT * FROM t {where}");

        Assert.Contains("or", qb.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Params(qb).Count);
    }

    [Fact]
    public void Where_values_share_the_commands_parameter_set()
    {
        // A clause bound in the WHERE builder must land in the same parameter dictionary as the
        // rest of the command — otherwise the SQL references a parameter that was never sent.
        var qb = NewBuilder();
        var branchId = 99L;

        var where = qb.Where(w => w.Append($"BranchId = {branchId}"));
        qb.Append($"SELECT * FROM Orders {where}");

        foreach (var name in Params(qb).Keys)
            Assert.Contains(name, qb.Sql);
    }
}
