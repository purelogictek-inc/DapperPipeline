using DapperPipeline.Abstractions;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Interpolation;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Interpolation;

/// <summary>
/// <c>string.SqlParam()</c> — the opt-in that says "treat this string as a SQL value".
/// </summary>
/// <remarks>
/// Every other type in an interpolation hole binds automatically. A string is the one type that must
/// declare itself, because it is the one type SQL cannot disambiguate: <c>{customer}</c> could be the
/// value <c>'Contoso'</c> or the column <c>customer</c>. <c>InterpolationEnforcementTests</c> proves
/// the bare form does not compile; these prove the opt-in actually binds rather than concatenates.
/// </remarks>
public sealed class StringSqlExtensionsTests
{
    private static (string Sql, IDictionary<string, object?> Params) Build(Action<IQueryBuilder> build)
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect("Data Source=:memory:"));
        var qb = services.BuildServiceProvider().GetRequiredService<IQueryBuilder>();
        build(qb);
        return (qb.Sql, (IDictionary<string, object?>)qb.Parameters);
    }

    [Fact]
    public void SqlParam_binds_the_value_and_the_payload_never_reaches_the_SQL()
    {
        const string attack = "Contoso'; DROP TABLE Orders;--";

        var (sql, ps) = Build(b => b.Append($"WHERE Name = {attack.SqlParam()}"));

        Assert.DoesNotContain("DROP TABLE", sql);          // it is a parameter, not text
        Assert.Contains("WHERE Name = @p000_Attack", sql);
        Assert.Equal(attack, ps["@p000_Attack"]);          // the payload survives intact — as DATA
    }

    [Fact]
    public void SqlParam_is_exactly_Sql_Text()
    {
        const string customer = "Contoso";

        var viaExtension = Build(b => b.Append($"WHERE Name = {customer.SqlParam()}"));
        // Sql.Text is deprecated in favour of .SqlParam(), but this test is the PROOF that swapping
        // to it is safe — so it has to keep calling the deprecated door on purpose.
#pragma warning disable CS0618
        var viaSqlText = Build(b => b.Append($"WHERE Name = {Sql.Text(customer)}"));
#pragma warning restore CS0618

        // Same type, same binding, same SQL. SqlParam is a spelling, not a second mechanism —
        // one code path means one thing to get wrong.
        Assert.Equal(viaSqlText.Sql, viaExtension.Sql);
        Assert.Equal(viaSqlText.Params["@p000_Customer"], viaExtension.Params["@p000_Customer"]);
    }

    [Fact]
    public void A_null_string_binds_as_SQL_NULL_rather_than_the_word_null()
    {
        string? missing = null;

        var (sql, ps) = Build(b => b.Append($"WHERE Name = {missing.SqlParam()}"));

        Assert.Contains("@p000_Missing", sql);
        Assert.Null(ps["@p000_Missing"]);
    }

    [Fact]
    public void SqlParam_binds_inside_a_WHERE_clause_too()
    {
        const string status = "shipped";

        var (sql, ps) = Build(b =>
        {
            var where = b.Where(w => w.Append($"o.status = {status.SqlParam()}"));
            b.Append($"SELECT * FROM orders o {where}");
        });

        Assert.Contains("o.status = @p000_Status", sql);
        Assert.Equal(status, ps["@p000_Status"]);
    }

    [Fact]
    public void SqlIdentifier_emits_a_validated_name_raw()
    {
        const string sortColumn = "created_at";

        var (sql, ps) = Build(b => b.Append($"SELECT * FROM orders ORDER BY {sortColumn.SqlIdentifier()}"));

        // An identifier CANNOT be parameterized — ORDER BY @p would order by a constant and silently
        // not sort. So it is emitted raw, which is only safe because it was validated first.
        Assert.Contains("ORDER BY created_at", sql);
        Assert.Empty(ps);
    }

    [Theory]
    [InlineData("name; DROP TABLE orders--")]
    [InlineData("name' OR '1'='1")]
    [InlineData("orders.name")]
    [InlineData("name with spaces")]
    [InlineData("[name]")]
    public void SqlIdentifier_throws_on_anything_that_is_not_an_identifier(string attack)
    {
        // This is what makes the identifier door safe to put in IntelliSense next to the value door.
        // Every classical injection vector dies here, at the boundary, before any SQL is built.
        var ex = Assert.Throws<ArgumentException>(() =>
            Build(b => b.Append($"SELECT * FROM {attack.SqlIdentifier()}")));

        Assert.Contains("not a valid SQL identifier", ex.Message);
    }

    [Fact]
    public void Misusing_the_identifier_door_for_a_value_fails_loudly_not_silently()
    {
        // Someone reaches for SqlIdentifier to silence a compile error on a VALUE. It is emitted as a
        // bare name, so the database rejects it as an unknown column — loud, and at the first run.
        // (The injection payload case throws even earlier; see the theory above.)
        const string customer = "Contoso";

        var (sql, _) = Build(b => b.Append($"WHERE Name = {customer.SqlIdentifier()}"));

        Assert.Contains("WHERE Name = Contoso", sql);   // → "no such column: Contoso" at the engine
    }
}
