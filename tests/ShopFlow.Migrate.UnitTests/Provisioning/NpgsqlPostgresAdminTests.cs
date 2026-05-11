using ShopFlow.Migrate.Provisioning;

namespace ShopFlow.Migrate.UnitTests.Provisioning;

public class NpgsqlPostgresAdminTests
{
    [Theory]
    [InlineData("shopflow_t_acme")]
    [InlineData("shopflow_control")]
    [InlineData("a")]
    [InlineData("ab12_3")]
    public void ValidateIdentifier_accepts_allowlist_chars(string identifier)
    {
        var act = () => NpgsqlPostgresAdmin.ValidateIdentifier(identifier, nameof(identifier));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("acme; DROP TABLE tenants")]
    [InlineData("acme-1")]
    [InlineData("acme.db")]
    [InlineData("acme\"")]
    [InlineData("")]
    public void ValidateIdentifier_rejects_disallowed_input(string identifier)
    {
        var act = () => NpgsqlPostgresAdmin.ValidateIdentifier(identifier, nameof(identifier));

        act.Should().Throw<ArgumentException>();
    }
}
