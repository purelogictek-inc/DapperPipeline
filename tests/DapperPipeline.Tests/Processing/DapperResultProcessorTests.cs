using DapperPipeline.Processing;

namespace DapperPipeline.Tests.Processing;

public sealed class DapperResultProcessorTests
{
    private readonly DapperResultProcessor _processor = new();

    [Fact]
    public void NeedsToProcess_false_when_no_readers_registered()
    {
        Assert.False(_processor.NeedsToProcess);
    }

    [Fact]
    public void NeedsToProcess_true_after_Handle()
    {
        _processor.Read<int>(_ => { });
        Assert.True(_processor.NeedsToProcess);
    }

    [Fact]
    public void Readers_clears_scopes_when_accessed()
    {
        _processor.Read<int>(_ => { });
        _ = _processor.Readers;
        Assert.False(_processor.NeedsToProcess);
    }

    [Fact]
    public void Handle_single_type_registers_one_reader()
    {
        _processor.Read<int>(_ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Handle_multiple_calls_registers_multiple_readers()
    {
        _processor.Read<int>(_ => { });
        _processor.Read<string>(_ => { });
        Assert.Equal(2, _processor.Readers.Count);
    }


    [Fact]
    public void CaughtError_defaults_to_false()
    {
        Assert.False(_processor.CaughtError);
    }

    [Fact]
    public void CaughtError_can_be_set()
    {
        _processor.CaughtError = true;
        Assert.True(_processor.CaughtError);
    }

    [Fact]
    public void Project_single_type_registers_reader()
    {
        _processor.Project<int, string>(x => x.ToString(), _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Read_two_type_registers_reader()
    {
        _processor.Read<int, string>((a, _) => a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_two_type_registers_reader()
    {
        _processor.Project<int, string, long>((a, _) => (long)a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_three_type_registers_reader()
    {
        _processor.Project<int, string, byte, long>((a, _, _) => (long)a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_four_type_registers_reader()
    {
        _processor.Project<int, string, byte, short, long>((a, _, _, _) => (long)a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_five_type_registers_reader()
    {
        _processor.Project<int, string, byte, short, float, long>((a, _, _, _, _) => (long)a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_six_type_registers_reader()
    {
        _processor.Project<int, string, byte, short, float, double, long>((a, _, _, _, _, _) => (long)a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_seven_type_registers_reader()
    {
        _processor.Project<int, string, byte, short, float, double, char, long>((a, _, _, _, _, _, _) => (long)a, _ => { });
        Assert.Single(_processor.Readers);
    }

    // T8–T15: object-array path via Dapper's gr.Read<TReturn>(Type[], Func<object[],TReturn>, string)

    [Fact]
    public void Read_eight_type_registers_reader()
    {
        _processor.Read<int, string, byte, short, float, double, char, uint>((a, _, _, _, _, _, _, _) => a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Read_fifteen_type_registers_reader()
    {
        _processor.Read<int, string, byte, short, float, double, char, uint, long, sbyte, ushort, ulong, decimal, bool, Guid>(
            (a, _, _, _, _, _, _, _, _, _, _, _, _, _, _) => a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_eight_type_registers_reader()
    {
        _processor.Project<int, string, byte, short, float, double, char, uint, long>((a, _, _, _, _, _, _, _) => (long)a, _ => { });
        Assert.Single(_processor.Readers);
    }

    [Fact]
    public void Project_fifteen_type_registers_reader()
    {
        _processor.Project<int, string, byte, short, float, double, char, uint, long, sbyte, ushort, ulong, decimal, bool, Guid, string>(
            (a, _, _, _, _, _, _, _, _, _, _, _, _, _, _) => a.ToString(), _ => { });
        Assert.Single(_processor.Readers);
    }
}
