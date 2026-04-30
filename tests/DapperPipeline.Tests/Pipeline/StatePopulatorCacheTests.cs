using DapperPipeline.Pipeline;

namespace DapperPipeline.Tests.Pipeline;

public sealed class StatePopulatorCacheTests
{
    public StatePopulatorCacheTests()
    {
        // Fresh cache per test class so we can assert first-call behavior
        StatePopulatorCache.ClearCacheForTesting();
    }

    private static (string Name, object? Value)[] Capture<T>(T state) where T : class
    {
        var captured = new List<(string, object?)>();
        StatePopulatorCache.Populate(state, (n, v) => captured.Add((n, v)));
        return captured.ToArray();
    }

    // ----- Type-prefixed naming -----

    [Fact]
    public void EmitsTypeNamePropertyName()
    {
        var captured = Capture(new UserSession { BranchId = 42, UserId = "alice" });
        Assert.Contains(("UserSessionBranchId", (object?)42L), captured);
        Assert.Contains(("UserSessionUserId", (object?)"alice"), captured);
    }

    [Fact]
    public void TwoStateTypes_DistinctTypePrefixes()
    {
        var u = Capture(new UserSession { BranchId = 1, UserId = "x" });
        var t = Capture(new TenantContext { Id = 99, Name = "ACME" });

        Assert.Contains(("UserSessionBranchId", (object?)1L), u);
        Assert.Contains(("TenantContextId", (object?)99), t);
        Assert.Contains(("TenantContextName", (object?)"ACME"), t);
        // No collision possible — type prefix disambiguates
    }

    // ----- Scalar property filtering -----

    [Fact]
    public void IncludesPrimitivesAndStringAndCommonValueTypes()
    {
        var captured = Capture(new AllScalars
        {
            I = 1, L = 2L, S = "x", B = true, G = Guid.Empty,
            DT = DateTime.MinValue, DTO = DateTimeOffset.MinValue,
            Dec = 1.5m, En = TestEnum.A
        });

        var names = captured.Select(c => c.Name).ToArray();
        Assert.Contains("AllScalarsI", names);
        Assert.Contains("AllScalarsL", names);
        Assert.Contains("AllScalarsS", names);
        Assert.Contains("AllScalarsB", names);
        Assert.Contains("AllScalarsG", names);
        Assert.Contains("AllScalarsDT", names);
        Assert.Contains("AllScalarsDTO", names);
        Assert.Contains("AllScalarsDec", names);
        Assert.Contains("AllScalarsEn", names);
    }

    [Fact]
    public void IncludesNullableScalars()
    {
        var captured = Capture(new WithNullables { Maybe = 7, Always = 5 });
        Assert.Contains(("WithNullablesMaybe", (object?)(long?)7L), captured);
        Assert.Contains(("WithNullablesAlways", (object?)5), captured);
    }

    [Fact]
    public void NullValueOnNullableProp_StillEmitted()
    {
        var captured = Capture(new WithNullables { Maybe = null, Always = 0 });
        Assert.Contains(("WithNullablesMaybe", (object?)null), captured);
    }

    [Fact]
    public void ExcludesReferenceTypesThatArentString()
    {
        var captured = Capture(new WithReferences { Name = "x", Service = new SomeService() });
        Assert.Contains(("WithReferencesName", (object?)"x"), captured);
        Assert.DoesNotContain(captured, c => c.Name == "WithReferencesService");
    }

    [Fact]
    public void ExcludesCollections()
    {
        var captured = Capture(new WithCollection { Id = 1, Items = new[] { 1, 2, 3 } });
        Assert.Contains(("WithCollectionId", (object?)1), captured);
        Assert.DoesNotContain(captured, c => c.Name == "WithCollectionItems");
    }

    [Fact]
    public void TypeWithNoScalarProperties_EmitsNothing()
    {
        var captured = Capture(new OnlyReferences { Service = new SomeService() });
        Assert.Empty(captured);
    }

    // ----- Cached accessor reuse -----

    [Fact]
    public void RepeatedPopulate_ReturnsSameValuesEachTime()
    {
        var s = new UserSession { BranchId = 5, UserId = "b" };
        var first = Capture(s);
        var second = Capture(s);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentInstancesOfSameType_BothPopulateCorrectly()
    {
        var a = Capture(new UserSession { BranchId = 1, UserId = "a" });
        var b = Capture(new UserSession { BranchId = 2, UserId = "b" });
        Assert.Contains(("UserSessionBranchId", (object?)1L), a);
        Assert.Contains(("UserSessionBranchId", (object?)2L), b);
    }

    // ----- Test fixtures -----

    private sealed class UserSession
    {
        public long   BranchId { get; init; }
        public string UserId   { get; init; } = "";
    }

    private sealed class TenantContext
    {
        public int    Id   { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class AllScalars
    {
        public int            I   { get; init; }
        public long           L   { get; init; }
        public string         S   { get; init; } = "";
        public bool           B   { get; init; }
        public Guid           G   { get; init; }
        public DateTime       DT  { get; init; }
        public DateTimeOffset DTO { get; init; }
        public decimal        Dec { get; init; }
        public TestEnum       En  { get; init; }
    }

    private enum TestEnum { A, B }

    private sealed class WithNullables
    {
        public long? Maybe  { get; init; }
        public int   Always { get; init; }
    }

    private sealed class WithReferences
    {
        public string      Name    { get; init; } = "";
        public SomeService Service { get; init; } = new();
    }

    private sealed class WithCollection
    {
        public int   Id    { get; init; }
        public int[] Items { get; init; } = [];
    }

    private sealed class OnlyReferences
    {
        public SomeService Service { get; init; } = new();
    }

    private sealed class SomeService;
}
