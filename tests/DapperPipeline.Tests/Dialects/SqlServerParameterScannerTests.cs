using DapperPipeline.Dialects.SqlServer;

namespace DapperPipeline.Tests.Dialects;

public sealed class SqlServerParameterScannerTests
{
    private readonly SqlServerParameterScanner _scanner = new();

    private string Process(string sql, ISet<string>? scoped = null) =>
        _scanner.Process(sql, scopeIndex: 2, scoped ?? new HashSet<string>());

    // --- Basic rewriting ---

    [Fact]
    public void ScopedParam_IsRewrittenWithScopeIndex()
    {
        var scoped = new HashSet<string> { "BranchId" };
        var result = Process("SELECT * FROM Branch WHERE Id = @BranchId", scoped);
        Assert.Equal("SELECT * FROM Branch WHERE Id = @p002_BranchId", result);
    }

    [Fact]
    public void UnknownParam_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Process("SELECT @Unknown"));
    }

    // --- DECLARE detection ---

    [Fact]
    public void Declare_RegistersInScopeAndRewrites()
    {
        var scoped = new HashSet<string>();
        var result = Process("DECLARE @Ids TABLE (Id bigint)", scoped);

        Assert.Contains("Ids", scoped);
        Assert.Equal("DECLARE @p002_Ids TABLE (Id bigint)", result);
    }

    [Fact]
    public void Declare_AllowsSubsequentUsageOfSameVar()
    {
        var scoped = new HashSet<string>();
        var sql = """
            DECLARE @Ids TABLE (Id bigint)
            INSERT INTO Orders OUTPUT inserted.Id INTO @Ids
            SELECT Id FROM @Ids
            """;

        var result = Process(sql, scoped);

        Assert.Contains("@p002_Ids", result);
        Assert.DoesNotContain("@Ids\n", result);
        Assert.DoesNotContain("@Ids\r", result);
    }

    [Fact]
    public void Declare_ScalarVariable_RegistersAndRewrites()
    {
        var scoped = new HashSet<string>();
        var result = Process("DECLARE @ErrorMsg NVARCHAR(MAX)", scoped);

        Assert.Contains("ErrorMsg", scoped);
        Assert.Equal("DECLARE @p002_ErrorMsg NVARCHAR(MAX)", result);
    }

    // --- System variables ---

    [Fact]
    public void SystemVariable_DoubleAt_IsSkipped()
    {
        var result = Process("SELECT @@ROWCOUNT");
        Assert.Equal("SELECT @@ROWCOUNT", result);
    }

    [Fact]
    public void SystemVariable_RowCount_Unchanged()
    {
        var scoped = new HashSet<string> { "Count" };
        var result = Process("SET @Count = @@ROWCOUNT", scoped);
        Assert.Equal("SET @p002_Count = @@ROWCOUNT", result);
    }

    // --- Quoted strings and comments ---

    [Fact]
    public void ParamInQuotedString_IsSkipped()
    {
        var result = Process("SELECT '@NotAParam' AS Literal");
        Assert.Equal("SELECT '@NotAParam' AS Literal", result);
    }

    [Fact]
    public void ParamInLineComment_IsSkipped()
    {
        var scoped = new HashSet<string> { "RealParam" };
        var result = Process("SELECT @RealParam -- @NotAParam", scoped);
        Assert.Equal("SELECT @p002_RealParam -- @NotAParam", result);
    }

    [Fact]
    public void ParamInBlockComment_IsSkipped()
    {
        var scoped = new HashSet<string> { "RealParam" };
        var result = Process("SELECT @RealParam /* @NotAParam */", scoped);
        Assert.Equal("SELECT @p002_RealParam /* @NotAParam */", result);
    }

    // --- Multiple params in one SQL string ---

    [Fact]
    public void MultipleParams_AllRewritten()
    {
        var scoped = new HashSet<string> { "BranchId", "UserId" };
        var result = Process("SELECT @BranchId, @UserId", scoped);
        Assert.Equal("SELECT @p002_BranchId, @p002_UserId", result);
    }

    // --- Empty / null ---

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Process(string.Empty));
    }

    [Fact]
    public void SqlWithNoParams_ReturnedUnchanged()
    {
        const string sql = "SELECT 1 AS Value";
        Assert.Equal(sql, Process(sql));
    }
}
