namespace DapperPipeline.Commands;
using Abstractions;
using ErrorHandling;
using Pipeline;

/// <summary>
/// Base class for write-only pipeline commands (no result set).
/// Override <see cref="Build"/> to append SQL. Override <see cref="Process"/> if the
/// command needs to read result sets.
/// </summary>
public abstract class BaseQueryCommand : IQueryCommand
{
    /// <inheritdoc/>
    public virtual bool ShouldInclude(out string? reason) { reason = null; return true; }

    /// <inheritdoc/>
    public virtual string Name => GetType().Name;

    /// <inheritdoc/>
    public abstract void Build(IQueryBuilder builder, IPipelineState state);

    /// <inheritdoc/>
    public virtual void Process(IDapperResultProcessor processor) { }
}

/// <summary>
/// Base class for pipeline commands that return a typed result via <see cref="OnResult"/>.
/// </summary>
/// <typeparam name="T">The result type this command produces.</typeparam>
public abstract class BaseQueryCommand<T> : BaseQueryCommand, IQueryCommand<T>, IQueryCommandWithResultBinding
{
    private Action<T>? _onResult;
    private Action<string>? _onError;

    /// <inheritdoc/>
    public IQueryCommand<T> OnResult(Action<T> callback)
    {
        _onResult = callback;
        return this;
    }

    /// <inheritdoc/>
    public IQueryCommand<T> OnError(Action<string> handler)
    {
        _onError = handler;
        return this;
    }

    /// <inheritdoc cref="IQueryCommandWithResultBinding.HasResultBinding" />
    public bool HasResultBinding => _onResult != null;

    /// <summary>Invokes the registered <see cref="OnResult"/> callback with the command's result.</summary>
    protected void EmitResult(T result) => _onResult?.Invoke(result);

    /// <summary>
    /// If <paramref name="error"/> is non-empty, invokes <see cref="OnError"/> if registered,
    /// or throws <see cref="PipelineException"/> otherwise.
    /// Commands that return an error-string result set call this after reading the first result set.
    /// </summary>
    protected void EmitError(string? error)
    {
        if (string.IsNullOrEmpty(error)) return;
        if (_onError != null)
            _onError(error);
        else
            throw new PipelineException(error, errorCode: "", inner: null);
    }
}