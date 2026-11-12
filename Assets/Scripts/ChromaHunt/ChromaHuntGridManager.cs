using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ChromaHuntGridManager : BaseGridManager<ChromaHuntTile>
{
    public static ChromaHuntGridManager Instance { get; private set; }

    // host pushes layout to clients after any bulk color mutation
    public event System.Action OnLayoutChanged;

    [Header("References")]
    public ChromaHuntConfig config;
    public Transform gridParent;
    public GameObject tilePrefab;

    private HashSet<int> _steppedTiles = new HashSet<int>();

    public int GridWidth => GW;
    public int GridHeight => GH;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    protected override BaseGameConfig BaseConfig => config;
    protected override Transform GridParentRef { get => gridParent; set => gridParent = value; }
    protected override GameObject TilePrefabRef => tilePrefab;
    protected override string GridParentName => "ChromaHuntGridParent";
    protected override string TileNamePrefix => "ChromaTile";
    protected override float TileScale => config.tileSize + 0.02f;

    protected override ChromaHuntTile SetupTile(GameObject tileObj, int flatIndex, Vector2Int gridPos,
                                                 int visualCol, int visualRow)
    {
        var tile = tileObj.GetComponent<ChromaHuntTile>();
        if (tile == null) tile = tileObj.AddComponent<ChromaHuntTile>();
        tile.Init(flatIndex, gridPos);
        return tile;
    }

    protected override void SetTileFloor(ChromaHuntTile tile)
    {
        tile.SetFloorColor(config.floorColor);
    }

    protected override void FitCameraToGrid()
    {
        FitGridCamera();

        // add raycaster for display 2 clicks
        if (gridCamera != null && gridCamera.GetComponent<Physics2DRaycaster>() == null)
            gridCamera.gameObject.AddComponent<Physics2DRaycaster>();
    }

    public override void Initialize()
    {
        _steppedTiles.Clear();
        base.Initialize();
    }

    protected override void OnClearGrid()
    {
        _steppedTiles.Clear();
    }

    // stepped tiles tracking
    public void StepDown(int tileIndex) => _steppedTiles.Add(tileIndex);
    public void StepUp(int tileIndex) => _steppedTiles.Remove(tileIndex);
    public HashSet<int> GetSteppedTiles() => _steppedTiles;

    protected override void HandleTouchDown(ChromaHuntTile tile)
    {
        var mgr = ChromaHuntManager.Instance;
        if (mgr == null) return;

        // solo: START / Restart come from the tablet (TabletStart)
        bool floorHidden = TabletStart.FloorHidden(ChromaHuntNetwork.IsNetworked);

        if (mgr.state.appState == AppState.GameOver) {
            if (floorHidden) return;
            Vector2Int visual = FlatToVisual(tile.TileIndex);
            int btn = GetGameOverButtonFromVisualCoord(visual);
            if (btn != -99) mgr.HandleGameOverButton(btn);
            return;
        }

        if (mgr.state.appState == AppState.Setup && !mgr.IsCountingDown)
        {
            if (!floorHidden && IsStartButton(tile.TileIndex) && Time.time >= mgr.StartReadyTime)
            {
                // only host may start; clients no-op
                if (ChromaHuntNetwork.IsNetClientOnly) return;

                // Route through the UI fade so the floor tile triggers the same transition as the on-screen button.
                if (mgr.ui != null) mgr.ui.BeginStartGame();
                else
                {
                    AudioManager.Instance?.PlaySFX("ClickSFX");
                    if (ChromaHuntNetwork.IsNetworked && ChromaHuntNetwork.Instance != null)
                        ChromaHuntNetwork.Instance.LocalRequestStartGame();
                    else
                        mgr.StartGame();
                }
            }
            return;
        }

        if (mgr.state.appState == AppState.Playing)
        {
            _steppedTiles.Add(tile.TileIndex);

            // server adjudicates — even host routes through Cmd
            if (ChromaHuntNetwork.IsNetworked)
            {
                ChromaHuntNetwork.Instance.LocalPlayerStepped(tile.GridPosition);
                return;
            }

            mgr.OnTileStepped(tile);
        }
    }

    protected override void HandleTouchUp(ChromaHuntTile tile)
    {
        _steppedTiles.Remove(tile.TileIndex);
    }

    // TODO: make start button position configurable instead of magic numbers

    public void SetTileColorByVisual(int vx, int vy, Color color)
    {
        int idx = VisualToFlatIndex(vx, vy);
        if (idx < 0) return;
        var tile = GetTileByFlatIndex(idx);
        if (tile != null) tile.SetColor(color);
    }

    public void RenderStartButton(Color confirmColor)
    {
        SetAllTilesBlack();
        RenderStartOnlyButton(confirmColor);
    }

    public bool IsStartButton(int flatIndex)
    {
        return IsStartOnlyButton(flatIndex);
    }

    // --- color distribution ---

    public void DistributeColors(int targetColorIndex, int[] activeColorIndices = null)
    {
        if (_allTiles == null || config.gameColors.Length == 0) return;

        // server picks layout; clients get it via SyncList
        if (ChromaHuntNetwork.IsNetClientOnly) return;

        int total = TotalTiles;
        int poolCount = config.gameColors.Length;
        // when caller doesn't specify a subset, fall back to full pool in index order
        bool useSubset = activeColorIndices != null && activeColorIndices.Length > 0;
        int count = useSubset ? Mathf.Clamp(activeColorIndices.Length, 1, poolCount) : poolCount;
        int active = Mathf.Max(count, Mathf.RoundToInt(total * config.activeTileRatio));

        // assign color indices, -1 = floor
        int[] assignment = new int[total];
        int slot = 0;
        int perColor = active / count;
        int remainder = active % count;

        for (int c = 0; c < count; c++)
        {
            int colorIdx = useSubset ? activeColorIndices[c] : c;
            int n = perColor + (c < remainder ? 1 : 0);
            for (int j = 0; j < n; j++)
                assignment[slot++] = colorIdx;
        }
        for (int i = slot; i < total; i++)
            assignment[i] = -1;

        // fisher-yates shuffle
        for (int i = total - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (assignment[i], assignment[j]) = (assignment[j], assignment[i]);
        }

        for (int i = 0; i < total; i++)
        {
            if (_allTiles[i] == null) continue;
            int ci = assignment[i];
            if (ci >= 0)
                _allTiles[i].SetTileColor(ci, config.gameColors[ci].color);
            else
                _allTiles[i].SetFloorColor(config.floorColor);
        }

        OnLayoutChanged?.Invoke();
    }

    public void ShuffleTilePositions()
    {
        if (_allTiles == null) return;
        if (ChromaHuntNetwork.IsNetClientOnly) return;

        int total = TotalTiles;
        int[] indices = new int[total];
        Color[] colors = new Color[total];

        for (int i = 0; i < total; i++)
        {
            if (_allTiles[i] == null) continue;
            indices[i] = _allTiles[i].ColorIndex;
            colors[i] = _allTiles[i].GetColor();
        }

        for (int i = total - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
            (colors[i], colors[j]) = (colors[j], colors[i]);
        }

        for (int i = 0; i < total; i++)
        {
            if (_allTiles[i] != null)
                _allTiles[i].SetTileColor(indices[i], colors[i]);
        }

        OnLayoutChanged?.Invoke();
    }

    public void RespawnTile(ChromaHuntTile steppedTile, int targetColorIndex)
    {
        if (steppedTile == null) return;
        steppedTile.SetFloorColor(config.floorColor);

        // try placing new target on a random empty tile
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int ri = Random.Range(0, _allTiles.Length);
            var t = _allTiles[ri];
            if (t != null && t != steppedTile && t.ColorIndex == -1) {
                t.SetTileColor(targetColorIndex, config.gameColors[targetColorIndex].color);
                OnLayoutChanged?.Invoke();
                return;
            }
        }
        // FIXME: silently fails if all tiles occupied
        OnLayoutChanged?.Invoke();
    }

    public void FlashTile(int index)
    {
        if (index >= 0 && index < _allTiles.Length && _allTiles[index] != null)
            _allTiles[index].PlayHitAnimation();
    }
}
