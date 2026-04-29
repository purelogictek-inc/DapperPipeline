namespace DapperPipeline.Abstractions;

/// <summary>
/// Wraps pipeline execution with cross-cutting concerns such as logging, telemetry, or auditing.
/// Behaviors form a chain — each receives a <c>next</c> delegate that invokes the
/// next behavior (or the SQL execution itself at the end of the chain).
/// </summary>
/// <remarks>
/// Register behaviors with DI in the order you want them applied outermost-first:
/// <code>
/// services.AddTransient&lt;IPipelineBehavior, TelemetryBehavior&gt;();
/// services.AddTransient&lt;IPipelineBehavior, LoggingBehavior&gt;();
/// // Call stack: Telemetry → Logging → SQL execution
/// </code>
/// Behaviors use whatever logging/tracing framework the consumer prefers —
/// the library does not prescribe one.
/// <code>
/// // Example using log4net in Warehouse.Operations:
/// public sealed class PipelineLoggingBehavior : IPipelineBehavior
/// {
///     private static readonly ILog Log = LogManager.GetLogger(typeof(PipelineLoggingBehavior));
///
///     public async Task ExecuteAsync(PipelineContext context, Func&lt;Task&gt; next, CancellationToken token)
///     {
///         var sw = Stopwatch.StartNew();
///         try
///         {
///             await next();
///             Log.Debug($"Pipeline completed in {sw.ElapsedMilliseconds}ms: {string.Join(", ", context.CommandNames)}");
///         }
///         catch (Exception ex)
///         {
///             Log.Error($"Pipeline failed after {sw.ElapsedMilliseconds}ms", ex);
///             throw;
///         }
///     }
/// }
/// </code>
/// </remarks>
public interface IPipelineBehavior
{
    /// <summary>
    /// Executes this behavior. Call <paramref name="next"/> to invoke the next behavior
    /// in the chain (or the SQL execution if this is the last behavior).
    /// Not calling <paramref name="next"/> short-circuits the pipeline — no SQL is executed.
    /// </summary>
    Task ExecuteAsync(PipelineContext context, Func<Task> next, CancellationToken token);
}
