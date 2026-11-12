using Mirror;
using UnityEngine;
using kcp2k;
using Fortal.InviteToast;

/// <summary>
/// Mirror NetworkManager subclass for the Color_hunt scene.
///
/// Two entry points the game-launcher drives:
///
///   1. Inviter PC: launcher sends UDP "RMOT start-host {json}" → wire the
///      existing Fortal.TCP.CommandEvent ActionBinding (action = "start-host")
///      to <see cref="HandleStartHost"/>. We call StartHost.
///
///   2. Receiver PC: launcher sends UDP "RMOT join-invite {json}" → InviteToast
///      already parses this and fires InviteListener.Instance.OnJoin. We
///      subscribe and call StartClient with the host_ip/host_port.
///
/// On disconnect, the game falls back to solo play (round continues with
/// whatever score the local player had — see ChromaHuntManager.Net_ResumeAsSolo).
/// </summary>
public class ChromaHuntNetworkManager : NetworkManager
{
    [Header("Color Hunt — game ids")]
    [Tooltip("If set, the start-host request is ignored unless the launcher's game_id matches this.")]
    [SerializeField] private string expectedGameId = "color_hunt";

    [Header("Debug")]
    [SerializeField] private bool verboseLog = true;

    public override void Awake()
    {
        base.Awake();
        if (playerPrefab == null)
            Debug.LogError("[ChromaHuntNetworkManager] playerPrefab is not assigned. Drag the PlayerInputProxy prefab onto this component.");
    }

    public override void Start()
    {
        base.Start();
        TrySubscribeInviteListener();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (InviteListener.Instance != null)
        {
            InviteListener.Instance.OnJoin -= HandleJoinInvite;
            InviteListener.Instance.OnInviteCleared -= HandleInviteCleared;
        }
    }

    private void TrySubscribeInviteListener()
    {
        if (InviteListener.Instance == null)
        {
            Debug.LogWarning("[ChromaHuntNetworkManager] InviteListener not found in scene — receiver-side join will not auto-connect.");
            return;
        }
        InviteListener.Instance.OnJoin += HandleJoinInvite;
        InviteListener.Instance.OnInviteCleared += HandleInviteCleared;
        if (verboseLog) Debug.Log("[ChromaHuntNetworkManager] subscribed to InviteListener.OnJoin / OnInviteCleared");
    }

    // ---- Inviter PC: start-host trigger from launcher ----------------------

    /// <summary>
    /// Wire this from a CommandEvent ActionBinding with action = "start-host".
    /// Payload is the full invite record JSON (same shape as the receiver's
    /// "join-invite" payload, deserialized into <see cref="Invite"/>).
    /// </summary>
    public void HandleStartHost(string payload)
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            if (verboseLog) Debug.Log("[ChromaHuntNetworkManager] start-host ignored: a session is already active.");
            return;
        }

        Invite req = ParseInvite(payload);
        if (req != null && !string.IsNullOrEmpty(expectedGameId) &&
            !string.IsNullOrEmpty(req.game_id) && req.game_id != expectedGameId)
        {
            if (verboseLog) Debug.Log($"[ChromaHuntNetworkManager] start-host ignored: game_id '{req.game_id}' != expected '{expectedGameId}'");
            return;
        }

        // Launcher-controlled KCP port (from the invite's host_port). Falls
        // back to whatever the inspector has on KcpTransport if not set.
        if (req != null && req.host_port > 0) SetKcpPort((ushort)req.host_port);

        // Apply PC names to the local scoreboard (each PC knows the names
        // from its own launcher event — no Mirror sync needed).
        // Player A = host (from_pc), Player B = receiver (to_pc).
        ApplyScoreboardNames(req?.from_pc, req?.to_pc);

        if (verboseLog)
            Debug.Log($"[ChromaHuntNetworkManager] StartHost (id={req?.id} from={req?.from_pc} to={req?.to_pc}) on KCP port {GetKcpPort()}");

        StartHost();
    }

    private static void ApplyScoreboardNames(string a, string b)
    {
        var sb = UnityEngine.Object.FindAnyObjectByType<ChromaHuntScoreboard>();
        if (sb != null) sb.SetPlayerNames(a, b);

        var rs = UnityEngine.Object.FindAnyObjectByType<ChromaHuntResult>();
        if (rs != null) rs.SetPlayerNames(a, b);
    }

    private static Invite ParseInvite(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return null;
        try { return JsonUtility.FromJson<Invite>(payload); }
        catch { return null; }
    }

    // ---- Receiver PC: invite acceptance from launcher ----------------------

    /// <summary>
    /// Fired when the launcher cancels / declines / expires an invite (or
    /// when an invite transitions to accepted from the inviter's POV before
    /// the receiver actually joins). If the new state is anything other than
    /// "accepted", we fall back to solo: the round is no longer happening,
    /// no point keeping the server up.
    /// </summary>
    private void HandleInviteCleared(Invite invite)
    {
        if (invite == null) return;

        bool accepted = string.Equals(invite.state, "accepted", System.StringComparison.OrdinalIgnoreCase);
        if (accepted)
        {
            if (verboseLog) Debug.Log($"[ChromaHuntNetworkManager] invite-cleared (state=accepted, id={invite.id}) — staying connected.");
            return;
        }

        if (NetworkServer.active)
        {
            if (verboseLog) Debug.Log($"[ChromaHuntNetworkManager] invite-cleared (state={invite.state}, id={invite.id}) — falling back to solo on host.");
            int myScore = ChromaHuntNetwork.Instance != null ? ChromaHuntNetwork.Instance.LocalScore : -1;
            StopHost();
            ChromaHuntManager.Instance?.Net_ResumeAsSolo(myScore);
        }
        else if (NetworkClient.active)
        {
            if (verboseLog) Debug.Log($"[ChromaHuntNetworkManager] invite-cleared (state={invite.state}, id={invite.id}) — falling back to solo on client.");
            int myScore = ChromaHuntNetwork.Instance != null ? ChromaHuntNetwork.Instance.LocalScore : -1;
            StopClient();
            ChromaHuntManager.Instance?.Net_ResumeAsSolo(myScore);
        }
        // Not in a session — nothing to do.
    }

    private void HandleJoinInvite(Invite invite)
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            if (verboseLog) Debug.Log("[ChromaHuntNetworkManager] join-invite ignored: a session is already active.");
            return;
        }

        if (invite == null || string.IsNullOrEmpty(invite.host_ip) || invite.host_port <= 0)
        {
            Debug.LogWarning("[ChromaHuntNetworkManager] join-invite payload missing host_ip/host_port — cannot connect.");
            return;
        }

        networkAddress = invite.host_ip;
        SetKcpPort((ushort)invite.host_port);

        // Apply PC names to the local scoreboard. The receiver's invite has
        // from_pc = host PC, to_pc = self — same A/B mapping as the host's
        // start-host payload, so both PCs end up showing matching names.
        ApplyScoreboardNames(invite.from_pc, invite.to_pc);

        if (verboseLog) Debug.Log($"[ChromaHuntNetworkManager] StartClient → {invite.host_ip}:{invite.host_port}");
        StartClient();
    }

    // ---- Lifecycle hooks ---------------------------------------------------

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // PlayerInputProxy gets auto-spawned via base implementation.
        base.OnServerAddPlayer(conn);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (verboseLog) Debug.Log($"[ChromaHuntNetworkManager] server: client disconnected (connId={conn.connectionId})");
        base.OnServerDisconnect(conn);

        // We deliberately keep the server running even when the only opponent
        // disconnects. From the host's perspective, the dropped player just
        // "stops stepping on tiles" — their score is frozen at whatever it
        // was, and the round continues. If they reconnect (fresh join-invite)
        // they pick up the synced state.
        //
        // The local-loopback connection drops as part of our own StopHost
        // flow (invite-cleared, StopSession, etc.); base.OnServerDisconnect
        // is enough — no additional cleanup needed here.
    }

    public override void OnClientDisconnect()
    {
        if (verboseLog) Debug.Log("[ChromaHuntNetworkManager] client disconnected — falling back to solo.");
        int myScore = ChromaHuntNetwork.Instance != null ? ChromaHuntNetwork.Instance.LocalScore : -1;
        base.OnClientDisconnect();
        ChromaHuntManager.Instance?.Net_ResumeAsSolo(myScore);
    }

    // ---- Helpers -----------------------------------------------------------

    private ushort GetKcpPort()
    {
        if (Transport.active is KcpTransport kcp) return kcp.port;
        return 7777;
    }

    private void SetKcpPort(ushort port)
    {
        if (Transport.active is KcpTransport kcp) kcp.port = port;
    }
}
