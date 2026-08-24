using Dapper;

namespace DapperPipeline.Processing.Internal;

internal abstract class BaseDapperResultReadScope(params string[] splitOn)
{
    protected readonly string SplitOn = splitOn is {Length: > 0} ? string.Join(",", splitOn) : "Id";

    public abstract Action<SqlMapper.GridReader> Reader { get; }

    /// <summary>
    /// Hands materialized rows to the command's own callback, marking anything it throws as coming
    /// from <em>consumer</em> code rather than from reading the result set.
    /// </summary>
    /// <remarks>
    /// The pipeline decorates reader failures with alignment diagnostics, because a reader that
    /// fails while materializing is the signature of results having crossed between commands. A
    /// callback that inspects its rows and refuses them — a domain rule rejecting bad data — is
    /// nothing of the sort, and must reach the caller with its own type and stack intact. This
    /// marker is what lets the pipeline tell the two apart; it never escapes the assembly.
    /// </remarks>
    protected static void Emit<T>(Action<T> handler, T rows)
    {
        try
        {
            handler(rows);
        }
        catch (Exception ex)
        {
            throw new ResultHandlerException(ex);
        }
    }
}

/// <summary>
/// Internal marker: the exception it carries came from a command's result callback, not from
/// reading the result set. Unwrapped by the pipeline, which rethrows the original untouched.
/// </summary>
internal sealed class ResultHandlerException(Exception inner)
    : Exception("A result handler threw.", inner);

/// <summary>
/// Generic scope — handles any number of mapped types via <c>Type[]</c> + <c>Func&lt;object[], R&gt;</c>.
/// Also used as the simple single-type reader when no mapper is provided.
/// </summary>
internal sealed class DapperResultReadScope<R>(
    Type[]? types,
    Func<object[], R>? mapper,
    Action<IEnumerable<R>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    /// <summary>Simple single-type constructor — no mapper needed.</summary>
    public DapperResultReadScope(Action<IEnumerable<R>> handler) : this(null, null, handler) { }

    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = mapper != null ? gr.Read(types!, mapper, SplitOn) : gr.Read<R>();
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, TReturn>(
    Func<T1, T2, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read(mapper, SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, TReturn>(
    Func<T1, T2, T3, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read(mapper, SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, TReturn>(
    Func<T1, T2, T3, T4, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read(mapper, SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, TReturn>(
    Func<T1, T2, T3, T4, T5, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read(mapper, SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read(mapper, SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read(mapper, SplitOn);
        Emit(handler, dbResult);
    };
}

// T8–T15: Dapper's typed overloads cap at T7; use the object-array overload for higher arities.

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7]), SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7], (T9)o[8]), SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7], (T9)o[8], (T10)o[9]), SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7], (T9)o[8], (T10)o[9], (T11)o[10]), SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7], (T9)o[8], (T10)o[9], (T11)o[10], (T12)o[11]), SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7], (T9)o[8], (T10)o[9], (T11)o[10], (T12)o[11], (T13)o[12]), SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7], (T9)o[8], (T10)o[9], (T11)o[10], (T12)o[11], (T13)o[12], (T14)o[13]), SplitOn);
        Emit(handler, dbResult);
    };
}

internal sealed class DapperResultReadScope<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn>(
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn> mapper,
    Action<IEnumerable<TReturn>> handler,
    params string[] splitOn) : BaseDapperResultReadScope(splitOn)
{
    private static readonly Type[] Types = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14), typeof(T15)];
    public override Action<SqlMapper.GridReader> Reader => gr =>
    {
        var dbResult = gr.Read<TReturn>(Types, o => mapper((T1)o[0], (T2)o[1], (T3)o[2], (T4)o[3], (T5)o[4], (T6)o[5], (T7)o[6], (T8)o[7], (T9)o[8], (T10)o[9], (T11)o[10], (T12)o[11], (T13)o[12], (T14)o[13], (T15)o[14]), SplitOn);
        Emit(handler, dbResult);
    };
}