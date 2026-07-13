using DapperPipeline.Interpolation;

namespace DapperPipeline.Tests.Interpolation;

public sealed class ParamNameSanitizerTests
{
    private static string[] Candidates(string? expr) =>
        ParamNameSanitizer.GenerateCandidates(expr).ToArray();

    // ----- Empty / whitespace -----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void EmptyOrWhitespace_NoCandidates(string? expr)
    {
        Assert.Empty(Candidates(expr));
    }

    // ----- Single-token expressions — all tiers collapse to one candidate -----

    [Theory]
    [InlineData("customerId",  "CustomerId")]
    [InlineData("CustomerId",  "CustomerId")]    // already PascalCase
    [InlineData("orderId",     "OrderId")]
    [InlineData("x",           "X")]
    public void SingleToken_OneCandidate_PascalCased(string expr, string expected)
    {
        var c = Candidates(expr);
        Assert.Single(c);
        Assert.Equal(expected, c[0]);
    }

    // ----- Multi-segment — drop-root first, keep-root second -----

    [Fact]
    public void MultiSegment_DropRoot_ThenKeepRoot()
    {
        var c = Candidates("dto.LocationId");
        Assert.Equal(2, c.Length);
        Assert.Equal("LocationId",    c[0]);    // tier 1: drop "dto"
        Assert.Equal("DtoLocationId", c[1]);    // tier 2: keep "dto"
    }

    [Fact]
    public void MultiSegment_DeepPath_DropRoot_ThenKeepRoot()
    {
        var c = Candidates("loadItem.Origin.Branch.WatscoId");
        Assert.Equal(2, c.Length);
        Assert.Equal("OriginBranchWatscoId",         c[0]);
        Assert.Equal("LoadItemOriginBranchWatscoId", c[1]);
    }

    // ----- Leading underscore stripping -----

    [Theory]
    [InlineData("_customerId",   "CustomerId")]
    [InlineData("_x",            "X")]
    public void LeadingUnderscore_Stripped(string expr, string expected)
    {
        var c = Candidates(expr);
        Assert.Single(c);
        Assert.Equal(expected, c[0]);
    }

    [Fact]
    public void LeadingUnderscore_OnRoot_StrippedBeforeSegmentation()
    {
        // _session.BranchId → session.BranchId → drop-root → BranchId
        var c = Candidates("_session.BranchId");
        Assert.Equal(2, c.Length);
        Assert.Equal("BranchId",        c[0]);
        Assert.Equal("SessionBranchId", c[1]);
    }

    // ----- Trailing .Value stripping (typed-wrapper unwrap) -----

    [Fact]
    public void TrailingDotValue_Stripped()
    {
        // customerId.Value → customerId → CustomerId
        var c = Candidates("customerId.Value");
        Assert.Single(c);
        Assert.Equal("CustomerId", c[0]);
    }

    [Fact]
    public void TrailingDotValue_OnDeepPath_Stripped()
    {
        // dto.LocationId.Value → dto.LocationId → tier 1 LocationId, tier 2 DtoLocationId
        var c = Candidates("dto.LocationId.Value");
        Assert.Equal(2, c.Length);
        Assert.Equal("LocationId",    c[0]);
        Assert.Equal("DtoLocationId", c[1]);
    }

    // ----- Function call — never drop the root -----

    [Fact]
    public void FunctionCallRoot_NeverDropped()
    {
        // GetUser(id).Name → tier 1 same as tier 2 (function-call segments don't drop)
        var c = Candidates("GetUser(id).Name");
        // Tier 1 skipped (root has parens). Tier 2: GetUser_id_Name
        Assert.Single(c);
        Assert.Equal("GetUser_id_Name", c[0]);
    }

    [Fact]
    public void FunctionCallMidPath_StillCountsAsFunctionInThatSegment()
    {
        // dto.GetThing(x).Y
        // Root "dto" not a function — tier 1 drops it: GetThing_x_Y
        // Tier 2 keeps all: DtoGetThing_x_Y
        var c = Candidates("dto.GetThing(x).Y");
        Assert.Equal(2, c.Length);
        Assert.Equal("GetThing_x_Y",    c[0]);
        Assert.Equal("DtoGetThing_x_Y", c[1]);
    }

    // ----- Length cap (32 chars) -----

    [Fact]
    public void LongName_TruncatedTo32Chars()
    {
        // 50-char input → cap to 32
        var longExpr = new string('a', 50);
        var c = Candidates(longExpr);
        Assert.Single(c);
        Assert.Equal(32, c[0].Length);
    }

    [Fact]
    public void LongMultiSegment_BothTiersTruncated()
    {
        var longSegment = new string('a', 25);
        // root.<25 a's>.<25 a's>
        var c = Candidates($"root.{longSegment}.{longSegment}");
        // Tier 1: AaaaAaaa (50 a's PascalCased) → capped to 32
        // Tier 2: Root + 50 a's → capped to 32
        Assert.Equal(2, c.Length);
        Assert.All(c, s => Assert.True(s.Length <= 32));
    }

    // ----- Deduplication -----

    [Fact]
    public void IdenticalTiersAreDeduplicated()
    {
        // For single-token, only one candidate emerges (already deduped)
        var c = Candidates("x");
        Assert.Single(c);
    }

    // ----- Edge cases -----

    [Fact]
    public void AllUnderscores_NoCandidates()
    {
        // "_._" → strip leading _ → "._" → split → ["_"] → PascalCase("_") → "_" (cap)
        // Should yield "_" once
        var c = Candidates("_._");
        Assert.Single(c);
        Assert.Equal("_", c[0]);
    }

    [Fact]
    public void OnlyDots_NoCandidates()
    {
        var c = Candidates("...");
        Assert.Empty(c);
    }

    // ----- Characters that are legal in C# but not in a SQL parameter name -----

    [Theory]
    [InlineData("\"created_at\"", "Created_at")]   // a string LITERAL — the quotes came along too
    [InlineData("\"abc\"",        "Abc")]
    [InlineData("items[0]",       "Items0")]       // indexer brackets
    [InlineData("a + b",          "Ab")]           // operators and spaces
    public void SourceCharactersIllegalInSqlAreStripped(string callerExpr, string expected)
    {
        // The caller expression is SOURCE CODE, and source code contains characters SQL rejects in an
        // identifier. `"created_at".SqlParam()` used to produce the parameter name @p000_"created_at"
        // — quotes included — which is not a valid parameter name and fails at the engine.
        var c = Candidates(callerExpr);

        Assert.NotEmpty(c);
        Assert.Equal(expected, c[0]);
        Assert.DoesNotContain(c[0], ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_');
    }

    [Fact]
    public void EveryCandidateIsAlwaysAValidSqlParameterName()
    {
        // The property that actually matters, over every shape we know how to produce.
        string[] exprs =
        [
            "customerId", "_session.BranchId", "GetUser(id).Name", "\"literal\"", "items[0]",
            "a + b", "dto.Value", "x.SqlParam()", "Sql.Text(customer)", "obj.Method(\"arg\")",
        ];

        foreach (var e in exprs)
            foreach (var name in ParamNameSanitizer.GenerateCandidates(e))
                Assert.DoesNotContain(name, ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_');
    }
}
