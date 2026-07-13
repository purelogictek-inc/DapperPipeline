namespace DapperPipeline.Abstractions;

/// <summary>
/// Declares the columns of a rowset. Each column's SQL type is inferred from the type its selector
/// returns, so consumers never name a database type.
/// </summary>
/// <typeparam name="T">The row type being projected.</typeparam>
public interface IRowSetColumnBuilder<out T>
{
    /// <summary>
    /// Adds a column, projecting one value per row.
    /// </summary>
    /// <param name="name">The column name the SQL will use (e.g. <c>external_id</c>).</param>
    /// <param name="selector">Projects the column's value from a row.</param>
    /// <typeparam name="TValue">
    /// The column's CLR type. The dialect maps it to a SQL type — you do not name one.
    /// </typeparam>
    IRowSetColumnBuilder<T> Column<TValue>(string name, Func<T, TValue> selector);
}
