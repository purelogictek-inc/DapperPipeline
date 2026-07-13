namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Thrown when a pipeline execution fails and no error mapper handles the error,
/// or when a command calls <c>EmitError</c> with no <c>OnError</c> handler registered.
/// </summary>
public sealed class PipelineException : Exception
{
    /// <summary>
    /// The database error code associated with this exception — a number as text on SQL Server /
    /// SQLite, a SQLSTATE on PostgreSQL. Empty when not applicable.
    /// </summary>
    public string ErrorCode { get; }

    /// <inheritdoc cref="PipelineException"/>
    public PipelineException(string message, string errorCode, Exception? inner) : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}