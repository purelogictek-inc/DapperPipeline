namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Thrown when a pipeline execution fails and no error mapper handles the error,
/// or when a command calls <c>EmitError</c> with no <c>OnError</c> handler registered.
/// </summary>
public sealed class PipelineException : Exception
{
    /// <summary>The database error code associated with this exception, or 0 if not applicable.</summary>
    public int ErrorCode { get; }

    /// <inheritdoc cref="PipelineException"/>
    public PipelineException(string message, int errorCode, Exception? inner) : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}