using DapperPipeline.Interpolation;

namespace DapperPipeline.Tests.Interpolation;

public sealed class ParamNameRegistryTests
{
    [Fact]
    public void EmptyRegistry_ZeroClaims()
    {
        var r = new ParamNameRegistry();
        Assert.Equal(0, r.ClaimCount);
    }

    [Fact]
    public void TryClaim_FreshName_ReturnsTrue()
    {
        var r = new ParamNameRegistry();
        Assert.True(r.TryClaim("@p001_OrderId", "orderId"));
        Assert.Equal(1, r.ClaimCount);
    }

    [Fact]
    public void TryClaim_SameNameSameExpr_ReturnsTrue()
    {
        var r = new ParamNameRegistry();
        r.TryClaim("@p001_OrderId", "orderId");
        // Idempotent re-claim — same expression
        Assert.True(r.TryClaim("@p001_OrderId", "orderId"));
        Assert.Equal(1, r.ClaimCount);   // no new entry
    }

    [Fact]
    public void TryClaim_SameNameDifferentExpr_ReturnsFalse()
    {
        var r = new ParamNameRegistry();
        r.TryClaim("@p001_LocationId", "dto.LocationId");
        // Different expression sanitizing to the same name → collision
        Assert.False(r.TryClaim("@p001_LocationId", "customer.LocationId"));
        Assert.Equal(1, r.ClaimCount);   // no new entry on collision
    }

    [Fact]
    public void TryClaim_DifferentNames_AllSucceed()
    {
        var r = new ParamNameRegistry();
        Assert.True(r.TryClaim("@p001_OrderId",    "orderId"));
        Assert.True(r.TryClaim("@p001_CustomerId", "customerId"));
        Assert.True(r.TryClaim("@p001_BranchId",   "session.BranchId"));
        Assert.Equal(3, r.ClaimCount);
    }

    [Fact]
    public void Reset_ClearsAllClaims()
    {
        var r = new ParamNameRegistry();
        r.TryClaim("@p001_OrderId", "orderId");
        r.TryClaim("@p001_BranchId", "session.BranchId");
        Assert.Equal(2, r.ClaimCount);

        r.Reset();
        Assert.Equal(0, r.ClaimCount);
    }

    [Fact]
    public void Reset_AllowsRecyclingNames()
    {
        var r = new ParamNameRegistry();
        r.TryClaim("@p001_OrderId", "orderId");
        r.Reset();

        // After reset, the same name can be claimed by a different expression
        Assert.True(r.TryClaim("@p001_OrderId", "differentExpr"));
    }

    [Fact]
    public void TierEscalationFlow_FirstClaimerWinsName_SecondClaimerEscalates()
    {
        var r = new ParamNameRegistry();
        // Simulates two interpolation holes whose tier-1 sanitized name collides:
        //   {dto.LocationId}      → tier 1 "LocationId"      → claim @p001_LocationId
        //   {customer.LocationId} → tier 1 "LocationId"      → COLLIDES, must try tier 2 "CustomerLocationId"
        Assert.True(r.TryClaim("@p001_LocationId", "dto.LocationId"));
        Assert.False(r.TryClaim("@p001_LocationId", "customer.LocationId"));   // tier 1 collides
        Assert.True(r.TryClaim("@p001_CustomerLocationId", "customer.LocationId")); // tier 2 succeeds
        Assert.Equal(2, r.ClaimCount);
    }
}
