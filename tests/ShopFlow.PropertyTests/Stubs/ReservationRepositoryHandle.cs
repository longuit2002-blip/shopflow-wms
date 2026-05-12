using ShopFlow.Inventory.Application.Ports;

namespace ShopFlow.PropertyTests.Stubs;

/// <summary>
/// Single-slot static handle that lets the property suite swap in a real
/// <see cref="IReservationRepository"/> at test-setup time without
/// touching the property test bodies. The fixture sets
/// <see cref="Current"/> in its constructor; the
/// <see cref="NotImplementedReservationRepository"/> adapter forwards
/// every method to whatever is in the handle.
/// </summary>
/// <remarks>
/// This is the static-accessor pattern AGENTS.md §2.13 generally
/// forbids — exempted here because the property suite predates DI
/// integration and the goal is "zero test-body edits when the impl
/// arrives". Production code never reads from this handle.
/// </remarks>
public static class ReservationRepositoryHandle
{
    public static IReservationRepository? Current { get; set; }
}
