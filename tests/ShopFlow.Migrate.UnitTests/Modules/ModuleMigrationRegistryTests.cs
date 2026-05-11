using Microsoft.EntityFrameworkCore;
using ShopFlow.Migrate.Modules;

namespace ShopFlow.Migrate.UnitTests.Modules;

public class ModuleMigrationRegistryTests
{
    private sealed class FakeDbContext : DbContext
    {
        public FakeDbContext(DbContextOptions<FakeDbContext> options)
            : base(options) { }
    }

    private sealed class OtherDbContext : DbContext
    {
        public OtherDbContext(DbContextOptions<OtherDbContext> options)
            : base(options) { }
    }

    [Fact]
    public void Register_adds_descriptor()
    {
        var registry = new ModuleMigrationRegistry();

        registry.Register(
            new ModuleMigrationDescriptor("Fake", typeof(FakeDbContext), "FakeAssembly")
        );

        registry.All.Should().ContainSingle().Which.DbContextType.Should().Be<FakeDbContext>();
    }

    [Fact]
    public void Register_rejects_duplicate_dbcontext()
    {
        var registry = new ModuleMigrationRegistry();
        registry.Register(
            new ModuleMigrationDescriptor("Fake", typeof(FakeDbContext), "FakeAssembly")
        );

        var act = () =>
            registry.Register(
                new ModuleMigrationDescriptor("FakeAgain", typeof(FakeDbContext), "FakeAssembly")
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void Register_allows_distinct_dbcontexts()
    {
        var registry = new ModuleMigrationRegistry();

        registry.Register(
            new ModuleMigrationDescriptor("Fake", typeof(FakeDbContext), "FakeAssembly")
        );
        registry.Register(
            new ModuleMigrationDescriptor("Other", typeof(OtherDbContext), "OtherAssembly")
        );

        registry.All.Should().HaveCount(2);
    }

    [Fact]
    public void Descriptor_rejects_non_dbcontext_type()
    {
        var act = () =>
            new ModuleMigrationDescriptor("Fake", typeof(string), "FakeAssembly");

        act.Should().Throw<ArgumentException>().WithMessage("*not a DbContext*");
    }

    [Fact]
    public void Descriptor_rejects_blank_assembly()
    {
        var act = () =>
            new ModuleMigrationDescriptor("Fake", typeof(FakeDbContext), "   ");

        act.Should().Throw<ArgumentException>();
    }
}
