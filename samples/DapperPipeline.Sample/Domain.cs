using DapperPipeline.Abstractions;

namespace DapperPipeline.Sample;

// ─── Domain types. Plain POCOs — no library types leak into them. ──────────────────────────────

public sealed class Order
{
    public long Id { get; set; }
    public string Customer { get; set; } = "";
    public string Status { get; set; } = "";
    public List<OrderLine> Lines { get; } = [];
}

public sealed class OrderLine
{
    public long OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
}

/// <summary>
/// Shared context for a pipeline run. A plain POCO — <c>SetState</c> stores it for typed access and
/// pre-binds its scalar properties as SQL parameters.
/// </summary>
public sealed class UserSession
{
    public long BranchId { get; init; }
    public string UserId { get; init; } = "";
}

/// <summary>
/// A consumer-defined typed wrapper. Implementing <see cref="ISqlBindable"/> opts the type into the
/// interpolation allowlist, so <c>{sku}</c> binds as a parameter instead of failing to compile.
/// </summary>
/// <remarks>
/// For plain strings you don't need your own wrapper — <c>Sql.Text(...)</c> ships with the library.
/// This shows the pattern for a real domain type.
/// </remarks>
public sealed record Sku(string Code) : ISqlBindable
{
    public string? Value => Code;
}

/// <summary>Thrown when the database reports a constraint violation we've mapped.</summary>
public sealed class DuplicateOrderException(string message) : Exception(message);
