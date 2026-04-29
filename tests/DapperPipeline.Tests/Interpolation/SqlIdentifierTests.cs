using DapperPipeline.Interpolation;

namespace DapperPipeline.Tests.Interpolation;

public sealed class SqlIdentifierTests
{
    // ----- Valid identifiers -----

    [Theory]
    [InlineData("Orders")]
    [InlineData("a")]
    [InlineData("_a")]
    [InlineData("_")]
    [InlineData("Order_Lines")]
    [InlineData("Table123")]
    [InlineData("X9")]
    [InlineData("abc_123_DEF")]
    public void Constructor_AcceptsValidIdentifiers(string value)
    {
        var id = new SqlIdentifier(value);
        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        Assert.Equal("Orders", new SqlIdentifier("Orders").ToString());
    }

    // ----- Invalid identifiers — start char -----

    [Theory]
    [InlineData("1Orders")]   // digit start
    [InlineData("9")]          // single digit
    [InlineData("123abc")]
    public void Constructor_RejectsLeadingDigit(string value)
    {
        Assert.Throws<ArgumentException>(() => new SqlIdentifier(value));
    }

    // ----- Invalid identifiers — bad chars -----

    [Theory]
    [InlineData("Or ders")]         // space
    [InlineData("Or-ders")]         // dash
    [InlineData("Or.ders")]         // dot
    [InlineData("Or;ders")]         // semicolon
    [InlineData("Or'ders")]         // single quote
    [InlineData("Or\"ders")]       // double quote
    [InlineData("[Orders]")]        // brackets
    [InlineData("Or)ders")]
    [InlineData("Or(ders")]
    [InlineData("DROP TABLE Users") ]  // injection attempt
    [InlineData("Orders; DELETE FROM Customers")]
    public void Constructor_RejectsInvalidCharacters(string value)
    {
        Assert.Throws<ArgumentException>(() => new SqlIdentifier(value));
    }

    // ----- Empty / null -----

    [Fact]
    public void Constructor_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new SqlIdentifier(""));
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        // ArgumentException.ThrowIfNullOrEmpty throws ArgumentNullException for null,
        // ArgumentException for empty — both are surfaced as ArgumentException semantically
        Assert.Throws<ArgumentNullException>(() => new SqlIdentifier(null!));
    }

    // ----- Sql.Identifier helper -----

    [Fact]
    public void SqlHelper_Identifier_ReturnsValidatedInstance()
    {
        var id = Sql.Identifier("Orders");
        Assert.IsType<SqlIdentifier>(id);
        Assert.Equal("Orders", id.Value);
    }

    [Fact]
    public void SqlHelper_Identifier_ThrowsOnInvalid()
    {
        Assert.Throws<ArgumentException>(() => Sql.Identifier("DROP TABLE Users"));
    }

    // ----- ISqlIdentifier contract -----

    [Fact]
    public void ImplementsISqlIdentifier()
    {
        DapperPipeline.Abstractions.ISqlIdentifier id = new SqlIdentifier("Orders");
        Assert.Equal("Orders", id.Value);
    }
}
