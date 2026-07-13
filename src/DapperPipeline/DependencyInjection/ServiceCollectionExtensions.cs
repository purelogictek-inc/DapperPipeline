using System.Reflection;
using DapperPipeline.Abstractions;
using DapperPipeline.Pipeline;
using DapperPipeline.Processing;
using DapperPipeline.QueryBuilding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DapperPipeline.DependencyInjection;

/// <summary>
/// Extension methods for registering DapperPipeline services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DapperPipeline core services with the provided <paramref name="dialect"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="dialect">The database dialect (connection, scanner, retry, error code extraction).</param>
    /// <remarks>
    /// After calling this method, register your <see cref="IQueryCommand"/> implementations and
    /// optionally an <c>IErrorMapper</c> to map <see cref="System.Data.Common.DbException"/>
    /// instances to domain exceptions.
    /// </remarks>
    public static IServiceCollection AddDapperPipeline(
        this IServiceCollection services,
        IDatabaseDialect dialect)
    {
        services.AddSingleton(dialect);
        RegisterCore(services);
        return services;
    }

    /// <summary>
    /// Registers the DapperPipeline core services, resolving the dialect from the container.
    /// </summary>
    public static IServiceCollection AddDapperPipeline<TDialect>(this IServiceCollection services)
        where TDialect : class, IDatabaseDialect
    {
        services.AddSingleton<IDatabaseDialect, TDialect>();
        RegisterCore(services);
        return services;
    }

    /// <summary>
    /// Scans <paramref name="assemblies"/> and registers all concrete <see cref="IQueryCommand"/>
    /// implementations as transient services, keyed by their specific command interfaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each concrete command type is registered against every interface it implements that
    /// extends <see cref="IQueryCommand"/>, excluding <see cref="IQueryCommand"/> itself and
    /// <c>IQueryCommand&lt;T&gt;</c> (too broad to be useful as a DI key).
    /// </para>
    /// <para>
    /// For Autofac users, use <c>RegisterAssemblyTypes</c> with
    /// <c>.Where(t => typeof(IQueryCommand).IsAssignableFrom(t)).AsImplementedInterfaces()</c>
    /// instead of this method.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDapperPipelineCommands(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var commandType = typeof(IQueryCommand);
        var genericCommandType = typeof(IQueryCommand<>);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;
                if (!commandType.IsAssignableFrom(type)) continue;

                // Register as each consumer-defined command interface (e.g. IGetOrderCommand),
                // skipping the base IQueryCommand and IQueryCommand<T> — too broad to be useful keys.
                var registered = false;
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface == commandType) continue;
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == genericCommandType) continue;
                    if (!commandType.IsAssignableFrom(iface)) continue;

                    services.AddTransient(iface, type);
                    registered = true;
                }

                // If no specific interface was found, register as self so the pipeline can resolve it.
                if (!registered)
                    services.AddTransient(type);
            }
        }

        return services;
    }

    private static void RegisterCore(IServiceCollection services)
    {
        // QueryBuilder needs the dialect's scanner. Derive it from the registered dialect —
        // a scanner that disagrees with its dialect is never what a consumer wants.
        services.TryAddSingleton<IParameterScanner>(sp =>
            sp.GetRequiredService<IDatabaseDialect>().Scanner);

        // Same for the rowset renderer — it is how the dialect turns RowSet(...) into its own
        // table expression (unnest / OPENJSON / portable UNION ALL).
        services.TryAddSingleton<IRowSetRenderer>(sp =>
            sp.GetRequiredService<IDatabaseDialect>().RowSetRenderer);

        services.AddTransient<IQueryBuilderInternal, QueryBuilder>();
        services.AddTransient<IQueryBuilder>(sp => sp.GetRequiredService<IQueryBuilderInternal>());
        services.AddTransient<IDapperResultProcessor, DapperResultProcessor>();
        services.AddTransient<IDapperPipeline, Pipeline.DapperPipeline>();
    }
}