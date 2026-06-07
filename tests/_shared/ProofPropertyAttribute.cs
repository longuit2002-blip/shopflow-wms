using FsCheck.Xunit;

namespace ShopFlow.TestSupport;

/// <summary>
/// A FsCheck <see cref="PropertyAttribute"/> gated by <see cref="ProofGate"/> —
/// the property-test analogue of <see cref="ProofFactAttribute"/>. Replaces a
/// bare <c>[Property(...)]</c> on the reservation-ledger invariant suite so the
/// Docker-backed run is opt-in locally and automatic in CI. All the inherited
/// FsCheck knobs (<c>MaxTest</c>, <c>Arbitrary</c>, <c>Replay</c>, …) remain
/// usable as named arguments on this subclass.
/// </summary>
/// <remarks>
/// Linked (via <c>&lt;Compile Include&gt;</c>) only into the project that
/// references FsCheck.Xunit (ShopFlow.PropertyTests); the rest of the proof
/// projects link <see cref="ProofGate"/> + <see cref="ProofFactAttribute"/>
/// only.
/// </remarks>
public sealed class ProofPropertyAttribute : PropertyAttribute
{
    public ProofPropertyAttribute()
    {
        if (!ProofGate.Enabled)
        {
            Skip = ProofGate.SkipMessage;
        }
    }
}
