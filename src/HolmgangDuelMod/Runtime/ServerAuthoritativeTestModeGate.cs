#if VALHEIM_RUNTIME
using Catosaurluna.HolmgangDuelMod.Core.Commands;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// Client-side test commands are denied until the server explicitly authorizes
/// test mode through the planned network/RPC gate.
/// </summary>
internal sealed class ServerAuthoritativeTestModeGate : ITestModeGate
{
    private readonly bool configuredOnThisInstance;

    public ServerAuthoritativeTestModeGate(bool configuredOnThisInstance)
    {
        this.configuredOnThisInstance = configuredOnThisInstance;
    }

    // The setting is deliberately evaluated only by the server process. A client
    // can have the same config entry, but it can never authorize its own request.
    public bool IsEnabled => configuredOnThisInstance && ZNet.instance is not null && ZNet.instance.IsServer();
}
#endif
