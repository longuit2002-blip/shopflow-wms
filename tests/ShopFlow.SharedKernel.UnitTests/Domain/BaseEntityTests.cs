using FluentAssertions;
using ShopFlow.SharedKernel.Domain;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Domain;

public class BaseEntityTests
{
    [Fact]
    public void NewEntity_HasNonEmptyId_AndUtcCreatedAt()
    {
        var entity = new TestEntity();

        entity.Id.Should().NotBe(Guid.Empty);
        entity.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        entity.UpdatedAt.Should().BeNull();
        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RaiseDomainEvent_AddsToBuffer()
    {
        var tenantId = Guid.NewGuid();
        var entity = new TestEntity();

        entity.RaiseTestEvent(tenantId);

        entity.DomainEvents.Should().HaveCount(1);
        entity.DomainEvents[0].TenantId.Should().Be(tenantId);
        entity.DomainEvents[0].OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ClearDomainEvents_DrainsBuffer()
    {
        var entity = new TestEntity();
        entity.RaiseTestEvent(Guid.NewGuid());
        entity.RaiseTestEvent(Guid.NewGuid());

        entity.ClearDomainEvents();

        entity.DomainEvents.Should().BeEmpty();
    }

    private sealed class TestEntity : BaseEntity
    {
        public void RaiseTestEvent(Guid tenantId) => RaiseDomainEvent(new TestEvent(tenantId));
    }

    private sealed record TestEvent(Guid TenantId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
}
