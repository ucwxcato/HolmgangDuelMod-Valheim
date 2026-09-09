#if VALHEIM_RUNTIME
using System.Collections;
using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using System.Reflection;
using Catosaurluna.HolmgangDuelMod.Core.Players;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// Transports administrator test commands to the dedicated server. The server
/// re-validates the sender, administrator list, and server-only config before it
/// invokes the duel service.
/// </summary>
internal sealed class DuelTestRpcBridge
{
    private const string RpcName = "HolmgangDuelMod_DuelTest";
    private readonly CustomRPC rpc;
    private readonly Func<string, ParsedDuelCommand, string> serverHandler;

    public DuelTestRpcBridge(Func<string, ParsedDuelCommand, string> serverHandler)
    {
        this.serverHandler = serverHandler;
        rpc = NetworkManager.Instance.AddRPC(RpcName, ReceiveOnServer, ReceiveOnClient);
    }

    public bool TrySend(string callerId, ParsedDuelCommand command, out string message)
    {
        if (ZNet.instance is null || ZNet.instance.IsServer())
        {
            message = string.Empty;
            return false;
        }

        var package = new ZPackage();
        package.Write(callerId);
        package.Write((int)command.Kind);
        package.Write(command.PlayerName ?? string.Empty);
        package.Write((float)(command.Health ?? -1d));
        rpc.SendPackage(GetServerPeerId(), package);
        message = "Test command sent to the dedicated server.";
        return true;
    }

    private IEnumerator ReceiveOnServer(long sender, ZPackage package)
    {
        if (ZNet.instance is null || !ZNet.instance.IsServer())
            yield break;

        string callerId;
        ParsedDuelCommand command;
        try
        {
            callerId = package.ReadString();
            var kind = (DuelCommandKind)package.ReadInt();
            var playerName = package.ReadString();
            var health = package.ReadSingle();
            command = new ParsedDuelCommand(kind, playerName, health < 0f ? null : health, null);
        }
        catch
        {
            Debug.LogWarning("[HolmgangDuelMod] Rejected malformed duel test RPC.");
            SendResponse(sender, "Invalid duel test request.");
            yield break;
        }

        // Resolve the caller from the server's peer table. The RPC sender is a
        // routed-peer ID and is not guaranteed to have the same string format
        // as Player.GetPlayerID().ToString(). The peer UID is the authenticated
        // platform identity; ZNetPeer.m_characterID is the character/ZDO owner
        // and can still be unset or differ while a player is joining.
        if (!TryResolveAuthoritativeCaller(sender, out var authoritativeCallerId))
        {
            Debug.LogWarning($"[HolmgangDuelMod] Rejected duel test RPC identity. Sender={sender}, caller={callerId}.");
            SendResponse(sender, "Duel test rejected: sender identity mismatch.");
            yield break;
        }

        var response = serverHandler(authoritativeCallerId, command);
        Debug.Log($"[HolmgangDuelMod] Duel test RPC accepted. Sender={sender}, caller={authoritativeCallerId}, command={command.Kind}, response={response}");
        SendResponse(sender, response);
        yield break;
    }

    private IEnumerator ReceiveOnClient(long sender, ZPackage package)
    {
        string response;
        try
        {
            response = package.ReadString();
        }
        catch
        {
            yield break;
        }

        if (Chat.instance is not null)
            Chat.instance.AddString(response);
        else if (Console.instance is not null)
            Console.instance.Print(response);
        yield break;
    }

    private void SendResponse(long peerId, string response)
    {
        var package = new ZPackage();
        package.Write(response ?? string.Empty);
        rpc.SendPackage(peerId, package);
    }

    private static bool TryResolveAuthoritativeCaller(long sender, out string authoritativeCallerId)
    {
        authoritativeCallerId = string.Empty;
        if (ZNet.instance is null)
            return false;

        try
        {
            var getPeer = typeof(ZNet).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "GetPeer" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(long));
            var peer = getPeer?.Invoke(ZNet.instance, new object[] { sender });
            if (peer is null)
                return false;

            var peerType = peer.GetType();
            var peerUid = peerType.GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer);
            if (peerUid is not long uid || uid <= 0)
                return false;

            authoritativeCallerId = uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (authoritativeCallerId.Length == 0)
                return false;

            // Do not compare against the callerId supplied in the package. It
            // is client-controlled presentation/lookup data and its ToString()
            // format is not stable across Valheim identity types. Authorization
            // uses only the authenticated peer UID resolved above.
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HolmgangDuelMod] Could not resolve RPC peer identity: {exception.Message}");
            return false;
        }
    }

    private static long GetServerPeerId()
    {
        var method = typeof(ZRoutedRpc).GetMethod(
            "GetServerPeerID",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return method?.Invoke(ZRoutedRpc.instance, null) is long peerId ? peerId : 0L;
    }
}
#endif
