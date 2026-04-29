using DapperPipeline.Abstractions;
using DapperPipeline.QueryBuilding;
using NSubstitute;

namespace DapperPipeline.Tests.QueryBuilding;

public sealed class WhereBuilderTests
{
    private readonly QueryBuilder _qb;

    public WhereBuilderTests()
    {
        var scanner = Substitute.For<IParameterScanner>();
        scanner.Process(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ISet<string>>(), Arg.Any<IReadOnlySet<string>>())
            .Returns(ci => (string)ci[0]);
        _qb = new QueryBuilder(scanner);
        _qb.BeginCommandScope(0);
    }

    [Fact]
    public void IsEmpty_true_when_no_clauses_added()
    {
        var where = _qb.Where();
        Assert.True(where.IsEmpty());
    }

    [Fact]
    public void Add_first_clause_uses_WHERE()
    {
        var where = _qb.Where();
        where.Add("x = 1");
        Assert.Contains("where", where.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Add_subsequent_clause_uses_AND()
    {
        var where = _qb.Where();
        where.Add("x = 1").Add("y = 2");
        var sql = where.ToString();
        Assert.Contains("where", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Add_with_isOr_uses_OR()
    {
        var where = _qb.Where();
        where.Add("x = 1").Add("y = 2", isOr: true);
        Assert.Contains("or", where.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsEmpty_false_after_clause_added()
    {
        var where = _qb.Where();
        where.Add("x = 1");
        Assert.False(where.IsEmpty());
    }

    [Fact]
    public void Clear_makes_builder_empty_again()
    {
        var where = _qb.Where();
        where.Add("x = 1").Clear();
        Assert.True(where.IsEmpty());
    }

    [Fact]
    public void InnerJoin_emits_INNER_JOIN()
    {
        var where = _qb.Where();
        where.InnerJoin("Foo", "Foo.Id = Bar.FooId");
        Assert.Contains("inner join", where.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Foo", where.ToString());
    }

    [Fact]
    public void InnerJoin_with_extras_emits_AND_conditions()
    {
        var where = _qb.Where();
        where.InnerJoin("Foo", "Foo.Id = Bar.FooId", "Foo.Active = 1");
        Assert.Contains("AND", where.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeftOuterJoin_emits_left_outer_join()
    {
        var where = _qb.Where();
        where.LeftOuterJoin("Foo", "Foo.Id = Bar.FooId");
        Assert.Contains("left outer join", where.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToString_empty_when_no_clauses()
    {
        var where = _qb.Where();
        Assert.Equal(string.Empty, where.ToString());
    }

    [Fact]
    public void Where_with_source_copies_clauses()
    {
        var source = _qb.Where();
        source.Add("x = 1");

        var derived = _qb.Where(source);
        Assert.Contains("x = 1", derived.ToString());
    }

    [Fact]
    public void Where_with_source_and_replacement_rewrites_tokens()
    {
        var source = _qb.Where();
        source.Add("x = TARGET");

        var derived = _qb.Where(source, "TARGET", "42");
        Assert.Contains("42", derived.ToString());
        Assert.DoesNotContain("TARGET", derived.ToString());
    }

    [Fact]
    public void Where_out_param_returns_querybuilder_for_chaining()
    {
        var result = _qb.Where(out var where);
        Assert.Same(_qb, result);
    }

    [Fact]
    public void upCase_uppercases_keywords()
    {
        var where = _qb.Where(upCase: true);
        where.Add("x = 1");
        Assert.Contains("WHERE", where.ToString());
    }
}
