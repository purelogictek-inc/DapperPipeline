using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Processing;

/// <summary>
/// <c>Read&lt;T1, T2&gt;</c> calls the mapping function <strong>once per row</strong> — Dapper
/// constructs a fresh <c>T1</c> for every row of the join. Folding children in without deduplicating
/// the parent therefore produces N parents each holding ONE child, and taking the first gives you an
/// order with one line instead of five. It compiles, it does not throw, and the data is wrong.
/// </summary>
/// <remarks>
/// The README documented exactly that pattern until a runnable sample read three lines back as one.
/// These tests pin both the trap and the correct shape, against a real database.
/// </remarks>
public sealed class MultiMapTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_mm_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public MultiMapTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customer TEXT NOT NULL);
            CREATE TABLE order_lines (order_id INTEGER NOT NULL, sku TEXT NOT NULL);
            INSERT INTO orders VALUES (1, 'Contoso');
            INSERT INTO order_lines VALUES (1, 'A'), (1, 'B'), (1, 'C');
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    public sealed class Order
    {
        public long Id { get; set; }
        public string Customer { get; set; } = "";
        public List<OrderLine> Lines { get; } = [];
    }

    public sealed class OrderLine
    {
        public long OrderId { get; set; }
        public string Sku { get; set; } = "";
    }

    private const string Sql = """
        SELECT o.id, o.customer, l.order_id, l.sku
        FROM   orders o JOIN order_lines l ON l.order_id = o.id
        """;

    // The naive fold — what the README used to show.
    private interface INaiveCommand : IQueryCommand<Order?>;

    private sealed class NaiveCommand : BaseQueryCommand<Order?>, INaiveCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) => builder.AppendRaw(Sql);

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<Order, OrderLine>(
                (order, line) => { order.Lines.Add(line); return order; },
                rows => EmitResult(rows.FirstOrDefault()),
                "order_id");
    }

    // The correct fold — deduplicate the parent by key.
    private interface IGroupedCommand : IQueryCommand<Order?>;

    private sealed class GroupedCommand : BaseQueryCommand<Order?>, IGroupedCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) => builder.AppendRaw(Sql);

        public override void Process(IDapperResultProcessor processor)
        {
            var byId = new Dictionary<long, Order>();

            processor.Read<Order, OrderLine>(
                (order, line) =>
                {
                    if (!byId.TryGetValue(order.Id, out var parent))
                        byId[order.Id] = parent = order;

                    if (line is not null) parent.Lines.Add(line);
                    return parent;
                },
                rows => EmitResult(rows.Distinct().FirstOrDefault()),
                "order_id");
        }
    }

    // What the library now offers so nobody has to hand-roll the dictionary.
    private interface IHelperCommand : IQueryCommand<Order?>;

    private sealed class HelperCommand : BaseQueryCommand<Order?>, IHelperCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) => builder.AppendRaw(Sql);

        public override void Process(IDapperResultProcessor processor) =>
            processor.ReadGrouped<Order, OrderLine, long>(
                o => o.Id,
                (o, l) => o.Lines.Add(l),
                rows => EmitResult(rows.FirstOrDefault()),
                "order_id");
    }

    private async Task<Order?> Run<T>() where T : class, IQueryCommand<Order?>
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<T>();

        Order? result = null;
        await services.BuildServiceProvider().GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<T>(c => c.OnResult(o => result = o))
            .RunAsync(CancellationToken.None);
        return result;
    }

    [Fact]
    public async Task The_naive_fold_silently_loses_rows()
    {
        var order = await Run<NaiveCommand>();

        // Three lines were joined. The naive fold returns an order holding ONE of them, quietly.
        // This is the trap, pinned so nobody "fixes" the docs back to it.
        Assert.NotNull(order);
        Assert.Single(order!.Lines);
    }

    [Fact]
    public async Task Deduplicating_the_parent_keeps_every_child()
    {
        var order = await Run<GroupedCommand>();

        Assert.NotNull(order);
        Assert.Equal(3, order!.Lines.Count);
        Assert.Equal(["A", "B", "C"], order.Lines.Select(l => l.Sku).Order());
    }

    [Fact]
    public async Task ReadGrouped_does_the_deduplication_for_you()
    {
        var order = await Run<HelperCommand>();

        Assert.NotNull(order);
        Assert.Equal(3, order!.Lines.Count);
        Assert.Equal(["A", "B", "C"], order.Lines.Select(l => l.Sku).Order());
    }

    [Fact]
    public async Task ReadGrouped_yields_one_entry_per_parent_not_per_row()
    {
        // The join produces three rows for one order. The handler must see ONE order, not three —
        // otherwise callers go back to guessing with FirstOrDefault and lose the children again.
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO orders VALUES (2, 'Fabrikam'); INSERT INTO order_lines VALUES (2, 'D');";
            cmd.ExecuteNonQuery();
        }

        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<CountingCommand>();

        List<Order> seen = [];
        await services.BuildServiceProvider().GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<CountingCommand>(c => c.Captured = o => seen = o.ToList())
            .RunAsync(CancellationToken.None);

        Assert.Equal(2, seen.Count);                       // two orders, not four rows
        Assert.Equal(3, seen.Single(o => o.Id == 1).Lines.Count);
        Assert.Single(seen.Single(o => o.Id == 2).Lines);
    }

    private sealed class CountingCommand : BaseQueryCommand
    {
        public Action<IEnumerable<Order>> Captured { get; set; } = _ => { };

        public override void Build(IQueryBuilder builder, IPipelineState state) => builder.AppendRaw(Sql);

        public override void Process(IDapperResultProcessor processor) =>
            processor.ReadGrouped<Order, OrderLine, long>(
                o => o.Id,
                (o, l) => o.Lines.Add(l),
                rows => Captured(rows),
                "order_id");
    }
}
