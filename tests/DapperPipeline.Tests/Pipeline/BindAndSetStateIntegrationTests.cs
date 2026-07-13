using DapperPipeline.Abstractions;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.Interpolation;
using DapperPipeline.Pipeline;
using DapperPipeline.RowSets;
using DapperPipeline.QueryBuilding;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Integration tests for the Bind + SetState + handler interaction. Verifies that pipeline-level
/// pre-bindings flow through to the handler dispatch and produce the expected SQL + params.
/// </summary>
public sealed class BindAndSetStateIntegrationTests
{
    private static (PipelineState State, QueryBuilder QueryBuilder) NewPair(int scopeIndex = 1)
    {
        var qb = new QueryBuilder(new SqlServerParameterScanner(), ValuesRowSetRenderer.Instance);
        qb.BeginCommandScope(scopeIndex);
        return (new PipelineState(), qb);
    }

    private static IDictionary<string, object?> Params(IQueryBuilder qb) =>
        (IDictionary<string, object?>)qb.Parameters;

    /// <summary>
    /// Helper that mirrors what DapperPipeline.RegisterPipelineBinding does — populates both
    /// PipelineState (for state.Bound&lt;T&gt;) and QueryBuilder (for value-dedup) in lockstep.
    /// </summary>
    private static void Bind(PipelineState state, QueryBuilder qb, string name, object? value)
    {
        state.RegisterBinding(name, value);
        qb.RegisterBinding(name, value);
    }

    // ----- Bind: simple named scalar -----

    [Fact]
    public void Bind_PopulatesParametersAndIndex()
    {
        var (state, qb) = NewPair();
        Bind(state, qb, "BranchId", 42L);

        Assert.Equal(42L, Params(qb)["@BranchId"]);
    }

    [Fact]
    public void Bind_BoundParam_RoundTrips_Through_Handler()
    {
        var (state, qb) = NewPair();
        Bind(state, qb, "BranchId", 42L);

        // In a command's Build:  builder.Append($"WHERE BranchId = {state.Bound<long>("BranchId")}");
        var bp = state.Bound<long>("BranchId");
        qb.Append($"WHERE BranchId = {bp}");

        Assert.Equal("WHERE BranchId = @BranchId", qb.Sql);
        Assert.Equal(42L, Params(qb)["@BranchId"]);
    }

    [Fact]
    public void Bind_PrimitiveInterpolationOfSameValue_ResolvesToBoundName()
    {
        // The cross-command value-dedup wins: pre-bound value 42 → @BranchId,
        // a later primitive {orderId = 42} interpolation should dedup to @BranchId.
        var (state, qb) = NewPair();
        Bind(state, qb, "BranchId", 42L);

        long orderId = 42L;
        qb.Append($"WHERE Id = {orderId}");

        Assert.Equal("WHERE Id = @BranchId", qb.Sql);
        Assert.Single(Params(qb));   // exactly one param — the pre-bound one
    }

    // ----- SetState: auto-binding with type prefix -----

    [Fact]
    public void SetState_AutoBindsScalarProperties_WithTypePrefix()
    {
        var (state, qb) = NewPair();

        // Mimic SetState behavior: store typed access + walk scalar props
        var session = new TestUserSession { BranchId = 42, UserId = "alice", IsAdmin = true };
        state.Set(session);
        StatePopulatorCache.Populate(session, (n, v) => Bind(state, qb, n, v));

        // Typed access still works
        Assert.Equal(42L, state.Require<TestUserSession>().BranchId);

        // Auto-bound names exist in params
        Assert.Equal(42L, Params(qb)["@TestUserSessionBranchId"]);
        Assert.Equal("alice", Params(qb)["@TestUserSessionUserId"]);
        Assert.Equal(true, Params(qb)["@TestUserSessionIsAdmin"]);
    }

    [Fact]
    public void SetState_PrimitiveInterpolation_ResolvesToTypePrefixedName()
    {
        var (state, qb) = NewPair();
        var session = new TestUserSession { BranchId = 42, UserId = "alice", IsAdmin = true };
        state.Set(session);
        StatePopulatorCache.Populate(session, (n, v) => Bind(state, qb, n, v));

        // In a command: builder.Append($"WHERE BranchId = {s.BranchId}")
        // Primitive long {42} → handler dispatches to BindAndEmit → finds 42 in _valueIndex →
        // emits @TestUserSessionBranchId
        var s = state.Require<TestUserSession>();
        qb.Append($"WHERE BranchId = {s.BranchId}");

        Assert.Equal("WHERE BranchId = @TestUserSessionBranchId", qb.Sql);
        Assert.Equal(42L, Params(qb)["@TestUserSessionBranchId"]);
    }

    [Fact]
    public void SetState_BoundLookupByName_Works()
    {
        var (state, qb) = NewPair();
        var session = new TestUserSession { BranchId = 42, UserId = "alice", IsAdmin = true };
        state.Set(session);
        StatePopulatorCache.Populate(session, (n, v) => Bind(state, qb, n, v));

        // Explicit named lookup also works
        var bp = state.Bound<long>("TestUserSessionBranchId");
        Assert.Equal(42L, bp.Value);

        qb.Append($"... = {bp}");
        Assert.Contains("@TestUserSessionBranchId", qb.Sql);
    }

    [Fact]
    public void SetState_TwoStateTypes_NoCollisionOnSamePropertyName()
    {
        var (state, qb) = NewPair();

        var tenant = new TestTenantContext { Id = 99, Name = "ACME" };
        var user   = new TestUserContext { Id = 7,  Name = "alice" };

        state.Set(tenant);
        StatePopulatorCache.Populate(tenant, (n, v) => Bind(state, qb, n, v));

        state.Set(user);
        StatePopulatorCache.Populate(user, (n, v) => Bind(state, qb, n, v));

        // Distinct names because of type prefix
        Assert.Equal(99, Params(qb)["@TestTenantContextId"]);
        Assert.Equal("ACME", Params(qb)["@TestTenantContextName"]);
        Assert.Equal(7, Params(qb)["@TestUserContextId"]);
        Assert.Equal("alice", Params(qb)["@TestUserContextName"]);
    }

    // ----- Mixing Bind + SetState + handler interpolation -----

    [Fact]
    public void Bind_And_SetState_CoexistInOneRun()
    {
        var (state, qb) = NewPair();

        var session = new TestUserSession { BranchId = 42, UserId = "alice", IsAdmin = true };
        state.Set(session);
        StatePopulatorCache.Populate(session, (n, v) => Bind(state, qb, n, v));
        Bind(state, qb, "Now", new DateTime(2026, 4, 29));

        // From state POCO — primitive long flows through value-dedup
        var s = state.Require<TestUserSession>();
        // From explicit Bind — DateTime via BoundParam
        var now = state.Bound<DateTime>("Now");
        // String values must come through state.Bound<string> (no AppendFormatted(string) overload)
        var userId = state.Bound<string>("TestUserSessionUserId");

        qb.Append($"""
            SELECT * FROM Orders
            WHERE BranchId = {s.BranchId}
              AND CreatedBy = {userId}
              AND CreatedAt > {now}
            """);

        Assert.Contains("@TestUserSessionBranchId", qb.Sql);
        Assert.Contains("@TestUserSessionUserId", qb.Sql);
        Assert.Contains("@Now", qb.Sql);

        Assert.Equal(42L, Params(qb)["@TestUserSessionBranchId"]);
        Assert.Equal("alice", Params(qb)["@TestUserSessionUserId"]);
        Assert.Equal(new DateTime(2026, 4, 29), Params(qb)["@Now"]);
    }

    // ----- Test fixtures -----

    private sealed class TestUserSession
    {
        public long   BranchId { get; init; }
        public string UserId   { get; init; } = "";
        public bool   IsAdmin  { get; init; }
    }

    private sealed class TestTenantContext
    {
        public int    Id   { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class TestUserContext
    {
        public int    Id   { get; init; }
        public string Name { get; init; } = "";
    }
}
