using System.Data.Common;

namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Chains multiple <see cref="IErrorMapper"/> instances — the first non-null result wins.
/// </summary>
public sealed class CompositeErrorMapper : IErrorMapper
{
    private readonly IReadOnlyList<IErrorMapper> _mappers;

    /// <inheritdoc cref="CompositeErrorMapper"/>
    public CompositeErrorMapper(IEnumerable<IErrorMapper> mappers)
    {
        _mappers = mappers.ToList();
    }

    /// <inheritdoc />
    public Exception? Map(DbException exception, int errorCode)
    {
        foreach (var mapper in _mappers)
        {
            var result = mapper.Map(exception, errorCode);
            if (result != null) return result;
        }
        return null;
    }
}