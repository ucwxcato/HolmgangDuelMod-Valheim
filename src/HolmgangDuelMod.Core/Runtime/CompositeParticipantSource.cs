using Catosaurluna.HolmgangDuelMod.Core.Players;

namespace Catosaurluna.HolmgangDuelMod.Core.Runtime;

/// <summary>
/// Resolves a participant from the first source that knows it, so real
/// players and the server-owned test opponent share one coordinator path.
/// </summary>
public sealed class CompositeParticipantSource : IRuntimeParticipantSource
{
    private readonly IRuntimeParticipantSource primary;
    private readonly IRuntimeParticipantSource secondary;

    public CompositeParticipantSource(IRuntimeParticipantSource primary, IRuntimeParticipantSource secondary)
    {
        this.primary = primary;
        this.secondary = secondary;
    }

    public bool TryGet(string stableId, out PlayerSnapshot? participant)
    {
        if (primary.TryGet(stableId, out participant) && participant is not null) return true;
        return secondary.TryGet(stableId, out participant);
    }
}
