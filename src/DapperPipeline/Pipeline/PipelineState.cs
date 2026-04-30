using DapperPipeline.Abstractions;
using DapperPipeline.Interpolation;

namespace DapperPipeline.Pipeline;

internal sealed class PipelineState : IPipelineState
{
    private readonly Dictionary<Type, object> _state = [];
    private readonly Dictionary<string, object?> _bindings = [];

    // ----- Typed POCO state -----

    internal void Set<T>(T value) where T : class =>
        _state[typeof(T)] = value;

    public T Require<T>() where T : class =>
        Get<T>() ?? throw new InvalidOperationException(
            $"Pipeline state does not contain an entry for '{typeof(T).Name}'. " +
            $"Call SetState<{typeof(T).Name}>(value) before RunAsync.");

    public T? Get<T>() where T : class =>
        _state.TryGetValue(typeof(T), out var value) ? (T)value : null;

    // ----- Named scalar bindings -----

    internal void RegisterBinding(string name, object? value)
    {
        var key = StripAtSign(name);
        _bindings[key] = value;
    }

    public BoundParam<T> Bound<T>(string name)
    {
        var key = StripAtSign(name);
        if (!_bindings.TryGetValue(key, out var value))
            throw new InvalidOperationException(
                $"No bound parameter named '{name}'. Register via pipeline.Bind(\"{key}\", value) " +
                $"or pipeline.SetState<T>(state) for a POCO with a property of that name.");

        if (value is null)
        {
            // Null is acceptable for reference types and Nullable<T> generics
            if (default(T) is null)
                return new BoundParam<T>(key, default!);
            throw new InvalidOperationException(
                $"Bound parameter '{name}' is null but '{typeof(T).Name}' is a non-nullable value type.");
        }

        if (value is T typed)
            return new BoundParam<T>(key, typed);

        throw new InvalidOperationException(
            $"Bound parameter '{name}' is type '{value.GetType().Name}', not '{typeof(T).Name}'.");
    }

    private static string StripAtSign(string name) =>
        name.StartsWith('@') ? name[1..] : name;
}
