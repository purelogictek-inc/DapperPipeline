namespace DapperPipeline.Abstractions;

/// <summary>
/// A single unit of SQL work registered with the pipeline.
/// Commands build SQL + parameters in <see cref="Build"/> and register result readers in <see cref="Process"/>.
/// </summary>
public interface IQueryCommand
{
    /// <summary>Appends SQL and registers parameters on the shared query builder.</summary>
    void Build(IQueryBuilder builder, IPipelineState state);

    /// <summary>
    /// Registers result-set readers on the processor.
    /// Called once per command after the SQL batch is executed.
    /// </summary>
    void Process(IDapperResultProcessor processor);

    /// <summary>
    /// Returns <c>false</c> to exclude this command from the current pipeline run.
    /// The pipeline skips <see cref="Build"/> and <see cref="Process"/> for excluded commands.
    /// </summary>
    /// <param name="reason">
    /// Set to a human-readable explanation when returning <c>false</c>.
    /// The pipeline logs this at Debug level so developers can see exactly which check failed.
    /// Set to <c>null</c> when returning <c>true</c>.
    /// </param>
    /// <example>
    /// <code>
    /// public override bool ShouldInclude(out string? reason)
    /// {
    ///     if (!customerId.HasValue)  { reason = "CustomerId not set";       return false; }
    ///     if (items.Count == 0)      { reason = "No items to process";      return false; }
    ///     reason = null;
    ///     return true;
    /// }
    /// </code>
    /// </example>
    bool ShouldInclude(out string? reason);

    /// <summary>Display name used in diagnostic messages and pre-run validation errors.</summary>
    string Name { get; }
}

/// <summary>
/// A command that produces a typed result, delivered via <see cref="OnResult"/>.
/// </summary>
public interface IQueryCommand<T> : IQueryCommand
{
    /// <summary>Registers the callback that receives the command's result after the pipeline runs.</summary>
    IQueryCommand<T> OnResult(Action<T> callback);

    /// <summary>
    /// Registers an optional error handler for commands that return an error-string result set.
    /// If not set and the command calls <c>EmitError</c> with a non-empty string, a
    /// <see cref="ErrorHandling.PipelineException"/> is thrown.
    /// </summary>
    IQueryCommand<T> OnError(Action<string> handler);

    /// <summary>Returns <c>true</c> if <see cref="OnResult"/> has been called.</summary>
    bool HasResultBinding { get; }
}