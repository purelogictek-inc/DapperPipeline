namespace DapperPipeline.Tests.Documentation;

/// <summary>
/// The context the README's examples are written against: the domain types and locals they assume
/// exist. <see cref="ReadmeCompilationTests"/> compiles every C# block from README.md inside this
/// scaffold, so a documented example that does not compile fails the build.
/// </summary>
/// <remarks>
/// This exists because the README has repeatedly been a bug source in its own right — it has shipped
/// <c>m.Add(selector)</c> (no such method), <c>splitOn: "x"</c> (a named argument cannot bind to a
/// <c>params</c> parameter), <c>using static DapperPipeline.Sql</c> (wrong namespace) and
/// <c>"dbo.OrderLineType"</c> (double-qualified at runtime). All four would have failed here.
/// </remarks>
internal static class ReadmeScaffoldText
{
    /// <summary>Prelude compiled ahead of every README snippet.</summary>
    public const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Data.Common;
        using System.Diagnostics;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using DapperPipeline.Abstractions;
        using DapperPipeline.Commands;
        using DapperPipeline.DependencyInjection;
        using DapperPipeline.Dialects.PostgreSql;
        using DapperPipeline.Dialects.Sqlite;
        using DapperPipeline.Dialects.SqlServer;
        using DapperPipeline.ErrorHandling;
        using DapperPipeline.Interpolation;
        using DapperPipeline.Pipeline;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;
        using static DapperPipeline.Interpolation.Sql;

        namespace ReadmeScope;

        // ---- domain types the README's examples refer to ----

        public sealed class Order
        {
            public Order() { }
            public Order(OrderRecord r) { }
            public long Id { get; set; }
            public List<OrderLine> Lines { get; } = new();
            public Branch? Branch { get; set; }
        }

        public sealed class OrderLine
        {
            public long ProductId { get; set; }
            public int Qty { get; set; }
            public decimal Price { get; set; }
        }

        public sealed class OrderRecord { public long Id { get; set; } }
        public sealed class BranchRecord { public long BranchId { get; set; } }
        public sealed class Branch { public Branch() { } public Branch(BranchRecord r) { } }

        public sealed class UserSession
        {
            public long BranchId { get; init; }
            public string UserId { get; init; } = "";
            public bool IsAdmin { get; init; }
        }

        public interface IGetOrderCommand : IQueryCommand<Order?> { long OrderId { get; set; } }
        public interface ILogOrderViewCommand : IQueryCommand { long OrderId { get; set; } }
        public interface ICmd : IQueryCommand { }
        public interface IMyCommand : IQueryCommand { }

        public sealed record TableName(string Value) : ISqlIdentifier;
        public static class Tables { public static readonly TableName Orders = new("Orders"); }

        public sealed class MyAppException : Exception { public MyAppException(int offset, string m) : base(m) { } }
        public sealed class DomainException : Exception { public DomainException(string m) : base(m) { } }
        public sealed class DuplicateKeyException : Exception { public DuplicateKeyException(string m) : base(m) { } }
        public sealed class ConstraintViolationException : Exception { public ConstraintViolationException(string s, string m) : base(m) { } }
        public sealed class MetricsBehavior : IPipelineBehavior
        {
            public Task ExecuteAsync(PipelineContext context, Func<Task> next, CancellationToken token) => next();
        }

        public sealed class ExternalIdRow { public string Source { get; set; } = ""; public int ExternalId { get; set; } }

        public sealed class MyDialect : IDatabaseDialect
        {
            public MyDialect(string connectionString) { }
            public DbConnection CreateConnection() => throw new NotSupportedException();
            public IParameterScanner Scanner => throw new NotSupportedException();
            public string PipelinePreamble => "";
            public bool ShouldRetry(DbException exception) => false;
            public string ExtractErrorCode(DbException exception) => "";
        }


        public sealed class Filter { public long? StatusId { get; set; } public bool OnlyPending { get; set; } }
        public sealed class Feature { public bool IsEnabled { get; set; } }

        // ---- the ambient locals a snippet may use ----

        public static class Ambient
        {
            public static readonly IServiceCollection services = new ServiceCollection();
            public static readonly IDapperPipeline pipeline = null!;
            public static readonly IQueryBuilder builder = null!;
            public static readonly IPipelineState state = null!;
            public static readonly IDapperResultProcessor processor = null!;
            public static readonly string connectionString = "";
            public static readonly string conn = "";
            public static readonly CancellationToken token = default;
            public static readonly long orderId = 1;
            public static readonly long branchId = 1;
            public static readonly long customerId = 1;
            public static readonly string userId = "";
            public static readonly string status = "";
            public static readonly Guid correlationId = Guid.Empty;
            public static readonly Filter filter = new();
            public static readonly Feature feature = new();
            public static readonly List<OrderLine> lines = new();
            public static readonly List<long> orderIds = new();
            public static readonly List<long> ids = new();
            public static readonly List<ExternalIdRow> externalIds = new();
            public static readonly IConfigLike config = null!;
        }

        public interface IConfigLike { IConfigSection GetSection(string name); }
        public interface IConfigSection { string this[string key] { get; } }
        """;

    /// <summary>
    /// Opens a command class. Snippets compile <em>inside a command</em>, because that is where the
    /// README's examples actually live — it is what puts <c>EmitResult</c> / <c>EmitError</c>
    /// (protected on <c>BaseQueryCommand&lt;T&gt;</c>) in scope.
    /// </summary>
    public const string CommandOpen = """

        public sealed class Snippet : BaseQueryCommand<Order?>
        {
            private readonly long? customerId = 1;
            private readonly List<OrderLine> items = new();
            private readonly Feature feature = new();
            private readonly IGetOrderCommand cmd = null!;
            private readonly Filter filter = new();
            private readonly string status = "";

        """;

    public const string BuildStub = """

            public override void Build(IQueryBuilder builder, IPipelineState state) { }
        """;

    public const string ProcessStub = """

            public override void Process(IDapperResultProcessor processor) { }
        """;

    /// <summary>Wraps a statement-shaped snippet in a method body inside that command.</summary>
    public const string StatementOpen = """

            public async Task Run()
            {
                var services = Ambient.services;
                var pipeline = Ambient.pipeline;
                var builder = Ambient.builder;
                var state = Ambient.state;
                var processor = Ambient.processor;
                var connectionString = Ambient.connectionString;
                var conn = Ambient.conn;
                var token = Ambient.token;
                var orderId = Ambient.orderId;
                var branchId = Ambient.branchId;
                var userId = Ambient.userId;
                var status = Ambient.status;
                var correlationId = Ambient.correlationId;
                var filter = Ambient.filter;
                var lines = Ambient.lines;
                var orderIds = Ambient.orderIds;
                var ids = Ambient.ids;
                var externalIds = Ambient.externalIds;
                var config = Ambient.config;
                await Task.CompletedTask;
        """;

    public const string StatementClose = """
            }
        """;

    public const string CommandClose = """
        }
        """;
}
