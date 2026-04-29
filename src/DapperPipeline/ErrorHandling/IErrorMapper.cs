using System.Data.Common;

namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Maps a database exception to a consumer-defined exception.
/// Return <c>null</c> if this mapper does not handle the given exception.
/// </summary>
public interface IErrorMapper
{
    /// <summary>
    /// Maps <paramref name="exception"/> to a consumer exception, or returns <c>null</c>
    /// if this mapper does not handle it.
    /// </summary>
    /// <param name="exception">The database exception.</param>
    /// <param name="errorCode">The dialect-specific error code extracted by <c>IDatabaseDialect.ExtractErrorCode</c>.</param>
    Exception? Map(DbException exception, int errorCode);
}