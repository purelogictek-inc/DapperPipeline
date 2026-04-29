namespace DapperPipeline.Utilities;

internal static class ExtensionsHelper
{
    extension(string? source)
    {
        internal bool IsEmpty() => source == null || source.All(c => c is ' ' or '\t' or '\r' or '\n');
        internal bool IsNotEmpty() => !source.IsEmpty();

        /// <summary>Truncates or pads <paramref name="source"/> to exactly <paramref name="length"/> characters.</summary>
        internal string Truncate(int length) =>
            $"{source ?? string.Empty}".PadRight(length)[..length].Trim();
    }

    internal static void ForEach<T>(this IEnumerable<T>? source, Action<T, int> action)
    {
        if (source == null) return;
        var i = 0;
        foreach (var item in source)
            action(item, i++);
    }
}