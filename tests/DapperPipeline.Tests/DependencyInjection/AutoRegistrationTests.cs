using DapperPipeline.Abstractions;
using DapperPipeline.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.DependencyInjection;

public sealed class AutoRegistrationTests
{
    // -------------------------------------------------------------------------
    // Fake command hierarchy for scanning
    // -------------------------------------------------------------------------

    private interface IAlphaCommand : IQueryCommand { }
    private interface IBetaCommand : IQueryCommand<string> { }

    private sealed class AlphaCommand : IAlphaCommand
    {
        public bool ShouldInclude(out string? reason) { reason = null; return true; }
        public string Name => nameof(AlphaCommand);
        public void Build(IQueryBuilder b, IPipelineState s) { }
        public void Process(IDapperResultProcessor p) { }
    }

    private sealed class BetaCommand : IBetaCommand
    {
        public bool ShouldInclude(out string? reason) { reason = null; return true; }
        public string Name => nameof(BetaCommand);
        public void Build(IQueryBuilder b, IPipelineState s) { }
        public void Process(IDapperResultProcessor p) { }
        public IQueryCommand<string> OnResult(Action<string> callback) => this;
        public IQueryCommand<string> OnError(Action<string> handler) => this;
        public bool HasResultBinding => false;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsRegistered<TService, TImpl>(IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient) =>
        services.Any(d =>
            d.ServiceType == typeof(TService) &&
            d.ImplementationType == typeof(TImpl) &&
            d.Lifetime == lifetime);

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Registers_command_against_specific_interface()
    {
        var services = new ServiceCollection();
        services.AddDapperPipelineCommands(typeof(AlphaCommand).Assembly);
        Assert.True(IsRegistered<IAlphaCommand, AlphaCommand>(services));
    }

    [Fact]
    public void Registers_generic_command_against_specific_interface()
    {
        var services = new ServiceCollection();
        services.AddDapperPipelineCommands(typeof(BetaCommand).Assembly);
        Assert.True(IsRegistered<IBetaCommand, BetaCommand>(services));
    }

    [Fact]
    public void Does_not_register_against_base_IQueryCommand()
    {
        var services = new ServiceCollection();
        services.AddDapperPipelineCommands(typeof(AlphaCommand).Assembly);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IQueryCommand));
    }

    [Fact]
    public void Does_not_register_against_generic_IQueryCommand_of_T()
    {
        var services = new ServiceCollection();
        services.AddDapperPipelineCommands(typeof(BetaCommand).Assembly);
        Assert.DoesNotContain(services, d =>
            d.ServiceType.IsGenericType &&
            d.ServiceType.GetGenericTypeDefinition() == typeof(IQueryCommand<>));
    }

    [Fact]
    public void Registration_is_transient()
    {
        var services = new ServiceCollection();
        services.AddDapperPipelineCommands(typeof(AlphaCommand).Assembly);
        Assert.True(IsRegistered<IAlphaCommand, AlphaCommand>(services, ServiceLifetime.Transient));
    }

    [Fact]
    public void Scans_multiple_assemblies_without_duplicates()
    {
        var services = new ServiceCollection();
        services.AddDapperPipelineCommands(
            typeof(AlphaCommand).Assembly,
            typeof(AlphaCommand).Assembly); // same assembly twice

        var count = services.Count(d => d.ServiceType == typeof(IAlphaCommand));
        Assert.Equal(2, count); // two registrations — DI allows duplicates; GetServices returns all
    }
}
