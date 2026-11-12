using UnityEngine;
using Fortal.InviteToast;
using Mirror;

/// <summary>
/// Dev-only helper that simulates the launcher → game UDP RMOT actions
/// from the Inspector. Right-click the component header and pick a menu
/// item; each one calls into the same handler the real CommandEvent
/// ActionBinding would route to. Useful for testing without the launcher.
///
/// All actions only do anything in Play Mode (Mirror needs the runtime).
/// </summary>
public class ChromaHuntMultiplayerTester : MonoBehaviour
{
    [Header("Common payload fields")]
    [SerializeField] private string inviteId = "test-invite-001";
    [SerializeField] private string fromPc = "PC-A";
    [SerializeField] private string toPc = "PC-B";
    [SerializeField] private string gameId = "color_hunt";
    [SerializeField] private string gameName = "Color Hunt";

    [Header("Host port (used by both start-host and join-invite payloads)")]
    [SerializeField] private string joinHostIp = "127.0.0.1";
    [SerializeField] private int hostPort = 7777;

    [ContextMenu("1. Fire start-host  (simulate launcher → become host)")]
    public void FireStartHost()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Tester] Enter Play Mode first."); return; }

        var nm = FindAnyObjectByType<ChromaHuntNetworkManager>();
        if (nm == null) { Debug.LogError("[Tester] ChromaHuntNetworkManager not found in scene."); return; }

        // The real launcher sends a full Invite record on start-host (same
        // shape as the receiver's join-invite), so we mirror that here.
        var req = new Invite {
            id = inviteId, from_pc = fromPc, to_pc = toPc,
            host_ip = joinHostIp, host_port = hostPort,
            game_id = gameId, game_name = gameName, state = "pending"
        };
        string json = JsonUtility.ToJson(req);
        Debug.Log($"[Tester] simulate RMOT 'start-host' payload: {json}");
        nm.HandleStartHost(json);
    }

    [ContextMenu("2. Fire join-invite (simulate launcher → become client)")]
    public void FireJoinInvite()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Tester] Enter Play Mode first."); return; }

        var listener = InviteListener.Instance;
        if (listener == null) { Debug.LogError("[Tester] InviteListener not found in scene."); return; }

        var invite = new Invite {
            id = inviteId, from_pc = fromPc, to_pc = toPc,
            host_ip = joinHostIp, host_port = hostPort,
            game_id = gameId, game_name = gameName, state = "accepted"
        };
        string json = JsonUtility.ToJson(invite);
        Debug.Log($"[Tester] simulate RMOT 'join-invite' payload: {json}");
        listener.HandleJoinInvite(json);
    }

    [ContextMenu("3a. Fire invite-cleared (state=cancelled — should fall back to solo)")]
    public void FireInviteClearedCancelled()
    {
        FireInviteClearedWithState("cancelled");
    }

    [ContextMenu("3b. Fire invite-cleared (state=declined)")]
    public void FireInviteClearedDeclined()
    {
        FireInviteClearedWithState("declined");
    }

    private void FireInviteClearedWithState(string state)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Tester] Enter Play Mode first."); return; }

        var listener = InviteListener.Instance;
        if (listener == null) { Debug.LogError("[Tester] InviteListener not found in scene."); return; }

        var invite = new Invite
        {
            id = inviteId, from_pc = fromPc, to_pc = toPc,
            host_ip = joinHostIp, host_port = hostPort,
            game_id = gameId, game_name = gameName, state = state
        };
        string json = JsonUtility.ToJson(invite);
        Debug.Log($"[Tester] simulate RMOT 'invite-cleared' (state={state}) payload: {json}");
        listener.HandleInviteCleared(json);
    }

    [ContextMenu("4. Stop session  (StopHost / StopClient)")]
    public void StopSession()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Tester] Enter Play Mode first."); return; }

        var nm = NetworkManager.singleton;
        if (nm == null) { Debug.Log("[Tester] No NetworkManager."); return; }

        if (NetworkServer.active && NetworkClient.isConnected) { Debug.Log("[Tester] StopHost"); nm.StopHost(); }
        else if (NetworkServer.active)                          { Debug.Log("[Tester] StopServer"); nm.StopServer(); }
        else if (NetworkClient.active)                          { Debug.Log("[Tester] StopClient"); nm.StopClient(); }
        else                                                    { Debug.Log("[Tester] No active session."); }
    }

    [ContextMenu("? Print session status")]
    public void PrintStatus()
    {
        Debug.Log($"[Tester] NetworkServer.active={NetworkServer.active}  NetworkClient.active={NetworkClient.active}  isConnected={NetworkClient.isConnected}  ChromaHuntNetwork.IsNetworked={ChromaHuntNetwork.IsNetworked}  IsNetClientOnly={ChromaHuntNetwork.IsNetClientOnly}");
    }
}
