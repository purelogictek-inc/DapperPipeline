using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.Processing.Internal;

namespace DapperPipeline.Processing;

/// <summary>
/// Default <see cref="IDapperResultProcessor"/> implementation.
/// Registers typed result-set readers and exposes them to the pipeline via <see cref="Readers"/>.
/// </summary>
public sealed class DapperResultProcessor : IDapperResultProcessor
{
    private readonly List<BaseDapperResultReadScope> _scopes = [];

    /// <inheritdoc/>
    public bool CaughtError { get; set; }

    /// <inheritdoc/>
    public bool NeedsToProcess => _scopes.Count > 0;

    /// <inheritdoc/>
    public List<Action<SqlMapper.GridReader>> Readers
    {
        get
        {
            var readers = _scopes.Select(s => s.Reader).ToList();
            _scopes.Clear();
            return readers;
        }
    }

    // -------------------------------------------------------------------------
    // Read — DB type is the output type
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T>(Action<IEnumerable<T>> handler)
    {
        _scopes.Add(new DapperResultReadScope<T>(handler));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2>(Func<T1, T2, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3>(Func<T1, T2, T3, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4>(Func<T1, T2, T3, T4, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6>(Func<T1, T2, T3, T4, T5, T6, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7>(Func<T1, T2, T3, T4, T5, T6, T7, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T1>(map, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T1>(map, handler, splitOn));
        return this;
    }

    // -------------------------------------------------------------------------
    // Project — DB types are projected into a distinct domain type
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public IDapperResultProcessor Project<TDb, TOut>(Func<TDb, TOut> project, Action<IEnumerable<TOut>> handler)
    {
        _scopes.Add(new DapperResultReadScope<TDb>(items => handler(items.Select(project))));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, TOut>(Func<T1, T2, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, TOut>(Func<T1, T2, T3, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, TOut>(Func<T1, T2, T3, T4, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, TOut>(Func<T1, T2, T3, T4, T5, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, TOut>(Func<T1, T2, T3, T4, T5, T6, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TOut>(project, handler, splitOn));
        return this;
    }

    /// <inheritdoc/>
    public IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn)
    {
        _scopes.Add(new DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TOut>(project, handler, splitOn));
        return this;
    }
}