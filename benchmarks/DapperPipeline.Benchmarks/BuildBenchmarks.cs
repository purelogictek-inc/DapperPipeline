using BenchmarkDotNet.Attributes;
using DapperPipeline.Abstractions;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Interpolation;
using DapperPipeline.QueryBuilding;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// The build path with <strong>no database in it at all</strong>.
/// </summary>
/// <remarks>
/// The end-to-end benchmarks measure ~160 μs of overhead per query, but a TCP round-trip is mixed
/// into that number and swamps it. This isolates the part we actually control: turning an
/// interpolated string into SQL + bound parameters. If the string handling is the problem, it shows
/// up here in nanoseconds and bytes, with nothing to hide behind.
/// </remarks>
[MemoryDiagnoser]
public class BuildBenchmarks
{
    private readonly SqliteDialect _dialect = new("Data Source=:memory:");

    private QueryBuilder NewBuilder() =>
        new(_dialect.Scanner, _dialect.RowSetRenderer, _dialect.DebugRenderer);

    private static IQueryBuilder Q(QueryBuilder b) => b;

    // Values a real command binds. Fields, not consts — consts get folded by the JIT.
    private long _orderId = 4711;
    private string _customer = "Contoso";
    private string _status = "new";
    private int _branchId = 42;
    private DateTime _from = new(2024, 1, 1);
    private DateTime _to = new(2024, 12, 31);
    private decimal _min = 10.5m;
    private decimal _max = 999.99m;

    /// <summary>The single hottest string routine: it runs once per bound parameter, per execution.</summary>
    [Benchmark]
    public int Sanitize_one_parameter_name()
    {
        var n = 0;
        foreach (var c in ParamNameSanitizer.GenerateCandidates("session.Branch.Id")) n += c.Length;
        return n;
    }

    /// <summary>One parameter bound through the full naming + registry path.</summary>
    [Benchmark]
    public string Bind_one_parameter()
    {
        var b = NewBuilder();
        b.BeginCommandScope(0);
        return b.BindValue(_orderId, "_orderId");
    }

    /// <summary>A realistic command: 8 bound parameters and a WHERE clause.</summary>
    [Benchmark]
    public string Build_a_realistic_command()
    {
        var b = NewBuilder();
        b.BeginCommandScope(0);

        var where = Q(b).Where(w =>
        {
            w.Append($"o.branch_id = {_branchId}");
            w.Append($"o.status = {Sql.Text(_status)}");
            w.Append($"o.created_at >= {_from}");
            w.Append($"o.created_at < {_to}");
            w.Append($"o.total BETWEEN {_min} AND {_max}");
        });

        Q(b).Append($"""
            SELECT o.id, o.customer, o.status
            FROM   orders o
            WHERE  o.id = {_orderId} AND o.customer = {Sql.Text(_customer)}
            {where}
            """);

        return b.Sql;
    }
}
