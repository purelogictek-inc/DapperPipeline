namespace DapperPipeline.Abstractions;

/// <summary>
/// Fluent mapper from a sequence of <typeparamref name="T"/> to a <see cref="System.Data.DataTable"/>
/// for use as a SQL table-valued parameter.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Boolean columns are stored as <c>int</c> (0/1) — SQL Server has no native bool type.</description></item>
/// <item><description>String columns are truncated to the specified max length before insertion.</description></item>
/// </list>
/// </remarks>
public interface IDataTableMapper<out T>
{
    /// <summary>Maps a column using a generic value selector.</summary>
    IDataTableMapper<T> Map<V>(string colName, Func<T, V> getVal, bool? allowNull = null);

    /// <summary>Maps a string column, truncating values to <paramref name="maxLength"/> characters.</summary>
    IDataTableMapper<T> String(string colName, int maxLength, Func<T, string?> getVal, bool? allowNull = null);

    /// <summary>Maps a primary key column with a generic value selector.</summary>
    IDataTableMapper<T> Primary<V>(string colName, Func<T, V> getVal);

    /// <summary>Maps an auto-incrementing integer primary key column.</summary>
    IDataTableMapper<T> Primary(string colName);

    /// <summary>Maps a string primary key column, truncating to <paramref name="maxLength"/> characters.</summary>
    IDataTableMapper<T> Primary(string colName, int maxLength, Func<T, string?> getVal);
}