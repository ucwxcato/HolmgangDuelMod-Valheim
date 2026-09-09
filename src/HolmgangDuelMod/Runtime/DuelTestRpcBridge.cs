#if VALHEIM_RUNTIME
using System.Collections;
using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using System.Reflection;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// Transports server test commands to the dedicated server. The server
/// re-validates the sender and server-only config before it invokes the duel
/// service.
/// </summary>
internal sealed class DuelTestRpcBridge
{
    private const string RpcName = "HolmgangDuelMod_DuelTest";
    private const string PresentationRpcName = "HolmgangDuelMod_DuelTestPresentation";
    private readonly CustomRPC rpc;
    private readonly CustomRPC presentationRpc;
    private readonly Func<string, ParsedDuelCommand, string> serverHandler;
    private readonly Action<DuelPosition, double, bool>? clientPresentation;

    public DuelTestRpcBridge(
        Func<string, ParsedDuelCommand, string> serverHandler,
        Action<DuelPosition, double, bool>? clientPresentation = null)
    {
        this.serverHandler = serverHandler;
        this.clientPresentation = clientPresentation;
        rpc = NetworkManager.Instance.AddRPC(RpcName, ReceiveOnServer, ReceiveOnClient);
        presentationRpc = NetworkManager.Instance.AddRPC(PresentationRpcName, IgnorePresentationOnServer, ReceivePresentationOnClient);
    }

    public void BroadcastPresentation(DuelSession session, bool ended)
    {
        if (ZNet.instance is null || !ZNet.instance.IsServer())
            return;

        var peerIds = new List<long>();
        foreach (var peer in ZNet.instance.GetPeers())
        {
            if (peer is null) continue;
            var peerId = peer.GetType().GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer);
            if (peerId is long id && id > 0)
                peerIds.Add(id);
        }

        if (peerIds.Count == 0)
            return;

        foreach (var peerId in peerIds)
        {
            var package = new ZPackage();
            package.Write(ended ? 2 : 1);
            package.Write((float)session.Center.X);
            package.Write((float)session.Center.Y);
            package.Write((float)session.Center.Z);
            package.Write((float)session.Radius);
            presentationRpc.SendPackage(peerId, package);
        }
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
        // as Player.GetPlayerID().ToString(). The routed peer UID identifies the
        // connection; the authenticated platform identity is the UserID stored
        // in the peer's character ZDOID.
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

    private IEnumerator IgnorePresentationOnServer(long sender, ZPackage package)
    {
        yield break;
    }

    private IEnumerator ReceivePresentationOnClient(long sender, ZPackage package)
    {
        try
        {
            var kind = package.ReadInt();
            var center = new DuelPosition(package.ReadSingle(), package.ReadSingle(), package.ReadSingle());
            var radius = package.ReadSingle();
            clientPresentation?.Invoke(center, radius, kind == 2);
        }
        catch
        {
            Debug.LogWarning("[HolmgangDuelMod] Rejected malformed duel test presentation RPC.");
        }

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
            var characterId = peerType.GetField("m_characterID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer);
            var userId = characterId?.GetType().GetProperty("UserID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(characterId);
            if (userId is not long uid || uid <= 0)
                return false;

            authoritativeCallerId = uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (authoritativeCallerId.Length == 0)
                return false;

            // Do not compare against the callerId supplied in the package. It
            // is client-controlled presentation/lookup data and its ToString()
            // format is not stable across Valheim identity types. Authorization
            // uses only the authenticated character UserID resolved above.
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
