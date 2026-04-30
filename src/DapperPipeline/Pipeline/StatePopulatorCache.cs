using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace DapperPipeline.Pipeline;

/// <summary>
/// Caches per-state-type accessors that walk a POCO's scalar properties and emit
/// <c>(name, value)</c> pairs for pipeline binding. Uses <see cref="Expression"/>-compiled
/// delegates so subsequent calls bypass reflection.
/// </summary>
/// <remarks>
/// <para>
/// First call per type: builds the expression tree, compiles to a delegate (~1 ms).
/// Subsequent calls: invoke the delegate directly (~50 ns) — no reflection.
/// </para>
/// <para>
/// v1.1 will replace this with a Roslyn source generator for AOT compatibility (matching
/// Dapper's AOT support). The runtime API stays identical; consumers opt in by referencing
/// the analyzer NuGet package.
/// </para>
/// </remarks>
internal static class StatePopulatorCache
{
    private static readonly ConcurrentDictionary<Type, Action<object, Action<string, object?>>> _accessors = new();

    /// <summary>
    /// Walks the public scalar properties of <paramref name="state"/>, calling
    /// <paramref name="emit"/> with <c>(TypeNamePropertyName, value)</c> for each.
    /// </summary>
    public static void Populate<T>(T state, Action<string, object?> emit) where T : class
    {
        var accessor = _accessors.GetOrAdd(typeof(T), BuildAccessor);
        accessor(state, emit);
    }

    /// <summary>For tests: clear the cache (forces recompilation on next call).</summary>
    internal static void ClearCacheForTesting() => _accessors.Clear();

    private static Action<object, Action<string, object?>> BuildAccessor(Type t)
    {
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsScalarType(p.PropertyType))
            .ToArray();

        if (props.Length == 0)
            return static (_, _) => { };

        var stateParam = Expression.Parameter(typeof(object), "state");
        var emitParam = Expression.Parameter(typeof(Action<string, object?>), "emit");
        var castedState = Expression.Convert(stateParam, t);
        var emitInvoke = typeof(Action<string, object?>).GetMethod("Invoke")!;

        var calls = new List<Expression>(props.Length);
        foreach (var prop in props)
        {
            var nameConst = Expression.Constant($"{t.Name}{prop.Name}", typeof(string));
            var propAccess = Expression.Property(castedState, prop);
            // Box the property value as object? so the Action<string, object?> can accept it
            var boxed = Expression.Convert(propAccess, typeof(object));
            calls.Add(Expression.Call(emitParam, emitInvoke, nameConst, boxed));
        }

        var body = Expression.Block(calls);
        var lambda = Expression.Lambda<Action<object, Action<string, object?>>>(body, stateParam, emitParam);
        return lambda.Compile();
    }

    /// <summary>
    /// Returns true if <paramref name="t"/> is a scalar type that should be auto-bound from
    /// state. Includes primitives, <see cref="string"/>, common value types, enums, and
    /// nullable variants of all the above. Excludes reference types (services, complex POCOs)
    /// and collections.
    /// </summary>
    private static bool IsScalarType(Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t) ?? t;
        return underlying.IsPrimitive
            || underlying == typeof(string)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(decimal)
            || underlying.IsEnum;
    }
}
