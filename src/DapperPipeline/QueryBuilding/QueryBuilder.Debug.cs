using System.Dynamic;
using DapperPipeline.Abstractions;

namespace DapperPipeline.QueryBuilding;

internal sealed partial class QueryBuilder
{
    /// <inheritdoc />
    public string ToDebug()
    {
        var parameters = Parameters is ExpandoObject expando
            ? (IDictionary<string, object?>)expando
            : new Dictionary<string, object?>();

        return debugRenderer.Render(Sql, parameters.AsReadOnly());
    }
}
