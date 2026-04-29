using Dapper;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Fluent builder for SQL table-valued parameters.
/// </summary>
public interface IQueryBuilderTableBuilder
{
    /// <summary>Converts the built table to a Dapper TVP.</summary>
    SqlMapper.ICustomQueryParameter ToTableParam();

    /// <summary>Fills a single column from a sequence of scalar values.</summary>
    IQueryBuilderTableBuilder Fill<T>(string columnName, IEnumerable<T> cells);

    /// <summary>Fills rows using a converter that maps each element to a column value array.</summary>
    IQueryBuilderTableBuilder Fill<T>(IEnumerable<T> rows, Func<T, object[]> convert);

    /// <summary>Adds a column to the table schema.</summary>
    IQueryBuilderTableBuilder WithColumn<T>(string name, bool makePrimary = false);
}