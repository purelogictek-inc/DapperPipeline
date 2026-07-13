using System.Runtime.CompilerServices;
using DapperPipeline.Interpolation;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Extension methods enabling <c>IQueryBuilder.Append($"...")</c> compile-time-safe interpolation.
/// </summary>
/// <remarks>
/// Deliberately in the same namespace as <see cref="IQueryBuilder"/>. An extension method that lives
/// somewhere else is invisible until you find the extra <c>using</c> — and here the compiler's
/// "helpful" suggestion is LINQ's unrelated <c>IAsyncEnumerable.Append</c>, which sends you the wrong
/// way entirely. Anywhere you can name <see cref="IQueryBuilder"/>, you can call <c>Append</c>.
/// </remarks>
public static class QueryBuilderAppendExtensions
{
    /// <summary>
    /// Appends a SQL fragment composed via <c>$"..."</c> interpolation. The interpolation
    /// handler enforces compile-time SQL injection prevention — only typed values, identifiers,
    /// and pipeline-bound parameters are accepted in interpolation holes; raw <see cref="string"/>
    /// values are a compile error.
    /// </summary>
    /// <remarks>
    /// All actual work happens during handler construction and dispatch (the compiler invokes
    /// <see cref="SqlInterpolatedHandler.AppendLiteral"/> and <c>AppendFormatted</c> overloads
    /// before this method body runs). The body simply returns the builder for fluent chaining.
    /// </remarks>
    public static IQueryBuilder Append(this IQueryBuilder builder,
        [InterpolatedStringHandlerArgument(nameof(builder))] ref SqlInterpolatedHandler handler)
        => builder;
}
