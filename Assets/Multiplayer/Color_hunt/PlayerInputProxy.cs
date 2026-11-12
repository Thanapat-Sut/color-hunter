using Mirror;
using UnityEngine;

/// <summary>
/// Per-connection input proxy. Mirror auto-spawns one of these on each client
/// connection (configured via NetworkManager.playerPrefab). The local instance
/// is identified by isLocalPlayer; only it sends Cmds, and only the server
/// receives them with sender info attached.
///
/// This object has no visuals and no movement — it exists purely as the
/// authority/identity holder for [Command] calls.
/// </summary>
public class PlayerInputProxy : NetworkBehaviour
{
    public static PlayerInputProxy Local { get; private set; }

    public override void OnStartLocalPlayer()
    {
        Local = this;
    }

    public override void OnStopLocalPlayer()
    {
        if (Local == this) Local = null;
    }

    public override void OnStartServer()
    {
        if (ChromaHuntNetwork.Instance != null)
            ChromaHuntNetwork.Instance.Server_RegisterPlayer(connectionToClient);
    }

    public override void OnStopServer()
    {
        if (ChromaHuntNetwork.Instance != null)
            ChromaHuntNetwork.Instance.Server_UnregisterPlayer(connectionToClient);
    }

    /// <summary>Called from local floor input; forwarded to server for adjudication.</summary>
    [Command]
    public void CmdStepOnTile(int gridX, int gridY)
    {
        if (ChromaHuntNetwork.Instance == null) return;
        ChromaHuntNetwork.Instance.Server_HandleStep(connectionToClient, gridX, gridY);
    }

    /// <summary>Host-only request to start the round. Authority not required so the local
    /// player object on the host can call it via its own connection.</summary>
    [Command(requiresAuthority = false)]
    public void CmdRequestStartGame(NetworkConnectionToClient sender = null)
    {
        if (ChromaHuntNetwork.Instance == null) return;
        ChromaHuntNetwork.Instance.Server_RequestStartGame(sender);
    }
}
