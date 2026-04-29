using System.Data;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Fluent SQL builder. Commands call this in their <c>Build</c> method to register
/// parameters and append SQL. The pipeline batches all commands into a single SQL string
/// and executes them in one round-trip within a transaction.
/// </summary>
/// <remarks>
/// Parameter names are passed without the <c>@</c> prefix to <see cref="Add(string,object)"/>,
/// or with the prefix in SQL strings passed to <see cref="Add(string)"/>.
/// The builder's parameter scanner rewrites scoped parameters automatically — commands do not
/// need to manage name collisions between batched commands.
/// </remarks>
public interface IQueryBuilder
{
    // -------------------------------------------------------------------------
    // Parameters
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a named parameter. The name should include the <c>@</c> prefix.
    /// The scanner will rewrite it in subsequent SQL strings with the command's scope suffix.
    /// </summary>
    IQueryBuilder Add(string paramName, object? paramValue);

    /// <summary>
    /// Appends a SQL statement. The scanner processes all <c>@param</c> tokens,
    /// rewrites scoped parameters, and throws on unknown ones.
    /// </summary>
    IQueryBuilder Add(string command);

    /// <summary>
    /// Appends SQL inline (no trailing semicolon or newline).
    /// Use inside CTE blocks or for partial SQL fragments.
    /// </summary>
    IQueryBuilder Append(string command);

    /// <summary>Replaces a placeholder token in the accumulated SQL.</summary>
    IQueryBuilder Replace(string key, object clause);

    // -------------------------------------------------------------------------
    // Table-valued parameters
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a collection of <typeparamref name="T"/> to a SQL table-valued parameter
    /// using the fluent <see cref="IDataTableMapper{T}"/> setup action.
    /// </summary>
    IQueryBuilder MapTable<T>(string paramName, string tableType, IEnumerable<T> source,
        Action<IDataTableMapper<T>> setup);

    /// <summary>Adds a pre-built <see cref="DataTable"/> as a table-valued parameter.</summary>
    IQueryBuilder AddTableParam(string paramName, DataTable table);

    /// <summary>
    /// Adds a simple scalar collection as a table-valued parameter with a single column.
    /// </summary>
    IQueryBuilder AddTableParam<T>(string paramName, IEnumerable<T> values, string columnName = "Id");

    // -------------------------------------------------------------------------
    // Conditional SQL
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens a fluent <c>IF / ELSE IF / ELSE</c> decision block.
    /// All clauses and the optional <c>ELSE</c> are registered inside the <paramref name="builder"/> action.
    /// </summary>
    IQueryBuilder If(Action<IQueryBuilderDecisionBuilder> builder);

    /// <summary>Adds a simple <c>IF (<paramref name="clause"/>) BEGIN ... END</c> block.</summary>
    IQueryBuilder If(string clause, Action<IQueryBuilder> ifBlock, Action<IQueryBuilder>? elseBlock = null);

    /// <summary>Adds an <c>IF NOT EXISTS (<paramref name="clause"/>) BEGIN ... END</c> block.</summary>
    IQueryBuilder IfNotExists(string clause, Action<IQueryBuilder> ifBlock, Action<IQueryBuilder>? elseBlock = null);

    // -------------------------------------------------------------------------
    // WHERE / JOIN
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a WHERE / JOIN clause. The returned <see cref="IWhereBuilder"/> can be embedded
    /// in a subsequent <see cref="Add(string)"/> call via its <c>ToString()</c>.
    /// </summary>
    IWhereBuilder Where(Action<IWhereBuilder>? builder = null, bool upCase = false);

    /// <summary>
    /// Builds a WHERE clause starting from an existing <paramref name="source"/> builder.
    /// </summary>
    IWhereBuilder Where(IWhereBuilder source, Action<IWhereBuilder>? builder = null, bool upCase = false);

    /// <summary>
    /// Builds a WHERE clause and exposes it via <paramref name="where"/> for later embedding,
    /// while returning <c>this</c> for fluent chaining.
    /// </summary>
    IQueryBuilder Where(out IWhereBuilder where, Action<IWhereBuilder>? builder = null, bool upCase = false);

    /// <summary>Returns a new <see cref="IWhereBuilder"/> cloned from <paramref name="source"/>
    /// with <paramref name="target"/> replaced by <paramref name="replacement"/>.</summary>
    IWhereBuilder Where(IWhereBuilder source, string target, string replacement);

    // -------------------------------------------------------------------------
    // CTEs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a Common Table Expression block.
    /// SQL inside the CTE action is appended without semicolons.
    /// </summary>
    IQueryBuilder WithCte(string name, Action<IQueryBuilder> cte, string fields = "",
        string description = "", bool first = false, bool terminate = false);

    // -------------------------------------------------------------------------
    // Indentation (used internally by If/Where/CTE blocks)
    // -------------------------------------------------------------------------

    /// <summary>Increases the current indentation level.</summary>
    IQueryBuilder AddIndent();

    /// <summary>Decreases the current indentation level.</summary>
    IQueryBuilder RemoveIndent();

    // -------------------------------------------------------------------------
    // Inspection / pipeline use
    // -------------------------------------------------------------------------

    /// <summary>Returns <c>true</c> if any SQL has been added.</summary>
    bool HasQuery { get; }

    /// <summary>Returns <c>true</c> if the SQL contains unreplaced placeholder tokens.</summary>
    bool HasReplaceableStatements { get; }

    /// <summary>The accumulated SQL string.</summary>
    string Sql { get; }

    /// <summary>The accumulated Dapper parameters.</summary>
    object Parameters { get; }

    /// <summary>Removes unused parameters from the parameter bag (substring filter).</summary>
    void Optimize();

    /// <summary>Clears all SQL and parameters.</summary>
    void Clear();

    /// <summary>Returns a debug-friendly SQL string with DECLARE/SET statements for all parameters.</summary>
    string ToDebug();
}