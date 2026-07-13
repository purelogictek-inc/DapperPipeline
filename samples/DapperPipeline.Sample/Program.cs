using System.Data.Common;
using DapperPipeline.Abstractions;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.ErrorHandling;
using DapperPipeline.Sample;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
//  DapperPipeline — runnable sample
//
//  Uses SQLite against a temp file, so it needs no setup at all:
//      dotnet run --project samples/DapperPipeline.Sample
//
//  Everything here is dialect-agnostic. Swap SqliteDialect for PostgreSqlDialect or
//  SqlServerDialect at the one registration line below and the commands do not change.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

var dbPath = Path.Combine(Path.GetTempPath(), $"dp-sample-{Guid.NewGuid():N}.db");
var connectionString = $"Data Source={dbPath}";

try
{
    CreateSchema(connectionString);

    // ── Setup: one dialect, your commands. That's the whole registration. ──────────────────────
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));   // optional — works without it

    services.AddDapperPipeline(new SqliteDialect(connectionString));
    //                         ^^^^^^^^^^^^^^^^ the only dialect-specific line in this program

    services.AddDapperPipelineCommands(typeof(CreateOrderCommand).Assembly);

    // Map database errors to domain exceptions. SQLite reports 19 for a constraint violation.
    services.AddSingleton<IErrorMapper>(new ErrorRange(19, 20,
        (ex, _) => new DuplicateOrderException(ex.Message)));

    // A behavior wraps every RunAsync — logging, metrics, timing.
    services.AddSingleton<IPipelineBehavior, TimingBehavior>();

    var provider = services.BuildServiceProvider();

    // ── 1. Several commands, ONE round-trip, ONE transaction ───────────────────────────────────
    Section("1. Three commands batched into a single transaction");

    var lines = new List<OrderLine>
    {
        new() { Sku = "WIDGET-1", Qty = 2 },
        new() { Sku = "GIZMO-7", Qty = 1 },
        new() { Sku = "DOODAD-3", Qty = 5 },
    };

    long newOrderId = 0;

    await provider.GetRequiredService<IDapperPipeline>()
        .SetState(new UserSession { BranchId = 42, UserId = "alice" })
        .ResolveAndRegister<ICreateOrderCommand>(c =>
        {
            c.Customer = "Contoso";
            c.OnResult(id => newOrderId = id);
        })
        .ResolveAndRegister<IAuditCommand>(c => c.OrderId = 1)
        .RunAsync(CancellationToken.None);

    Console.WriteLine($"   created order {newOrderId} (insert + audit, one round-trip)");

    // ── 2. Bulk insert via a rowset ────────────────────────────────────────────────────────────
    Section("2. Bulk insert — one parameter per COLUMN, not per row");

    await provider.GetRequiredService<IDapperPipeline>()
        .SetState(new UserSession { BranchId = 42, UserId = "alice" })
        .ResolveAndRegister<IAddLinesCommand>(c =>
        {
            c.OrderId = newOrderId;
            c.Lines = lines;
        })
        .RunAsync(CancellationToken.None);

    Console.WriteLine($"   inserted {lines.Count} lines. 10,000 would bind the same 2 parameters.");

    // ── 3. Read it back, with a conditional WHERE ──────────────────────────────────────────────
    Section("3. Read back — WHERE builder binds too");

    Order? order = null;
    await provider.GetRequiredService<IDapperPipeline>()
        .SetState(new UserSession { BranchId = 42, UserId = "alice" })
        .ResolveAndRegister<IGetOrderCommand>(c =>
        {
            c.OrderId = newOrderId;
            c.StatusFilter = "new";       // try null — the clause simply isn't emitted
            c.OnResult(o => order = o);
        })
        .RunAsync(CancellationToken.None);

    Console.WriteLine($"   order {order?.Id}: {order?.Customer} [{order?.Status}]");
    foreach (var l in order?.Lines ?? [])
        Console.WriteLine($"     - {l.Sku} × {l.Qty}");

    // ── 4. A command that excludes itself ──────────────────────────────────────────────────────
    Section("4. ShouldInclude — a command can opt out of the run");

    await provider.GetRequiredService<IDapperPipeline>()
        .SetState(new UserSession { BranchId = 42, UserId = "alice" })
        .ResolveAndRegister<IAuditCommand>(c =>
        {
            c.OrderId = newOrderId;
            c.AuditingEnabled = false;    // → Build() is never called, no SQL, no parameters
        })
        .RunAsync(CancellationToken.None);

    Console.WriteLine("   audit command excluded — it contributed no SQL at all");

    // ── 5. Errors map to your own exception types ──────────────────────────────────────────────
    Section("5. Error mapping — a database error becomes a domain exception");

    try
    {
        await provider.GetRequiredService<IDapperPipeline>()
            .SetState(new UserSession { BranchId = 42, UserId = "alice" })
            .ResolveAndRegister<IAddLinesCommand>(c =>
            {
                c.OrderId = 999_999;      // no such order → FK violation
                c.Lines = lines;
            })
            .RunAsync(CancellationToken.None);

        Console.WriteLine("   (no error — FK enforcement is off?)");
    }
    catch (DuplicateOrderException ex)
    {
        Console.WriteLine($"   caught a DOMAIN exception, not a SqliteException:\n     {Truncate(ex.Message)}");
    }
    catch (PipelineException ex)
    {
        Console.WriteLine($"   unmapped DB error surfaced as PipelineException (code {ex.ErrorCode}):\n     {Truncate(ex.Message)}");
    }

    // ── 6. What the compiler stops you doing ───────────────────────────────────────────────────
    Section("6. Compile-time SQL injection prevention");

    Console.WriteLine("""
           builder.Append($"WHERE name = '{userInput}'");   ← does NOT compile
           builder.Append($"WHERE name = {Text(userInput)}"); ← binds a parameter

       A bare string in an interpolation hole has no overload. It is not a lint rule
       or a runtime check — the code simply does not build.
       """);

    Section("Done");
    Console.WriteLine($"   database: {dbPath}");
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(dbPath)) File.Delete(dbPath);
}

return;

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine($"── {title} ".PadRight(95, '─'));
}

static string Truncate(string s) => s.Length <= 90 ? s : s[..90] + "…";

static void CreateSchema(string connectionString)
{
    using var conn = new SqliteConnection(connectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        PRAGMA foreign_keys = ON;
        CREATE TABLE orders (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            customer   TEXT    NOT NULL,
            status     TEXT    NOT NULL,
            branch_id  INTEGER NOT NULL,
            created_by TEXT    NOT NULL
        );
        CREATE TABLE order_lines (
            order_id INTEGER NOT NULL REFERENCES orders(id),
            sku      TEXT    NOT NULL,
            qty      INTEGER NOT NULL
        );
        CREATE TABLE audit (order_id INTEGER NOT NULL, actor TEXT NOT NULL);
        """;
    cmd.ExecuteNonQuery();
}

/// <summary>Wraps every pipeline run. Runs outside the transaction, so it logs either way.</summary>
internal sealed class TimingBehavior : IPipelineBehavior
{
    public async Task ExecuteAsync(PipelineContext context, Func<Task> next, CancellationToken token)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await next();
        }
        finally
        {
            var included = string.Join(", ", context.CommandNames);
            var skipped = context.SkippedCommandNames.Count > 0
                ? $" (skipped: {string.Join(", ", context.SkippedCommandNames)})"
                : "";
            Console.WriteLine($"   ⏱  {sw.ElapsedMilliseconds}ms — [{included}]{skipped}");
        }
    }
}
