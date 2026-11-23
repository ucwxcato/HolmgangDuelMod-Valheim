#if VALHEIM_RUNTIME
using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;
using UnityEngine;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

internal sealed class DuelRuntimePresentation : IDuelPresentation
{
    private readonly DuelSettings settings;
    private readonly Action<DuelSession>? countdownStarted;
    private readonly Action<DuelSession, DuelResult>? duelEnded;
    private readonly Dictionary<Guid, Visuals> visuals = new();
    private Guid? clientTestVisualId;
    private float clientCountdownEndsAt = -1f;
    private int lastClientCountdownSecond = -1;
    private readonly DuelPvpState pvp = new();

    public DuelRuntimePresentation(
        DuelSettings settings,
        Action<DuelSession>? countdownStarted = null,
        Action<DuelSession, DuelResult>? duelEnded = null)
    {
        this.settings = settings;
        this.countdownStarted = countdownStarted;
        this.duelEnded = duelEnded;
    }

    public void CountdownStarted(DuelSession session)
    {
        if (visuals.ContainsKey(session.SessionId)) return;

        visuals[session.SessionId] = CreateVisuals(session.SessionId, session.Center, session.Radius);
        if (Player.m_localPlayer is not null)
            StartClientCountdown((int)Math.Ceiling(settings.CountdownSeconds));
        countdownStarted?.Invoke(session);
    }

    public void DuelStarted(DuelSession session)
    {
        // PvP is enabled only here, after the countdown. Who may damage whom is
        // still enforced by the damage hook, and the marker and boundary remain
        // in place for the active duel.
        pvp.Engage(session);
    }

    public void DuelEnded(DuelSession session, DuelResult result)
    {
        pvp.Restore(session);
        if (visuals.Remove(session.SessionId, out var current))
            current.Destroy();
        PrintChat(DuelResultText.Describe(session, result));
        duelEnded?.Invoke(session, result);
    }

    internal void ReconcilePvp() => pvp.ReconcileReturningPlayers();

    internal static void PrintChat(string message)
    {
        if (Chat.instance is not null)
            Chat.instance.AddString(message);
        else if (Console.instance is not null)
            Console.instance.Print(message);
    }

    internal void ShowClientTestPresentation(DuelPosition center, double radius, int countdownSeconds = 0)
    {
        if (clientTestVisualId is not null)
        {
            if (countdownSeconds > 0)
                StartClientCountdown(countdownSeconds);
            return;
        }

        var id = Guid.NewGuid();
        visuals[id] = CreateVisuals(id, center, radius);
        clientTestVisualId = id;
        if (countdownSeconds > 0)
            StartClientCountdown(countdownSeconds);
    }

    internal void HideClientTestPresentation()
    {
        if (clientTestVisualId is not Guid id)
            return;

        clientTestVisualId = null;
        clientCountdownEndsAt = -1f;
        lastClientCountdownSecond = -1;
        if (visuals.Remove(id, out var current))
            current.Destroy();
    }

    internal void TickClientPresentation()
    {
        if (clientCountdownEndsAt < 0f)
            return;

        var secondsRemaining = Math.Max(0, Mathf.CeilToInt(clientCountdownEndsAt - Time.unscaledTime));
        if (secondsRemaining == lastClientCountdownSecond)
            return;

        lastClientCountdownSecond = secondsRemaining;
        var message = secondsRemaining > 0
            ? $"Holmgang duel begins in {secondsRemaining}."
            : "Holmgang duel countdown complete.";
        if (Chat.instance is not null)
            Chat.instance.AddString(message);
        else if (Console.instance is not null)
            Console.instance.Print(message);

        if (secondsRemaining == 0)
            clientCountdownEndsAt = -1f;
    }

    private void StartClientCountdown(int countdownSeconds)
    {
        clientCountdownEndsAt = Time.unscaledTime + Math.Max(1, countdownSeconds);
        lastClientCountdownSecond = -1;
    }

    private Visuals CreateVisuals(Guid id, DuelPosition center, double radius)
    {
        var root = new GameObject($"HolmgangDuelMod_Duel_{id:N}");
        var position = ToVector3(center);
        var flag = settings.EnableFlag ? CreateFlag(root.transform, position) : null;
        var bubble = settings.EnableBubble ? CreateBubble(root.transform, position, (float)radius) : null;
        return new Visuals(root, flag, bubble);
    }

    private static GameObject CreateFlag(Transform parent, Vector3 position)
    {
        var flagRoot = new GameObject("DuelFlag");
        flagRoot.transform.SetParent(parent, true);
        flagRoot.transform.position = position;

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "DuelFlagPole";
        pole.transform.SetParent(flagRoot.transform, false);
        pole.transform.localPosition = new Vector3(0, 1.5f, 0);
        pole.transform.localScale = new Vector3(0.06f, 1.5f, 0.06f);
        RemoveCollider(pole);
        SetUnlitColor(pole, new Color(0.35f, 0.22f, 0.1f, 1f));

        var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cloth.name = "DuelFlagCloth";
        cloth.transform.SetParent(flagRoot.transform, false);
        cloth.transform.localPosition = new Vector3(0.38f, 2.65f, 0);
        cloth.transform.localScale = new Vector3(0.7f, 0.45f, 0.04f);
        RemoveCollider(cloth);
        SetUnlitColor(cloth, new Color(0.95f, 0.95f, 0.95f, 1f));

        return flagRoot;
    }

    private static GameObject CreateBubble(Transform parent, Vector3 position, float radius)
    {
        var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bubble.name = "DuelBoundaryBubble";
        bubble.transform.SetParent(parent, true);
        // Center the sphere on the ground so its equator sits at ground level
        // with a horizontal radius equal to the duel radius. The lower half is
        // hidden by terrain, leaving a dome that marks the fight area.
        bubble.transform.position = position;
        bubble.transform.localScale = Vector3.one * (radius * 2f);
        RemoveCollider(bubble);
        SetUnlitColor(bubble, new Color(0.15f, 0.55f, 1f, 0.2f));
        return bubble;
    }

    private static void RemoveCollider(GameObject gameObject)
    {
        var collider = gameObject.GetComponent<Collider>();
        if (collider is not null) UnityEngine.Object.Destroy(collider);
    }

    // The player stands inside the bubble, so its faces must render from both
    // sides. Standard transparent shaders cull back faces and make the dome
    // invisible from inside. Sprites/Default is unlit and has Cull Off.
    private static void SetUnlitColor(GameObject gameObject, Color color)
    {
        var renderer = gameObject.GetComponent<Renderer>();
        if (renderer is null) return;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader is null) return;
        var material = new Material(shader) { color = color };
        renderer.material = material;
    }

    private static Vector3 ToVector3(DuelPosition position) =>
        new((float)position.X, (float)position.Y, (float)position.Z);

    private sealed class Visuals
    {
        private readonly GameObject root;
        private readonly GameObject? flag;
        private readonly GameObject? bubble;

        public Visuals(GameObject root, GameObject? flag, GameObject? bubble)
        {
            this.root = root;
            this.flag = flag;
            this.bubble = bubble;
        }

        public void Destroy()
        {
            if (flag is not null) UnityEngine.Object.Destroy(flag);
            if (bubble is not null) UnityEngine.Object.Destroy(bubble);
            UnityEngine.Object.Destroy(root);
        }
    }
}
#endif
