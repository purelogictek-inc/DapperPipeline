using DapperPipeline.Abstractions;
using DapperPipeline.Debugging;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.Interpolation;
using DapperPipeline.RowSets;
using DapperPipeline.QueryBuilding;
using NSubstitute;

namespace DapperPipeline.Tests.QueryBuilding;

public sealed class QueryBuilderTests
{
    private readonly IParameterScanner _scanner;
    private readonly QueryBuilder _qb;

    public QueryBuilderTests()
    {
        _scanner = Substitute.For<IParameterScanner>();
        _scanner.Process(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ISet<string>>())
            .Returns(ci => (string)ci[0]);
        _qb = new QueryBuilder(_scanner, ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        _qb.BeginCommandScope(0);
    }

    private static IDictionary<string, object?> Params(IQueryBuilder qb) =>
        (IDictionary<string, object?>)qb.Parameters;

    // -------------------------------------------------------------------------
    // HasQuery / Clear
    // -------------------------------------------------------------------------

    [Fact]
    public void HasQuery_false_when_empty()
    {
        Assert.False(_qb.HasQuery);
    }

    [Fact]
    public void HasQuery_true_after_AppendRaw()
    {
        _qb.AppendRaw("SELECT 1");
        Assert.True(_qb.HasQuery);
    }

    [Fact]
    public void Clear_resets_sql_and_params()
    {
        _qb.RegisterBinding("Id", 1);
        _qb.AppendRaw("SELECT @Id");
        _qb.Clear();
        Assert.False(_qb.HasQuery);
        Assert.Empty(Params(_qb));
    }

    // -------------------------------------------------------------------------
    // Replace placeholder
    // -------------------------------------------------------------------------

    [Fact]
    public void Replace_substitutes_token_in_accumulated_sql()
    {
        _qb.AppendRaw("SELECT %%COLS%%");
        _qb.Replace("COLS", "Id, Name");
        Assert.Contains("Id, Name", _qb.Sql);
    }

    [Fact]
    public void HasReplaceableStatements_true_when_token_present()
    {
        _qb.AppendRaw("SELECT %%COLS%%");
        Assert.True(_qb.HasReplaceableStatements);
    }

    [Fact]
    public void HasReplaceableStatements_false_after_replace()
    {
        _qb.AppendRaw("SELECT %%COLS%%");
        _qb.Replace("COLS", "Id");
        Assert.False(_qb.HasReplaceableStatements);
    }

    // -------------------------------------------------------------------------
    // Optimize
    // -------------------------------------------------------------------------

    [Fact]
    public void Optimize_removes_unused_params()
    {
        _qb.RegisterBinding("Id", 1);
        _qb.RegisterBinding("Name", "test");
        _qb.AppendRaw("SELECT @Id"); // only Id referenced in SQL
        _qb.Optimize();

        var parameters = Params(_qb);
        Assert.Contains("@Id", parameters.Keys);
        Assert.DoesNotContain("@Name", parameters.Keys);
    }

    // -------------------------------------------------------------------------
    // If / DecisionBuilder
    // -------------------------------------------------------------------------

    [Fact]
    public void If_action_emits_IF_BEGIN_END()
    {
        _qb.If(b => b.Clause("@x = 1", q => q.AppendRaw("SELECT 1")));
        Assert.Contains("IF (@x = 1) BEGIN", _qb.Sql);
        Assert.Contains("END", _qb.Sql);
    }

    [Fact]
    public void If_with_else_emits_END_ELSE_BEGIN()
    {
        _qb.If(b => b
            .Clause("@x = 1", q => q.AppendRaw("SELECT 1"))
            .Else(q => q.AppendRaw("SELECT 2")));
        Assert.Contains("END ELSE BEGIN", _qb.Sql);
    }

    [Fact]
    public void If_exists_emits_IF_EXISTS()
    {
        _qb.If(b => b.Exists("SELECT 1 FROM Foo", q => q.AppendRaw("SELECT 1")));
        Assert.Contains("IF EXISTS (SELECT 1 FROM Foo) BEGIN", _qb.Sql);
    }

    [Fact]
    public void If_not_exists_emits_IF_NOT_EXISTS()
    {
        _qb.If(b => b.NotExists("SELECT 1 FROM Foo", q => q.AppendRaw("SELECT 1")));
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM Foo) BEGIN", _qb.Sql);
    }

    [Fact]
    public void If_string_overload_emits_block()
    {
        _qb.If("@x = 1", q => q.AppendRaw("SELECT 1"));
        Assert.Contains("IF (@x = 1) BEGIN", _qb.Sql);
        Assert.Contains("END", _qb.Sql);
    }

    [Fact]
    public void IfNotExists_string_overload_emits_block()
    {
        _qb.IfNotExists("SELECT 1 FROM Foo WHERE Id = 1", q => q.AppendRaw("INSERT INTO Foo VALUES (1)"));
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM Foo WHERE Id = 1) BEGIN", _qb.Sql);
    }

    [Fact]
    public void Else_before_clause_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _qb.If(b => b.Else(q => q.AppendRaw("SELECT 1"))));
    }

    // -------------------------------------------------------------------------
    // WithCte
    // -------------------------------------------------------------------------

    [Fact]
    public void WithCte_emits_WITH_AS_block()
    {
        _qb.WithCte("MyCte", q => q.AppendRaw("SELECT 1"), first: true, terminate: true);
        Assert.Contains("WITH MyCte AS (", _qb.Sql);
        Assert.Contains(")", _qb.Sql);
    }

    [Fact]
    public void WithCte_with_fields_includes_column_list()
    {
        _qb.WithCte("MyCte", q => q.AppendRaw("SELECT 1"), fields: "(Id, Name)", first: true, terminate: true);
        Assert.Contains("MyCte (Id, Name) AS (", _qb.Sql);
    }

    // -------------------------------------------------------------------------
    // ToDebug — uses real scanner so the Append handler can resolve @-tokens
    // -------------------------------------------------------------------------

    [Fact]
    public void ToDebug_includes_declare_and_set_for_int_param()
    {
        // The DECLARE/SET preamble is SQL Server's rendering, so ask the SQL Server dialect for it.
        // It used to be hardcoded in the core, which is why ToDebug() was nonsense on other engines.
        var dialect = new SqlServerDialect("Server=.;Database=d;Trusted_Connection=true;");
        var qb = new QueryBuilder(dialect.Scanner, dialect.RowSetRenderer, dialect.DebugRenderer);
        qb.BeginCommandScope(1);

        long orderId = 42L;
        qb.Append($"SELECT {orderId}");

        var debug = qb.ToDebug();
        Assert.Contains("DECLARE", debug);
        Assert.Contains("@p001_OrderId", debug);
        Assert.Contains("42", debug);
    }
}
