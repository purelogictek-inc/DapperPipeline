using DapperPipeline.Abstractions;

namespace DapperPipeline.Pipeline;

internal sealed class PipelineState : IPipelineState
{
    private readonly Dictionary<Type, object> _state = [];

    internal void Set<T>(T value) where T : class =>
        _state[typeof(T)] = value;

    public T Require<T>() where T : class =>
        Get<T>() ?? throw new InvalidOperationException(
            $"Pipeline state does not contain an entry for '{typeof(T).Name}'. " +
            $"Call SetState<{typeof(T).Name}>(value) before RunAsync.");

    public T? Get<T>() where T : class =>
        _state.TryGetValue(typeof(T), out var value) ? (T)value : null;
}