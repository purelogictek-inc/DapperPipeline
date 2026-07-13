using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using static DapperPipeline.Interpolation.Sql;

namespace DapperPipeline.Sample;

// ─── Commands. Each owns its SQL, its parameters, and how it reads its own results. ────────────
//
// A command file needs exactly two usings:
//     using DapperPipeline.Abstractions;   // IQueryBuilder, IPipelineState, Append
//     using DapperPipeline.Commands;       // BaseQueryCommand
// (plus `using static DapperPipeline.Interpolation.Sql;` if you want Text(...) / Identifier(...))

// ── 1. A write command, and the shared-state mechanism ────────────────────────────────────────

public interface ICreateOrderCommand : IQueryCommand<long>
{
    string Customer { get; set; }
}

/// <summary>Inserts an order and returns its new id.</summary>
public sealed class CreateOrderCommand : BaseQueryCommand<long>, ICreateOrderCommand
{
    public string Customer { get; set; } = "";

    public override void Build(IQueryBuilder builder, IPipelineState state)
    {
        // Typed access to whatever the caller passed to SetState.
        var session = state.Require<UserSession>();

        builder.Append($"""
            INSERT INTO orders (customer, status, branch_id, created_by)
            VALUES ({Customer.SqlParam()}, 'new', {session.BranchId}, {session.UserId.SqlParam()})
            RETURNING id
            """);
        //           ^^^^^^^^^^^^^^ every value above is BOUND, never concatenated.
        //           A bare string in a hole is a compile error — that is the whole point.
    }

    public override void Process(IDapperResultProcessor processor) =>
        processor.Read<long>(rows => EmitResult(rows.First()));
}

// ── 2. Bulk insert via a rowset — identical code on every dialect ─────────────────────────────

public interface IAddLinesCommand : IQueryCommand
{
    long OrderId { get; set; }
    IReadOnlyList<OrderLine> Lines { get; set; }
}

public sealed class AddLinesCommand : BaseQueryCommand, IAddLinesCommand
{
    public long OrderId { get; set; }
    public IReadOnlyList<OrderLine> Lines { get; set; } = [];

    public override void Build(IQueryBuilder builder, IPipelineState state)
    {
        // A rowset renders as a derived table. This exact code runs on SQL Server (OPENJSON),
        // PostgreSQL (unnest) and SQLite (json_each) — only the rendering differs.
        //
        // It binds one parameter per COLUMN, not per row, so 10,000 lines cost the same as 10.
        var rows = builder.RowSet("line", Lines, map =>
        {
            map.Column("sku", x => x.Sku);
            map.Column("qty", x => x.Qty);
        });

        builder.Append($"""
            INSERT INTO order_lines (order_id, sku, qty)
            SELECT {OrderId}, line.sku, line.qty
            FROM   {rows}
            """);
    }

    public override void Process(IDapperResultProcessor processor) { }
}

// ── 3. A read command with a WHERE builder and a joined result ────────────────────────────────

public interface IGetOrderCommand : IQueryCommand<Order?>
{
    long OrderId { get; set; }
    string? StatusFilter { get; set; }
}

public sealed class GetOrderCommand : BaseQueryCommand<Order?>, IGetOrderCommand
{
    public long OrderId { get; set; }
    public string? StatusFilter { get; set; }

    public override void Build(IQueryBuilder builder, IPipelineState state)
    {
        var session = state.Require<UserSession>();

        // The WHERE builder binds too — w.Append($"...") is as safe as builder.Append.
        var where = builder.Where(w =>
        {
            w.Append($"o.id = {OrderId}");
            w.Append($"o.branch_id = {session.BranchId}");

            // Plain C# control flow. No SQL-side conditionals needed, and it's portable.
            if (StatusFilter is not null)
                w.Append($"o.status = {StatusFilter.SqlParam()}");
        });

        builder.Append($"""
            SELECT o.id, o.customer, o.status, l.order_id, l.sku, l.qty
            FROM   orders o
            LEFT   JOIN order_lines l ON l.order_id = o.id
            {where}
            """);
    }

    public override void Process(IDapperResultProcessor processor) =>
        // ReadGrouped, NOT Read<T1,T2>. Dapper's multi-map builds a fresh Order for every row of the
        // join, so the naive fold gives you N orders each holding ONE line — a three-line order
        // silently arrives with one. ReadGrouped keeps the first parent per key and folds them all in.
        processor.ReadGrouped<Order, OrderLine, long>(
            o => o.Id,                        // identifies the parent
            (o, line) => o.Lines.Add(line),   // folds each line into it
            rows => EmitResult(rows.FirstOrDefault()),
            "order_id");                      // splitOn is `params string[]` — pass it positionally
}

// ── 4. A command that can exclude itself from the run ─────────────────────────────────────────

public interface IAuditCommand : IQueryCommand
{
    long OrderId { get; set; }
    bool AuditingEnabled { get; set; }
}

public sealed class AuditCommand : BaseQueryCommand, IAuditCommand
{
    public long OrderId { get; set; }
    public bool AuditingEnabled { get; set; } = true;

    /// <summary>Skipped commands contribute no SQL and no parameters — Build is never called.</summary>
    public override bool ShouldInclude(out string? reason)
    {
        if (!AuditingEnabled)
        {
            reason = "auditing is disabled";
            return false;
        }
        reason = null;
        return true;
    }

    public override void Build(IQueryBuilder builder, IPipelineState state)
    {
        var session = state.Require<UserSession>();
        builder.Append($"""
            INSERT INTO audit (order_id, actor) VALUES ({OrderId}, {session.UserId.SqlParam()})
            """);
    }

    public override void Process(IDapperResultProcessor processor) { }
}
