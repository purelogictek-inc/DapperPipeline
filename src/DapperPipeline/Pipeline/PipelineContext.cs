namespace DapperPipeline.Abstractions;

/// <summary>
/// Contextual information about a pipeline run, passed to each <see cref="IPipelineBehavior"/>.
/// </summary>
public sealed class PipelineContext
{
    /// <summary>
    /// The names of commands that will participate in this run (i.e. <see cref="IQueryCommand.ShouldInclude"/> returned <c>true</c>).
    /// </summary>
    public IReadOnlyList<string> CommandNames { get; init; } = [];

    /// <summary>
    /// The names of commands excluded from this run because <see cref="IQueryCommand.ShouldInclude"/> returned <c>false</c>.
    /// </summary>
    public IReadOnlyList<string> SkippedCommandNames { get; init; } = [];
}