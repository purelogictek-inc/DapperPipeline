using Dapper;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Collects result-set readers registered by command <c>Process()</c> implementations.
/// The pipeline drains <see cref="Readers"/> once per command after executing the SQL batch.
/// </summary>
/// <remarks>
/// <para>
/// Two families of methods are available:
/// </para>
/// <list type="bullet">
/// <item>
///   <term><c>Read</c></term>
///   <description>
///     The DB type is the output type. Use when Dapper maps rows directly to the domain type,
///     or when joining multiple DB types whose root type is the output.
///     <code>
///     // single type
///     processor.Read&lt;Order&gt;(orders => EmitResult(orders.FirstOrDefault()));
///
///     // join — T1 is the output type
///     processor.Read&lt;Order, OrderLine&gt;(
///         (o, l) => { o.Lines.Add(l); return o; },
///         orders => EmitResult(orders.FirstOrDefault()));
///     </code>
///   </description>
/// </item>
/// <item>
///   <term><c>Project</c></term>
///   <description>
///     The DB type(s) are projected into a distinct domain type. Use when SQL returns a flat
///     record or multiple joined records that must be shaped into a richer domain object.
///     <code>
///     // single flat record → domain type
///     processor.Project&lt;ProductRecord, Product&gt;(
///         r => new Product(r),
///         products => EmitResult(products.FirstOrDefault()));
///
///     // join → domain type
///     processor.Project&lt;ProductRecord, Vendor, Product&gt;(
///         (r, v) => new Product(r) { Vendor = v },
///         products => EmitResult(products.FirstOrDefault()));
///     </code>
///   </description>
/// </item>
/// </list>
/// </remarks>
public interface IDapperResultProcessor
{
    // Pipeline-internal — read by DapperPipeline after each command.

    /// <summary>Set to <c>true</c> when the SQL batch throws; reset to <c>false</c> in the finally block.</summary>
    bool CaughtError { get; set; }

    /// <summary>Returns <c>true</c> if at least one reader is registered.</summary>
    bool NeedsToProcess { get; }

    /// <summary>
    /// Extracts all registered readers and clears the internal list in one pass.
    /// The pipeline calls this once per command to drain pending readers.
    /// </summary>
    List<Action<SqlMapper.GridReader>> Readers { get; }

    // -------------------------------------------------------------------------
    // Read — DB type is the output type
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads one result set, mapping each row to <typeparamref name="T"/>.
    /// The output type is <typeparamref name="T"/>.
    /// </summary>
    IDapperResultProcessor Read<T>(Action<IEnumerable<T>> handler);

    /// <summary>
    /// Reads one result set using Dapper multi-mapping across two DB types.
    /// <typeparamref name="T1"/> is the output type.
    /// </summary>
    IDapperResultProcessor Read<T1, T2>(Func<T1, T2, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <summary>
    /// Reads a parent/child join, folding every child into <strong>one</strong> parent per key.
    /// </summary>
    /// <param name="key">Identifies a parent — usually its primary key.</param>
    /// <param name="addChild">Attaches a child to its parent. Not called when the child is null (an outer join with no match).</param>
    /// <param name="handler">Receives each distinct parent, in the order the rows arrived.</param>
    /// <param name="splitOn">Where a new type begins in the row. <c>params</c> — pass positionally.</param>
    /// <remarks>
    /// <para>
    /// Use this instead of <c>Read&lt;T1, T2&gt;</c> whenever a parent has many children. Dapper's
    /// multi-map invokes the mapping function <strong>once per row</strong> and materializes a
    /// <strong>fresh parent every time</strong>, so the obvious fold —
    /// </para>
    /// <code>
    /// Read&lt;Order, OrderLine&gt;((o, l) => { o.Lines.Add(l); return o; }, rows => Emit(rows.FirstOrDefault()))
    /// </code>
    /// <para>
    /// — produces N <em>different</em> orders each holding ONE line, and then hands you whichever one
    /// came first. A five-line order silently arrives with one line. It compiles and it does not
    /// throw; the data is just wrong. This method keeps the first parent per key and folds every
    /// child into it.
    /// </para>
    /// <example>
    /// <code>
    /// processor.ReadGrouped&lt;Order, OrderLine, long&gt;(
    ///     o => o.Id,
    ///     (o, l) => o.Lines.Add(l),
    ///     rows => EmitResult(rows.FirstOrDefault()),
    ///     "order_id");
    /// </code>
    /// </example>
    /// </remarks>
    IDapperResultProcessor ReadGrouped<TParent, TChild, TKey>(
        Func<TParent, TKey> key,
        Action<TParent, TChild> addChild,
        Action<IEnumerable<TParent>> handler,
        params string[] splitOn)
        where TKey : notnull;

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3>(Func<T1, T2, T3, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4>(Func<T1, T2, T3, T4, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6>(Func<T1, T2, T3, T4, T5, T6, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7>(Func<T1, T2, T3, T4, T5, T6, T7, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    /// <inheritdoc cref="Read{T1,T2}"/>
    IDapperResultProcessor Read<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T1> map, Action<IEnumerable<T1>> handler, params string[] splitOn);

    // -------------------------------------------------------------------------
    // Project — DB types are projected into a distinct domain type
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads one result set as <typeparamref name="T1"/> and projects each row to
    /// <typeparamref name="TOut"/> via <paramref name="project"/>.
    /// Use when SQL returns a flat DB record that must be shaped into a domain type.
    /// </summary>
    /// <typeparam name="T1">The type Dapper maps each row to from the DB.</typeparam>
    /// <typeparam name="TOut">The domain type produced by the projection.</typeparam>
    /// <example>
    /// <code>
    /// processor.Project&lt;ProductRecord, Product&gt;(
    ///     r => new Product(r),
    ///     products => EmitResult(products.FirstOrDefault()));
    /// </code>
    /// </example>
    IDapperResultProcessor Project<T1, TOut>(Func<T1, TOut> project, Action<IEnumerable<TOut>> handler);

    /// <summary>
    /// Reads one result set using Dapper multi-mapping across two DB types and projects to
    /// <typeparamref name="TOut"/> via <paramref name="project"/>.
    /// Use when SQL joins two tables whose combined data shapes a single domain type.
    /// </summary>
    /// <typeparam name="T1">First DB type (typically the root / left side of the join).</typeparam>
    /// <typeparam name="T2">Second DB type (typically the joined / right side).</typeparam>
    /// <typeparam name="TOut">The domain type produced by the projection.</typeparam>
    /// <example>
    /// <code>
    /// processor.Project&lt;ProductRecord, Vendor, Product&gt;(
    ///     (r, v) => new Product(r) { Vendor = v },
    ///     products => EmitResult(products.FirstOrDefault()),
    ///     splitOn: "VendorId");
    /// </code>
    /// </example>
    IDapperResultProcessor Project<T1, T2, TOut>(Func<T1, T2, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <summary>
    /// Reads one result set using Dapper multi-mapping across three DB types and projects to
    /// <typeparamref name="TOut"/> via <paramref name="project"/>.
    /// </summary>
    /// <typeparam name="T1">First DB type.</typeparam>
    /// <typeparam name="T2">Second DB type.</typeparam>
    /// <typeparam name="T3">Third DB type.</typeparam>
    /// <typeparam name="TOut">The domain type produced by the projection.</typeparam>
    IDapperResultProcessor Project<T1, T2, T3, TOut>(Func<T1, T2, T3, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, TOut>(Func<T1, T2, T3, T4, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, TOut>(Func<T1, T2, T3, T4, T5, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, TOut>(Func<T1, T2, T3, T4, T5, T6, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);

    /// <inheritdoc cref="Project{T1,T2,T3,TOut}"/>
    IDapperResultProcessor Project<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TOut>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TOut> project, Action<IEnumerable<TOut>> handler, params string[] splitOn);
}
