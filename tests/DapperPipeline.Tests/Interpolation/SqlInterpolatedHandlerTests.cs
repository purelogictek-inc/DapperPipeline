using DapperPipeline.Abstractions;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.Interpolation;
using DapperPipeline.RowSets;
using DapperPipeline.QueryBuilding;

namespace DapperPipeline.Tests.Interpolation;

public sealed class SqlInterpolatedHandlerTests
{
    private static QueryBuilder NewBuilder(int scopeIndex = 1)
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance);
        qb.BeginCommandScope(scopeIndex);
        return qb;
    }

    private static IDictionary<string, object?> Params(IQueryBuilder qb) =>
        (IDictionary<string, object?>)qb.Parameters;

    // ----- Literal-only -----

    [Fact]
    public void LiteralOnly_PassesThroughScanner()
    {
        var qb = NewBuilder();
        qb.Append($"SELECT 1");
        Assert.Equal("SELECT 1", qb.Sql);
    }

    // ----- Single primitive hole -----

    [Fact]
    public void PrimitiveLong_AutoNamedFromCallerExpr()
    {
        var qb = NewBuilder();
        long orderId = 42;
        qb.Append($"WHERE Id = {orderId}");

        Assert.Equal("WHERE Id = @p001_OrderId", qb.Sql);
        Assert.Equal(42L, Params(qb)["@p001_OrderId"]);
    }

    [Theory]
    [InlineData("orderId",      "@p001_OrderId")]
    [InlineData("OrderId",      "@p001_OrderId")]
    [InlineData("_orderId",     "@p001_OrderId")]   // leading _ stripped
    public void PrimitiveLong_NamedFromVariousCallerExprStyles(string varName, string expectedName)
    {
        var qb = NewBuilder();

        // We can't directly inject callerExpr from a test; instead exercise the canonical pattern.
        // This test verifies the QueryBuilder.BindAndEmit method directly using a synthetic callerExpr.
        qb.BindAndEmit(42L, varName);
        Assert.Equal(expectedName, qb.Sql);
    }

    [Fact]
    public void PrimitiveString_NotAllowedAsPrimitive_ButISqlBindableIs()
    {
        // String cannot be passed directly — there is no AppendFormatted(string) overload.
        // (Compile-time enforcement; not a runtime test.)
        // What CAN be done: pass a typed wrapper that implements ISqlBindable.
        var qb = NewBuilder();
        var tenantId = new TestTenantId("ACME-001");
        qb.Append($"WHERE TenantId = {tenantId}");

        Assert.Equal("WHERE TenantId = @p001_TenantId", qb.Sql);
        Assert.Equal("ACME-001", Params(qb)["@p001_TenantId"]);
    }

    // ----- Multiple holes, same value → one bind -----

    [Fact]
    public void SameValueMultipleHoles_OneBind_MultipleReferences()
    {
        var qb = NewBuilder();
        long orderId = 42;
        qb.Append($"WHERE Id = {orderId} OR ParentId = {orderId} OR RootId = {orderId}");

        Assert.Equal("WHERE Id = @p001_OrderId OR ParentId = @p001_OrderId OR RootId = @p001_OrderId", qb.Sql);
        Assert.Single(Params(qb));   // exactly one parameter
        Assert.Equal(42L, Params(qb)["@p001_OrderId"]);
    }

    // ----- Different values, different names -----

    [Fact]
    public void DifferentValues_GetDistinctNames()
    {
        var qb = NewBuilder();
        long orderId = 42;
        long branchId = 99;
        qb.Append($"WHERE Id = {orderId} AND BranchId = {branchId}");

        Assert.Contains("@p001_OrderId", qb.Sql);
        Assert.Contains("@p001_BranchId", qb.Sql);
        Assert.Equal(42L, Params(qb)["@p001_OrderId"]);
        Assert.Equal(99L, Params(qb)["@p001_BranchId"]);
    }

    // ----- Identifier position -----

    [Fact]
    public void ISqlIdentifier_EmittedRaw_NotBound()
    {
        var qb = NewBuilder();
        var table = new TestTableName("Orders");
        qb.Append($"SELECT * FROM {table}");

        Assert.Equal("SELECT * FROM Orders", qb.Sql);
        Assert.Empty(Params(qb));   // identifiers don't bind
    }

    [Fact]
    public void SqlIdentifier_FromHelper_EmittedRaw()
    {
        var qb = NewBuilder();
        var raw = "Customers";
        qb.Append($"SELECT * FROM {Sql.Identifier(raw)}");

        Assert.Equal("SELECT * FROM Customers", qb.Sql);
        Assert.Empty(Params(qb));
    }

    // ----- BoundParam<T> path -----

    [Fact]
    public void BoundParam_EmitsRegisteredName()
    {
        var qb = NewBuilder();
        var bp = new BoundParam<long>("BranchId", 42);
        qb.Append($"WHERE BranchId = {bp}");

        Assert.Equal("WHERE BranchId = @BranchId", qb.Sql);
        Assert.Equal(42L, Params(qb)["@BranchId"]);
    }

    [Fact]
    public void BoundParam_LazyMaterialization()
    {
        var qb = NewBuilder();
        // BoundParam created but never interpolated → no parameter
        var unused = new BoundParam<long>("Unused", 999);
        qb.Append($"SELECT 1");

        // Just check the BindShared path — it materializes only on first reference
        Assert.Empty(Params(qb));
        // Now reference it
        qb.Append($"WHERE X = {unused}");
        Assert.Equal(999L, Params(qb)["@Unused"]);
    }

    // ----- AppendRaw -----

    [Fact]
    public void AppendRaw_BypassesScannerAndHandler()
    {
        var qb = NewBuilder();
        qb.AppendRaw("SELECT TOP 100 * FROM Orders ORDER BY CreatedAt DESC");

        Assert.Equal("SELECT TOP 100 * FROM Orders ORDER BY CreatedAt DESC", qb.Sql);
        Assert.Empty(Params(qb));
    }

    // ----- Cross-command value dedup -----

    [Fact]
    public void CrossCommandSameValue_FirstBindWins_NameCarriesForward()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance);

        // Command 1 (scope index 1) binds value 42
        qb.BeginCommandScope(1);
        long branchId = 42;
        qb.Append($"WHERE BranchId = {branchId}");
        Assert.Contains("@p001_BranchId", qb.Sql);

        // Command 2 (scope index 2) — same value, should reuse @p001_BranchId
        qb.BeginCommandScope(2);
        long otherBranchId = 42;
        qb.Append($" UNION SELECT WHERE BranchId = {otherBranchId}");
        // Despite being in scope 2, the name from scope 1 carries forward
        Assert.Contains("@p001_BranchId", qb.Sql);
        Assert.Single(Params(qb));   // still one parameter
    }

    // ----- DECLARE @Var in literal portion -----

    [Fact]
    public void DeclareInLiteral_StillScopedByScanner()
    {
        var qb = NewBuilder(scopeIndex: 5);
        long orderId = 42;
        qb.Append($"""
            DECLARE @Ids TABLE (Id bigint)
            INSERT INTO Orders (Id) OUTPUT inserted.Id INTO @Ids VALUES ({orderId})
            SELECT * FROM @Ids
            """);

        // DECLARE'd @Ids → @p005_Ids (handled by scanner on literal portion)
        // {orderId} → @p005_OrderId (handled by handler)
        Assert.Contains("@p005_Ids", qb.Sql);
        Assert.Contains("@p005_OrderId", qb.Sql);
        Assert.DoesNotContain("@Ids ", qb.Sql);   // raw @Ids should not survive
        Assert.Equal(42L, Params(qb)["@p005_OrderId"]);
    }

    // ----- Tier escalation on name collision -----

    [Fact]
    public void DifferentExpressionsSameName_TierEscalates()
    {
        var qb = NewBuilder();

        // dto.LocationId  → tier 1 "LocationId"          → @p001_LocationId (binds dto value 1)
        // customer.LocationId → tier 1 collides → tier 2 "CustomerLocationId" → @p001_CustomerLocationId (binds customer value 2)
        qb.BindAndEmit(1L, "dto.LocationId");
        qb.BindAndEmit(2L, "customer.LocationId");

        Assert.Equal("@p001_LocationId@p001_CustomerLocationId", qb.Sql);
        Assert.Equal(1L, Params(qb)["@p001_LocationId"]);
        Assert.Equal(2L, Params(qb)["@p001_CustomerLocationId"]);
    }

    // ----- BeginCommandScope resets registry -----

    [Fact]
    public void NewCommandScope_RegistryResets_AllowsSameName()
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance);

        qb.BeginCommandScope(1);
        qb.BindAndEmit(1L, "dto.LocationId");   // claims @p001_LocationId

        qb.BeginCommandScope(2);
        // After scope reset, "dto.LocationId" can be claimed again — but with new scope prefix
        qb.BindAndEmit(99L, "dto.LocationId");   // claims @p002_LocationId

        Assert.Equal("@p001_LocationId@p002_LocationId", qb.Sql);
        Assert.Equal(1L, Params(qb)["@p001_LocationId"]);
        Assert.Equal(99L, Params(qb)["@p002_LocationId"]);
    }

    // ----- Test fixtures -----

    private sealed class TestTenantId : ISqlBindable
    {
        public string Value { get; }
        public TestTenantId(string raw) => Value = raw;
    }

    private sealed record TestTableName(string Value) : ISqlIdentifier;
}
