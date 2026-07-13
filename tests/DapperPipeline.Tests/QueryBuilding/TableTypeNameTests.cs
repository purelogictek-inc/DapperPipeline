using System.Reflection;
using DapperPipeline.Abstractions;
using DapperPipeline.Debugging;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.QueryBuilding;
using DapperPipeline.RowSets;

namespace DapperPipeline.Tests.QueryBuilding;

/// <summary>
/// The table type name must reach Dapper exactly as the caller wrote it. It used to be prefixed
/// with "dbo.", which made every other schema unreachable — and turned the name the README itself
/// documents ("dbo.OrderLineType") into "dbo.dbo.OrderLineType".
/// </summary>
public sealed class TableTypeNameTests
{
    private sealed record Line(int ProductId, int Qty);

    private static readonly Line[] Lines = [new(1, 2), new(3, 4)];

    private static QueryBuilder NewBuilder()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance, InlineDebugRenderer.Instance);
        qb.BeginCommandScope(1);
        return qb;
    }

    /// <summary>Reads the type name Dapper actually recorded on the bound TVP.</summary>
    private static string TypeNameOf(QueryBuilder qb, string paramName)
    {
        var parameters = (IDictionary<string, object?>)qb.Parameters;

        // TVPs are registered under the command-scoped name (@p001_Lines), not the raw @Lines.
        var key = parameters.Keys.Single(k => k.EndsWith(paramName.TrimStart('@')));
        var tvp = parameters[key];
        Assert.NotNull(tvp);

        var field = tvp!.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(f => f.FieldType == typeof(string));

        return (string)field.GetValue(tvp)!;
    }

    [Theory]
    [InlineData("dbo.OrderLineType")]   // the README's own example — was "dbo.dbo.OrderLineType"
    [InlineData("sales.OrderLineType")] // a non-dbo schema — was unreachable entirely
    [InlineData("OrderLineType")]       // unqualified: resolves against the default schema
    public void MapTable_uses_the_type_name_verbatim(string tableType)
    {
        var qb = NewBuilder();

        qb.MapTable("@Lines", tableType, Lines, m =>
        {
            m.Map("ProductId", l => l.ProductId);
            m.Map("Qty", l => l.Qty);
        });

        Assert.Equal(tableType, TypeNameOf(qb, "@Lines"));
    }

    [Fact]
    public void MapTable_never_double_qualifies_a_schema()
    {
        var qb = NewBuilder();

        qb.MapTable("@Lines", "dbo.OrderLineType", Lines, m => m.Map("ProductId", l => l.ProductId));

        var name = TypeNameOf(qb, "@Lines");
        Assert.DoesNotContain("dbo.dbo", name);
        Assert.Equal("dbo.OrderLineType", name);
    }

    [Fact]
    public void MapTable_rejects_an_empty_type_name()
    {
        var qb = NewBuilder();

        Assert.ThrowsAny<ArgumentException>(() =>
            qb.MapTable("@Lines", "  ", Lines, m => m.Map("ProductId", l => l.ProductId)));
    }
}
