using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DapperPipeline.Tests.Documentation;

/// <summary>
/// Compiles every C# example in README.md. A documented example that does not compile is a bug —
/// this repo has shipped four of them (<c>m.Add(selector)</c>, <c>splitOn: "x"</c> against a
/// <c>params</c> parameter, <c>using static DapperPipeline.Sql</c>, and a double-qualified
/// <c>dbo.OrderLineType</c>), each found only when someone tried to use the library for real.
/// </summary>
/// <remarks>
/// <para>
/// A block can opt out with an HTML comment on the line before its fence:
/// </para>
/// <code>
/// &lt;!-- readme-test: skip --&gt;        illustrative / pseudo-code
/// &lt;!-- readme-test: expect-error --&gt; must NOT compile (e.g. the SQL-injection demo)
/// </code>
/// </remarks>
public sealed class ReadmeCompilationTests
{
    private sealed record Block(int Index, string Code, string Mode, int Line);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "README.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static List<Block> ReadBlocks()
    {
        var path = Path.Combine(RepoRoot(), "README.md");
        var text = File.ReadAllText(path);

        var blocks = new List<Block>();
        var matches = Regex.Matches(text, @"(?<directive><!--\s*readme-test:\s*(?<mode>[a-z-]+)\s*-->\s*\n)?```csharp\n(?<code>.*?)```", RegexOptions.Singleline);

        var i = 0;
        foreach (Match m in matches)
        {
            var line = text[..m.Index].Count(c => c == '\n') + 1;
            var mode = m.Groups["mode"].Success ? m.Groups["mode"].Value : "compile";
            blocks.Add(new Block(i++, m.Groups["code"].Value, mode, line));
        }
        return blocks;
    }

    public static TheoryData<int> BlockIndexes
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (var b in ReadBlocks().Where(b => b.Mode != "skip"))
                data.Add(b.Index);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(BlockIndexes))]
    public void Readme_example_compiles(int index)
    {
        var block = ReadBlocks().Single(b => b.Index == index);
        var errors = Compile(block.Code);

        if (block.Mode == "expect-error")
        {
            Assert.True(errors.Count > 0,
                $"README.md line {block.Line}: this block is marked expect-error but it COMPILED. " +
                $"If the guarantee it demonstrates is gone, that is the bug.\n\n{block.Code}");
            return;
        }

        Assert.True(errors.Count == 0,
            $"README.md line {block.Line} does not compile:\n\n" +
            string.Join("\n", errors.Select(e => "  " + e.GetMessage())) +
            $"\n\n--- block ---\n{block.Code}");
    }

    /// <summary>Compiles a snippet inside the scaffold, shaped to what the snippet actually is.</summary>
    /// <remarks>Internal so <c>InterpolationEnforcementTests</c> can assert on what does NOT compile.</remarks>
    internal static List<Diagnostic> Compile(string snippet)
    {
        // `using` directives can't sit inside a method body, so hoist any the snippet shows for
        // clarity up to the compilation unit. (Duplicating a scaffold using is harmless.)
        var usings = new List<string>();
        var body = new List<string>();
        foreach (var line in snippet.Split('\n'))
        {
            if (Regex.IsMatch(line, @"^\s*using\s+(static\s+)?[\w.]+\s*;\s*$")) usings.Add(line.Trim());
            else body.Add(line);
        }
        var code = string.Join("\n", body);

        var isDeclaration = Regex.IsMatch(
            code, @"^\s*(public|internal)\s+(sealed\s+|static\s+|abstract\s+)*(class|record|interface|enum|struct)\b",
            RegexOptions.Multiline);

        // A bare `public override ...` is a class member, not a statement and not a type.
        var isMember = !isDeclaration && Regex.IsMatch(code, @"^\s*public\s+override\b", RegexOptions.Multiline);

        var scaffold = string.Join("\n", usings) + "\n" + ReadmeScaffoldText.Source;

        // BaseQueryCommand<T> is abstract, so the host class must supply Build/Process — but only
        // the ones the snippet itself doesn't already declare, or they collide.
        var commandOpen = ReadmeScaffoldText.CommandOpen
            + (isMember && code.Contains("void Build(") ? "" : ReadmeScaffoldText.BuildStub)
            + (isMember && code.Contains("void Process(") ? "" : ReadmeScaffoldText.ProcessStub);

        var source = isDeclaration
            ? scaffold + "\n" + code
            : isMember
                ? scaffold + commandOpen + "\n" + code + ReadmeScaffoldText.CommandClose
                : scaffold + commandOpen + ReadmeScaffoldText.StatementOpen + "\n"
                    + code + ReadmeScaffoldText.StatementClose + ReadmeScaffoldText.CommandClose;

        var compilation = CSharpCompilation.Create(
            "ReadmeSnippet",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        return compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            // The snippets are fragments: unused locals / unassigned results are expected.
            .Where(d => d.Id is not ("CS0168" or "CS0219"))
            .ToList();
    }

    /// <summary>
    /// Every assembly on the test's runtime path — not just the ones already loaded. Loaded
    /// assemblies are not enough: the CLR loads lazily, so an unused-so-far reference like
    /// Microsoft.Extensions.Logging would be invisible and every snippet would fail on it.
    /// </summary>
    private static IReadOnlyList<MetadataReference> References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
        .Select(MetadataReference (p) => MetadataReference.CreateFromFile(p))
        .ToList();

    [Fact]
    public void Readme_has_examples_to_check()
    {
        // Guard against the regex silently matching nothing and the suite passing vacuously.
        var blocks = ReadBlocks();
        Assert.True(blocks.Count > 20, $"Only found {blocks.Count} C# blocks in README.md — the parser is probably broken.");
    }
}
