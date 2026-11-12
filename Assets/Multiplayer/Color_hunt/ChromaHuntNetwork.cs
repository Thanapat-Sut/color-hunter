using Mirror;
using UnityEngine;

/// <summary>
/// Multiplayer orchestrator for Chroma Hunt (the Color_hunt scene).
///
/// Owns all networked state and drives the existing ChromaHuntManager /
/// ChromaHuntGridManager via their Net_* setters and existing public APIs.
/// The existing scripts remain MonoBehaviours and continue to work standalone
/// when no instance of this is present.
///
/// Mode detection at runtime: <see cref="IsNetworked"/>. When false (no
/// instance, or instance present but no Mirror connection active) every
/// existing script behaves exactly as it did before multiplayer was added.
/// </summary>
public class ChromaHuntNetwork : NetworkBehaviour
{
    public static ChromaHuntNetwork Instance { get; private set; }

    /// <summary>True when the multiplayer pipeline is live (host or pure client).</summary>
    public static bool IsNetworked =>
        Instance != null && (NetworkServer.active || NetworkClient.active);

    /// <summary>True only on a pure client (not host). Host ticks the local game loop.</summary>
    public static bool IsNetClientOnly =>
        Instance != null && NetworkClient.active && !NetworkServer.active;

    public const int FloorIdx = -1;

    // ---- SyncVars -----------------------------------------------------------

    [SyncVar(hook = nameof(OnAppStateSync))] private AppState syncAppState = AppState.Setup;
    [SyncVar(hook = nameof(OnPhaseSync))]    private GamePhase syncPhase = GamePhase.Phase1;
    [SyncVar(hook = nameof(OnTimerSync))]    private float syncTimer;
    [SyncVar(hook = nameof(OnTargetTimeSync))] private float syncTargetTimeLeft;

    // Target color package — three values land in the same network update.
    [SyncVar(hook = nameof(OnTargetIdxSync))] private int syncTargetIdx = -1;
    [SyncVar]                                 private bool syncIsStroop;
    [SyncVar]                                 private int syncStroopIdx = -1;

    [SyncVar(hook = nameof(OnScoreASync))]   private int syncScoreA;
    [SyncVar(hook = nameof(OnScoreBSync))]   private int syncScoreB;
    [SyncVar(hook = nameof(OnStreakASync))]  private int syncStreakA;
    [SyncVar(hook = nameof(OnStreakBSync))]  private int syncStreakB;

    /// <summary>One int per tile (color index, or FloorIdx). Indexed by tile.TileIndex.</summary>
    private readonly SyncList<int> tileLayout = new SyncList<int>();

    // ---- Server-only state --------------------------------------------------

    private NetworkConnectionToClient playerAConn;
    private NetworkConnectionToClient playerBConn;
    private bool eventsHooked;

    // ---- Lifecycle ----------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // Throttle SyncVar updates so the timer doesn't flood the wire.
        syncInterval = 0.1f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnStartClient()
    {
        tileLayout.Callback += OnTileLayoutChanged;
        // Apply any layout we already received before the callback was wired.
        for (int i = 0; i < tileLayout.Count; i++)
            ApplyTileColorLocal(i, tileLayout[i]);
    }

    public override void OnStopClient()
    {
        tileLayout.Callback -= OnTileLayoutChanged;
    }

    public override void OnStartServer()
    {
        var grid = ChromaHuntGridManager.Instance;
        int total = grid != null ? grid.GridWidth * grid.GridHeight : 0;
        tileLayout.Clear();
        for (int i = 0; i < total; i++) tileLayout.Add(FloorIdx);

        HookManagerEvents();
    }

    public override void OnStopServer()
    {
        UnhookManagerEvents();
        playerAConn = null;
        playerBConn = null;
    }

    // ---- Player slot tracking (server) -------------------------------------

    [Server]
    public void Server_RegisterPlayer(NetworkConnectionToClient conn)
    {
        if (playerAConn == null || playerAConn == conn) playerAConn = conn;
        else if (playerBConn == null || playerBConn == conn) playerBConn = conn;
    }

    [Server]
    public void Server_UnregisterPlayer(NetworkConnectionToClient conn)
    {
        if (playerAConn == conn) playerAConn = null;
        if (playerBConn == conn) playerBConn = null;
    }

    [Server]
    private int SlotOf(NetworkConnectionToClient conn)
    {
        if (conn == playerAConn) return 0;
        if (conn == playerBConn) return 1;
        return -1;
    }

    // ---- Game flow ----------------------------------------------------------

    [Server]
    public void Server_RequestStartGame(NetworkConnectionToClient sender)
    {
        // Host-only: only the local server connection may start the round.
        if (sender != NetworkServer.localConnection) return;
        if (syncAppState == AppState.Playing) return;

        syncScoreA = 0; syncScoreB = 0;
        syncStreakA = 0; syncStreakB = 0;

        // Tell every client (host included) to run StartGame locally.
        // Each side runs its own CountdownThenPlay coroutine in parallel
        // so the 3-2-1-GO UI shows on both PCs at the same time.
        RpcStartGame();
    }

    [ClientRpc]
    private void RpcStartGame()
    {
        var mgr = ChromaHuntManager.Instance;
        if (mgr != null) mgr.StartGame();
    }

    [Server]
    public void Server_HandleStep(NetworkConnectionToClient sender, int gridX, int gridY)
    {
        var mgr = ChromaHuntManager.Instance;
        if (mgr == null || mgr.state.appState != AppState.Playing) return;

        int slot = SlotOf(sender);
        if (slot < 0) return;

        var grid = mgr.gridManager;
        if (grid == null) return;
        var tile = grid.GetTileByGridCoord(gridX, gridY);
        if (tile == null) return;
        if (tile.ColorIndex < 0) return;
        // prompt i-frame / Phase 3 intro: no score, no penalty (CH-01) — same rule as solo
        if (mgr.StepsIgnored) return;

        bool correct = tile.ColorIndex == mgr.state.targetColorIndex;
        int delta = 0;
        bool bonus = false;

        var cfg = mgr.config;
        if (correct)
        {
            int prevStreak = (slot == 0 ? syncStreakA : syncStreakB) + 1;
            int gain = cfg.correctScore;
            if (prevStreak % cfg.bonusEvery == 0) { gain += cfg.bonusScore; bonus = true; }

            if (slot == 0) { syncScoreA += gain; syncStreakA = prevStreak; }
            else           { syncScoreB += gain; syncStreakB = prevStreak; }
            delta = gain;

            // Sound is played in RpcOnStepResult on the player's own PC only.
            grid.FlashTile(tile.TileIndex);
            // RespawnTile mutates the grid; OnLayoutChanged fires → SyncList push.
            grid.RespawnTile(tile, mgr.state.targetColorIndex);
        }
        else
        {
            int newScore;
            if (slot == 0) { newScore = Mathf.Max(0, syncScoreA - cfg.wrongPenalty); syncScoreA = newScore; syncStreakA = 0; }
            else           { newScore = Mathf.Max(0, syncScoreB - cfg.wrongPenalty); syncScoreB = newScore; syncStreakB = 0; }
            delta = -cfg.wrongPenalty;

            // Sound is played in RpcOnStepResult on the player's own PC only.
        }

        RpcOnStepResult(slot, tile.TileIndex, delta, correct, bonus);
    }

    // ---- Server: capture grid layout into SyncList ------------------------

    [Server]
    private void Server_PushFullLayout()
    {
        var grid = ChromaHuntGridManager.Instance;
        if (grid == null) return;
        int total = grid.GridWidth * grid.GridHeight;
        // Resize SyncList if grid size changed.
        while (tileLayout.Count < total) tileLayout.Add(FloorIdx);
        while (tileLayout.Count > total) tileLayout.RemoveAt(tileLayout.Count - 1);

        for (int i = 0; i < total; i++)
        {
            var tile = grid.GetTileByFlatIndex(i);
            int idx = tile == null ? FloorIdx : tile.ColorIndex;
            if (tileLayout[i] != idx) tileLayout[i] = idx;
        }
    }

    [Server]
    private void Server_OnGridLayoutChanged()
    {
        Server_PushFullLayout();
    }

    // ---- Server: hook ChromaHuntManager events ----------------------------

    [Server]
    private void HookManagerEvents()
    {
        if (eventsHooked) return;
        var mgr = ChromaHuntManager.Instance;
        if (mgr == null) return;

        mgr.OnStateChanged += Server_OnStateChanged;
        mgr.OnPhaseChanged += Server_OnPhaseChanged;
        mgr.OnTimeChanged += Server_OnTimeChanged;
        mgr.OnTargetColorChanged += Server_OnTargetColorChanged;
        mgr.OnTargetTimeChanged += Server_OnTargetTimeChanged;

        if (mgr.gridManager != null)
            mgr.gridManager.OnLayoutChanged += Server_OnGridLayoutChanged;

        eventsHooked = true;
    }

    [Server]
    private void UnhookManagerEvents()
    {
        if (!eventsHooked) return;
        var mgr = ChromaHuntManager.Instance;
        if (mgr != null)
        {
            mgr.OnStateChanged -= Server_OnStateChanged;
            mgr.OnPhaseChanged -= Server_OnPhaseChanged;
            mgr.OnTimeChanged -= Server_OnTimeChanged;
            mgr.OnTargetColorChanged -= Server_OnTargetColorChanged;
            mgr.OnTargetTimeChanged -= Server_OnTargetTimeChanged;
            if (mgr.gridManager != null)
                mgr.gridManager.OnLayoutChanged -= Server_OnGridLayoutChanged;
        }
        eventsHooked = false;
    }

    [Server] private void Server_OnStateChanged(AppState s) { syncAppState = s; }
    [Server] private void Server_OnPhaseChanged(GamePhase p) { syncPhase = p; }
    [Server] private void Server_OnTimeChanged(float t)      { syncTimer = t; }
    [Server] private void Server_OnTargetTimeChanged(float t){ syncTargetTimeLeft = t; }

    [Server]
    private void Server_OnTargetColorChanged(int idx, bool isStroop, int stroopIdx)
    {
        // Send all three together — order matters: idx is the trigger SyncVar
        // (its hook reads the others), so set the others first.
        syncIsStroop = isStroop;
        syncStroopIdx = stroopIdx;
        syncTargetIdx = idx;
    }

    // ---- SyncVar hooks (client + host) -------------------------------------

    private void OnAppStateSync(AppState oldVal, AppState newVal)
    {
        if (!IsNetClientOnly) return;
        ChromaHuntManager.Instance?.Net_SetAppState(newVal);
    }

    private void OnPhaseSync(GamePhase oldVal, GamePhase newVal)
    {
        if (!IsNetClientOnly) return;
        ChromaHuntManager.Instance?.Net_SetPhase(newVal);
    }

    private void OnTimerSync(float oldVal, float newVal)
    {
        if (!IsNetClientOnly) return;
        ChromaHuntManager.Instance?.Net_SetTimer(newVal);
    }

    private void OnTargetTimeSync(float oldVal, float newVal)
    {
        if (!IsNetClientOnly) return;
        ChromaHuntManager.Instance?.Net_SetTargetTime(newVal);
    }

    private void OnTargetIdxSync(int oldVal, int newVal)
    {
        if (!IsNetClientOnly) return;
        ChromaHuntManager.Instance?.Net_SetTargetColor(newVal, syncIsStroop, syncStroopIdx);
    }

    private void OnScoreASync(int oldVal, int newVal) { if (LocalIsPlayerA) PushMyScore(newVal); }
    private void OnScoreBSync(int oldVal, int newVal) { if (!LocalIsPlayerA) PushMyScore(newVal); }
    private void OnStreakASync(int oldVal, int newVal) { if (LocalIsPlayerA) PushMyStreak(newVal); }
    private void OnStreakBSync(int oldVal, int newVal) { if (!LocalIsPlayerA) PushMyStreak(newVal); }

    private void PushMyScore(int s) { ChromaHuntManager.Instance?.Net_SetScore(s); }
    private void PushMyStreak(int s) { ChromaHuntManager.Instance?.Net_SetStreak(s); }

    // ---- SyncList: tile colors → local grid --------------------------------

    private void OnTileLayoutChanged(SyncList<int>.Operation op, int idx, int oldVal, int newVal)
    {
        if (op != SyncList<int>.Operation.OP_SET) return;
        if (IsNetClientOnly) ApplyTileColorLocal(idx, newVal);
        // Host already mutated its own grid; no need to apply locally on host.
    }

    private void ApplyTileColorLocal(int flatIndex, int colorIdx)
    {
        var grid = ChromaHuntGridManager.Instance;
        if (grid == null) return;
        var tile = grid.GetTileByFlatIndex(flatIndex);
        if (tile == null) return;

        if (colorIdx == FloorIdx)
        {
            tile.SetFloorColor(grid.config != null ? grid.config.floorColor : Color.black);
        }
        else
        {
            var cfg = grid.config;
            if (cfg == null || colorIdx < 0 || colorIdx >= cfg.gameColors.Length) return;
            tile.SetTileColor(colorIdx, cfg.gameColors[colorIdx].color);
        }
    }

    // ---- Client-side input bridge ------------------------------------------

    /// <summary>Called from ChromaHuntGridManager.HandleTouchDown when networked.</summary>
    public void LocalPlayerStepped(Vector2Int gridPos)
    {
        if (PlayerInputProxy.Local == null) return;
        PlayerInputProxy.Local.CmdStepOnTile(gridPos.x, gridPos.y);
    }

    public void LocalRequestStartGame()
    {
        if (PlayerInputProxy.Local == null) return;
        PlayerInputProxy.Local.CmdRequestStartGame();
    }

    // ---- RPCs --------------------------------------------------------------

    [ClientRpc]
    private void RpcOnStepResult(int slot, int tileFlatIndex, int delta, bool correct, bool bonus)
    {
        // Visual: both clients see the flash on a correct step.
        if (correct)
            ChromaHuntGridManager.Instance?.FlashTile(tileFlatIndex);

        // Audio + UI feedback are local — only the player who stepped hears
        // and sees their own +/- pop and bonus.
        bool isMe = (slot == 0 && LocalIsPlayerA) || (slot == 1 && !LocalIsPlayerA);
        if (!isMe) return;

        AudioManager.Instance?.PlaySFX(correct ? "CorrectSFX" : "WrongSFX");
        var mgr = ChromaHuntManager.Instance;
        mgr?.Net_FireTileStepResult(delta, correct);
        if (bonus) mgr?.Net_FireBonusAwarded(delta);
        if (mgr != null)
        {
            // correct tiles may already be respawned by the SyncList push; the target is what was stepped
            var tile = ChromaHuntGridManager.Instance?.GetTileByFlatIndex(tileFlatIndex);
            int tileColor = correct ? mgr.state.targetColorIndex : (tile != null ? tile.ColorIndex : -1);
            mgr.RecordReplayStep(tileFlatIndex, tileColor, correct, delta, bonus);
        }
    }

    // ---- Public read-only accessors for UI ---------------------------------

    /// <summary>Host = player A by convention (its connection registers first).</summary>
    public bool LocalIsPlayerA => !IsNetClientOnly;

    public int LocalScore => LocalIsPlayerA ? syncScoreA : syncScoreB;
    public int OpponentScore => LocalIsPlayerA ? syncScoreB : syncScoreA;
    public int LocalStreak => LocalIsPlayerA ? syncStreakA : syncStreakB;
    public int OpponentStreak => LocalIsPlayerA ? syncStreakB : syncStreakA;
}
