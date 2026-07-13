using DapperPipeline.Tests.Documentation;
using Microsoft.CodeAnalysis;

namespace DapperPipeline.Tests.Interpolation;

/// <summary>
/// The library's central promise: <strong>a bare string cannot reach your SQL, because the code does
/// not build</strong>. Everything else is a convenience. This is the test suite for that promise.
/// </summary>
/// <remarks>
/// <para>
/// You cannot assert this with a normal unit test, because the thing being asserted is the ABSENCE of
/// a successful compile. So these run the real C# compiler over real snippets and assert on the
/// diagnostics — the only way to prove a compile error exists is to try to compile.
/// </para>
/// <para>
/// Until now the guarantee rested on a single <c>expect-error</c> block in the README test. If someone
/// added an <c>AppendFormatted(string)</c> overload to make a call site convenient, that one assertion
/// was all that stood between them and silently reopening SQL injection across the whole library.
/// </para>
/// </remarks>
public sealed class InterpolationEnforcementTests
{
    private static List<Diagnostic> Errors(string snippet) =>
        ReadmeCompilationTests.Compile(snippet);

    private static void MustNotCompile(string snippet, string expectedCode, params string[] mustMention)
    {
        var errors = Errors(snippet);

        Assert.True(errors.Count > 0,
            $"THIS COMPILED, AND IT MUST NOT:\n\n  {snippet}\n\n" +
            "A bare string reaching the SQL builder is the injection hole this library exists to close. " +
            "If an AppendFormatted(string)/(object) overload was added, remove it.");

        Assert.True(errors.Any(e => e.Id == expectedCode),
            $"Expected {expectedCode} but got: {string.Join(", ", errors.Select(e => e.Id))}");

        // The rule being enforced is worthless if the error sends people the wrong way. The compiler's
        // own message (CS0311) pointed at ISqlIdentifier — i.e. "emit your user input as raw SQL".
        var text = string.Join("\n", errors.Select(e => e.GetMessage()));
        foreach (var phrase in mustMention)
            Assert.Contains(phrase, text);
    }

    // ── Append ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_bare_string_in_a_SQL_hole_does_not_compile()
        => MustNotCompile(
            """builder.Append($"WHERE Name = {status}");""",
            "CS0619", "Sql.Text(x)", "Sql.Identifier(x)");

    [Fact]
    public void A_quoted_bare_string_does_not_compile()
        // The classic injection shape. Still a bare string, so it still cannot be written.
        => MustNotCompile(
            """builder.Append($"WHERE Name = '{status}'");""",
            "CS0619", "Sql.Text(x)");

    [Fact]
    public void An_untyped_object_does_not_compile()
        => MustNotCompile(
            """
            object thing = 1;
            builder.Append($"WHERE X = {thing}");
            """,
            "CS0619", "untyped object");

    [Fact]
    public void A_concatenated_string_does_not_compile()
        // Building the string outside the hole doesn't help you — it's still a string in a hole.
        => MustNotCompile(
            """builder.Append($"WHERE Name = {status + "!"}");""",
            "CS0619", "Sql.Text(x)");

    // ── Where ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_bare_string_in_a_WHERE_hole_does_not_compile()
        // The WHERE builder is a second door into the same room. It was missing this guard once
        // already; it is not missing it silently again.
        => MustNotCompile(
            """
            var where = builder.Where(w => w.Append($"name = {status}"));
            """,
            "CS0619", "Sql.Text(x)");

    // ── The doors that ARE open, and must stay open ─────────────────────────────────────────────

    [Fact]
    public void Sql_Text_compiles_and_is_the_safe_door_for_string_values()
        => Assert.Empty(Errors("""builder.Append($"WHERE Name = {Sql.Text(status)}");"""));

    [Fact]
    public void Sql_Identifier_compiles_and_is_the_safe_door_for_identifiers()
        => Assert.Empty(Errors("""builder.Append($"SELECT * FROM {Sql.Identifier(status)}");"""));

    [Fact]
    public void A_typed_ISqlBindable_needs_no_wrapper_at_all()
        // The ceremony-free path: a typed domain drops straight into a hole and stays compile-safe.
        => Assert.Empty(Errors("""
            var name = new CustomerName("Contoso");
            builder.Append($"WHERE Name = {name}");
            """));

    [Fact]
    public void Primitives_need_no_wrapper()
        => Assert.Empty(Errors("""builder.Append($"WHERE Id = {orderId} AND Created > {DateTime.UtcNow}");"""));
}
