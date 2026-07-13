using DapperPipeline.Abstractions;
using DapperPipeline.Interpolation;
using DapperPipeline.RowSets;

namespace DapperPipeline.QueryBuilding;

internal sealed partial class QueryBuilder
{
    /// <inheritdoc />
    public ISqlRowSet RowSet<T>(string alias, IEnumerable<T> rows, Action<IRowSetColumnBuilder<T>> map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);

        // The alias lands in raw SQL, so it must be a safe identifier — same rule as Sql.Identifier.
        // SqlIdentifier's constructor is the validator; it throws on anything unsafe.
        _ = new SqlIdentifier(alias);

        var materialized = rows as IReadOnlyList<T> ?? rows.ToList();

        var columns = new RowSetColumnBuilder<T>(materialized);
        map(columns);

        var built = columns.Build();
        if (built.Count == 0)
            throw new InvalidOperationException(
                $"Rowset '{alias}' declares no columns — call map.Column(...) at least once.");

        return new RowSet(alias, built, materialized.Count);
    }

    /// <summary>
    /// Renders <paramref name="rowSet"/> through the dialect and writes the resulting table
    /// expression into the SQL. Called by the interpolation handler for <c>{rowSet}</c>.
    /// </summary>
    public void EmitRowSet(ISqlRowSet rowSet)
    {
        var sql = rowSetRenderer.Render(rowSet, new BindContext(this));
        _fullSql.Append(sql);
    }

    /// <summary>
    /// The narrow binding surface handed to a renderer: it may bind values and learn their names,
    /// and nothing else. Renderers live in dialect assemblies, so this has to be public API —
    /// which is exactly why it is this small.
    /// </summary>
    private sealed class BindContext(QueryBuilder owner) : IRowSetBindContext
    {
        public string Bind(object? value, string nameHint)
        {
            var sanitized = ParamNameSanitizer.GenerateCandidates(nameHint).First();
            var baseName = $"@p{owner._scopeIndex:D3}_{sanitized}";

            // Rowsets bind many values under the same hint (one per row, or one array per column),
            // so ordinals are the norm here rather than the exception.
            var name = baseName;
            for (var n = 2; owner._parameters.ContainsKey(name); n++)
                name = $"{baseName}__{n}";

            owner._parameters[name] = value;
            return name;
        }
    }
}
