using DapperPipeline.Abstractions;
using DapperPipeline.Debugging;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.Interpolation;
using DapperPipeline.QueryBuilding;
using DapperPipeline.RowSets;

namespace DapperPipeline.Tests.QueryBuilding;

/// <summary>
/// Appending the same interpolated expression in a loop used to throw
/// ("bound twice with different values") because the parameter name comes from the caller
/// expression, which is identical on every pass. It now mints ordinals instead.
/// </summary>
public sealed class LoopBindingTests
{
    private static QueryBuilder NewBuilder()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);
        return qb;
    }

    private static IDictionary<string, object?> Params(QueryBuilder qb) =>
        (IDictionary<string, object?>)qb.Parameters;

    [Fact]
    public void Same_expression_with_different_values_in_a_loop_binds_each_value()
    {
        var qb = NewBuilder();

        foreach (var id in new[] { 10, 20, 30 })
            qb.Append($"INSERT INTO t (id) VALUES ({id});");

        var parameters = Params(qb);

        // Three distinct values → three distinct parameters, none lost, nothing thrown.
        Assert.Equal(3, parameters.Count);
        Assert.Equal([10, 20, 30], parameters.Values.Cast<int>().OrderBy(v => v));

        // ...and every one is actually referenced in the SQL.
        foreach (var name in parameters.Keys)
            Assert.Contains(name, qb.Sql);
    }

    [Fact]
    public void Ordinals_are_suffixed_off_the_base_name()
    {
        var qb = NewBuilder();

        foreach (var id in new[] { 10, 20 })
            qb.Append($"SELECT {id};");

        var names = Params(qb).Keys.ToList();

        Assert.Contains(names, n => n.EndsWith("_Id"));      // first keeps the natural name
        Assert.Contains(names, n => n.EndsWith("__2"));      // second gets an ordinal
    }

    [Fact]
    public void Same_expression_with_the_same_value_still_dedupes()
    {
        var qb = NewBuilder();

        // The existing value-dedup behaviour must survive the fix — repeating a value must NOT
        // mint a new parameter.
        foreach (var _ in Enumerable.Range(0, 3))
        {
            var id = 42;
            qb.Append($"SELECT {id};");
        }

        Assert.Single(Params(qb));
    }

    [Fact]
    public void Distinct_expressions_are_unaffected()
    {
        var qb = NewBuilder();

        var first = 1;
        var second = 2;
        qb.Append($"SELECT {first}, {second}");

        var parameters = Params(qb);
        Assert.Equal(2, parameters.Count);
        Assert.Contains(parameters.Keys, k => k.EndsWith("_First"));
        Assert.Contains(parameters.Keys, k => k.EndsWith("_Second"));
    }
}
