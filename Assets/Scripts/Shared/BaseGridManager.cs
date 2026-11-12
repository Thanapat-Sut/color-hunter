using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// Non-generic view of any game's LED floor (FloorShowRunner paints it without knowing the game).
public interface ILedFloor
{
    int VisualColCount { get; }
    int VisualRowCount { get; }
    void SetTileColorByVisual(int vx, int vy, Color color);
}

public abstract class BaseGridManager<TTile> : MonoBehaviour, ILedFloor
    where TTile : BaseTileController
{
    protected TTile[,] _tiles;
    protected TTile[] _allTiles;
    protected Camera _mainCamera;

    [Header("Grid Display")]
    [SerializeField] protected Camera gridCamera;

    static readonly Color GridBgColor = new Color32(0x1A, 0x1A, 0x1A, 0xFF);
    const float CameraZ = -10f;
    const float GridPadding = 0.5f;

    protected abstract BaseGameConfig BaseConfig { get; }
    protected abstract Transform GridParentRef { get; set; }
    protected abstract GameObject TilePrefabRef { get; }

    protected virtual int GW => BaseConfig.gridWidth;
    protected virtual int GH => BaseConfig.gridHeight;
    protected virtual int TotalTiles => GW * GH;
    protected virtual int VisCols => GW * 2;
    protected virtual int VisRows => GH / 2;

    public int VisualColCount => VisCols;
    public int VisualRowCount => VisRows;

    protected abstract string GridParentName { get; }
    protected abstract string TileNamePrefix { get; }
    protected abstract TTile SetupTile(GameObject tileObj, int flatIndex, Vector2Int gridPos,
                                        int visualCol, int visualRow);
    protected abstract void SetTileFloor(TTile tile);
    protected abstract void HandleTouchDown(TTile tile);
    protected virtual void HandleTouchUp(TTile tile) { }
    protected virtual void PostInitialize() { }
    protected virtual void OnClearGrid() { }
    protected virtual void PreBuildGrid() { }
    protected virtual float TileScale => 1f;

    protected virtual void Start()
    {
        _mainCamera = Camera.main;
        if (_mainCamera != null && _mainCamera.GetComponent<Physics2DRaycaster>() == null)
            _mainCamera.gameObject.AddComponent<Physics2DRaycaster>();
    }

    public virtual void Initialize()
    {
        ClearGrid();
        EnsureGridParent();
        PreBuildGrid();

        int gw = GW;
        int gh = GH;
        int total = TotalTiles;
        int visCols = VisCols;
        int half = gh / 2;

        _tiles = new TTile[gw, gh];
        _allTiles = new TTile[total];

        float step = BaseConfig.tileSize + BaseConfig.tileSpacing;
        float totalW = (visCols - 1) * step;
        float totalH = (half - 1) * step;
        Vector3 origin = new Vector3(-totalW / 2f, totalH / 2f, 0f);

        int flat = 0;
        for (int x = 0; x < gw; x++)
            for (int y = 0; y < gh; y++)
                CreateTile(x, y, ref flat, gw, gh, half, origin, step);

        SetAllFloor();
        FitCameraToGrid();
        PostInitialize();
    }

    // serpentine mapping: each LED strip folds in half
    // bottom half (y < half) -> right column, top-to-bottom
    // top half (y >= half) -> left column, bottom-to-top
    void CreateTile(int x, int y, ref int flat, int gw, int gh, int half, Vector3 origin, float step)
    {
        int visCol, visRow;
        if (y < half) {
            visCol = (gw - 1 - x) * 2 + 1;
            visRow = y;
        } else {
            visCol = (gw - 1 - x) * 2;
            visRow = gh - 1 - y;
        }

        Vector3 pos = origin + new Vector3(visCol * step, -visRow * step, 0f);

        var tileObj = Instantiate(TilePrefabRef, pos, Quaternion.identity, GridParentRef);
        tileObj.name = $"{TileNamePrefix}_{x}_{y}";

        float scale = TileScale;
        if (!Mathf.Approximately(scale, 1f))
            tileObj.transform.localScale = new Vector3(scale, scale, 1f);

        TTile tile = SetupTile(tileObj, flat, new Vector2Int(x, y), visCol, visRow);
        _tiles[x, y] = tile;
        _allTiles[flat] = tile;

        OnTileCreated(tile, flat, visCol, visRow);
        flat++;
    }

    protected virtual void OnTileCreated(TTile tile, int flatIndex, int visualCol, int visualRow) { }

    public virtual void SetAllFloor()
    {
        if (_allTiles == null) return;
        for (int i = 0; i < _allTiles.Length; i++)
            if (_allTiles[i] != null) SetTileFloor(_allTiles[i]);
    }

    float CalcOrthoSize(int cols, int rows, float step, float aspect)
    {
        float w = cols * step;
        float h = rows * step;
        float sizeH = (h / 2f) + GridPadding;
        float sizeW = ((w / 2f) + GridPadding) / aspect;
        return Mathf.Max(sizeH, sizeW);
    }

    protected virtual void FitCameraToGrid()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        _mainCamera.orthographic = true;
        _mainCamera.clearFlags = CameraClearFlags.SolidColor;
        _mainCamera.backgroundColor = GridBgColor;
        _mainCamera.rect = new Rect(0f, 0f, 1f, BaseConfig.gridViewportHeight);

        float step = BaseConfig.tileSize + BaseConfig.tileSpacing;
        float screenAspect = (float)Screen.width / Screen.height;
        float vpAspect = screenAspect / BaseConfig.gridViewportHeight;

        _mainCamera.orthographicSize = CalcOrthoSize(VisCols, VisRows, step, vpAspect);
        FitGridCamera();
    }

    // fit dedicated grid camera (Display 2) to show full grid
    protected void FitGridCamera()
    {
        if (gridCamera == null) return;

        gridCamera.orthographic = true;
        gridCamera.clearFlags = CameraClearFlags.SolidColor;
        gridCamera.backgroundColor = GridBgColor;
        gridCamera.rect = new Rect(0f, 0f, 1f, 1f);

        Vector3 center = GridParentRef != null ? GridParentRef.position : Vector3.zero;
        gridCamera.transform.position = new Vector3(center.x, center.y, CameraZ);

        float step = BaseConfig.tileSize + BaseConfig.tileSpacing;
        gridCamera.orthographicSize = CalcOrthoSize(VisCols, VisRows, step, gridCamera.aspect);
        SnapGridCameraToPixels(center, step);
    }

    // LED Player Pro samples the floor display per pixel, so every tile must be the same whole
    // number of pixels with its edges on pixel boundaries. The plain fit gives fractional tiles
    // at 10×20 (1080 / 21 = 51.4 px) and 10×16, which smears colours into the next tile
    // (Pilot 2026-10-31). Keep the same padding, round the tile down to whole pixels.
    void SnapGridCameraToPixels(Vector3 center, float step)
    {
        GetGridDisplaySize(out int pxW, out int pxH);
        if (pxW <= 0 || pxH <= 0 || step <= 0f) return;

        float pad = GridPadding * 2f / step;   // padding in tiles, both sides together
        int tilePx = Mathf.FloorToInt(Mathf.Min(pxH / (VisRows + pad), pxW / (VisCols + pad)));
        if (tilePx < 1) return;

        float unitsPerPx = step / tilePx;
        gridCamera.orthographicSize = pxH * unitsPerPx / 2f;

        // grid edges on whole pixels: offset the camera by the half pixel left over by odd sizes
        float offX = ((pxW - VisCols * tilePx) % 2) * 0.5f * unitsPerPx;
        float offY = ((pxH - VisRows * tilePx) % 2) * 0.5f * unitsPerPx;
        gridCamera.transform.position = new Vector3(center.x + offX, center.y + offY, CameraZ);
        Debug.Log($"[Grid] floor {VisCols}x{VisRows} on {pxW}x{pxH} px: {tilePx} px per tile");
    }

    void GetGridDisplaySize(out int w, out int h)
    {
        int d = gridCamera.targetDisplay;
        if (d >= 0 && d < Display.displays.Length && Display.displays[d].active && d > 0)
        {
            w = Display.displays[d].renderingWidth;
            h = Display.displays[d].renderingHeight;
            return;
        }
        w = gridCamera.pixelWidth;
        h = gridCamera.pixelHeight;
    }

    public virtual void ClearGrid()
    {
        if (_allTiles != null) {
            foreach (var tile in _allTiles)
                if (tile != null) Destroy(tile.gameObject);
        }
        _tiles = null;
        _allTiles = null;
        OnClearGrid();
    }

    void EnsureGridParent()
    {
        if (GridParentRef == null || GridParentRef is RectTransform)
        {
            if (GridParentRef is RectTransform)
                Debug.LogWarning($"[{GridParentName}] gridParent is RectTransform. Creating world-space parent.");

            var existing = GameObject.Find(GridParentName);
            if (existing != null)
                GridParentRef = existing.transform;
            else {
                var go = new GameObject(GridParentName);
                go.transform.position = Vector3.zero;
                GridParentRef = go.transform;
            }
        }
    }

    // coordinate mapping — serpentine LED floor
    // flat index = x * GH + y (column-major per strip)

    protected int ToFlatIndex(int x, int y) => x * GH + y;

    protected Vector2Int FromFlatIndex(int idx)
    {
        int x = idx / GH;
        int y = idx % GH;
        return new Vector2Int(x, y);
    }

    public int VisualToFlatIndex(int visCol, int visRow)
    {
        int gw = GW, gh = GH;
        int strip = visCol / 2;
        int x = gw - 1 - strip;
        bool rightCol = (visCol % 2) == 1;

        int y = rightCol ? visRow : gh - 1 - visRow;

        if (x < 0 || x >= gw || y < 0 || y >= gh) return -1;
        return ToFlatIndex(x, y);
    }

    public Vector2Int FlatToVisual(int flatIndex)
    {
        int gw = GW, gh = GH;
        int x = flatIndex / gh;
        int y = flatIndex % gh;
        int half = gh / 2;

        int visCol, visRow;
        if (y < half) {
            visCol = (gw - 1 - x) * 2 + 1;
            visRow = y;
        } else {
            visCol = (gw - 1 - x) * 2;
            visRow = gh - 1 - y;
        }
        return new Vector2Int(visCol, visRow);
    }

    public bool IsInGrid(int visCol, int visRow)
    {
        return visCol >= 0 && visCol < VisCols && visRow >= 0 && visRow < VisRows;
    }

    public TTile GetTileByFlatIndex(int idx)
    {
        if (_allTiles == null || idx < 0 || idx >= _allTiles.Length) return null;
        return _allTiles[idx];
    }

    public TTile GetTileByGridCoord(int x, int y)
    {
        if (_tiles == null || x < 0 || x >= GW || y < 0 || y >= GH) return null;
        return _tiles[x, y];
    }

    // LED floor touch input
    public void OnDebugTouchDown(int physX, int physY)
    {
        if (physX < 0 || physX >= GW || physY < 0 || physY >= GH) return;
        var tile = GetTileByGridCoord(physX, physY);
        if (tile != null) HandleTouchDown(tile);
    }

    public void OnDebugTouchUp(int physX, int physY)
    {
        if (physX < 0 || physX >= GW || physY < 0 || physY >= GH) return;
        var tile = GetTileByGridCoord(physX, physY);
        if (tile != null) HandleTouchUp(tile);
    }

    public float GridViewportHeight => BaseConfig.gridViewportHeight;

    // game over floor buttons (shared across all games)

    public void SetTileColorByVisual(int vx, int vy, Color color)
    {
        int idx = VisualToFlatIndex(vx, vy);
        if (idx < 0) return;
        var tile = GetTileByFlatIndex(idx);
        if (tile != null) tile.SetColor(color);
    }

    // restart only (exit is done from the tablet) — same size as the start-only button;
    // default sits on the start-only button (bottom-centered), when UseCenterSplitGameOverLayout=true
    // it is centered at grid middle
    // (for center-split games where players stand at edges, this avoids accidental steps)
    protected virtual bool UseCenterSplitGameOverLayout => false;

    int CenterCol => VisCols / 2;
    int CenterRow => VisRows / 2;

    protected virtual int GameOverRowStart =>
        UseCenterSplitGameOverLayout ? CenterRow - StartOnlyH / 2 : StartOnlyY;
    protected virtual int GameOverRowEnd => GameOverRowStart + StartOnlyH - 1;
    protected virtual int RestartColStart =>
        UseCenterSplitGameOverLayout ? CenterCol - StartOnlyW / 2 : StartOnlyX;
    protected virtual int RestartColEnd => RestartColStart + StartOnlyW - 1;

    public void RenderGameOverButtons()
    {
        SetAllTilesBlack();
        Color restart = BaseConfig.restartButtonColor;

        for (int vy = GameOverRowStart; vy <= GameOverRowEnd; vy++)
            for (int vx = RestartColStart; vx <= RestartColEnd; vx++)
                SetTileColorByVisual(vx, vy, restart);
    }

    public int GetGameOverButtonFromVisualCoord(Vector2Int coord)
    {
        int vx = coord.x, vy = coord.y;
        if (vy >= GameOverRowStart && vy <= GameOverRowEnd
            && vx >= RestartColStart && vx <= RestartColEnd) return 0;
        return -99;
    }

    // hybrid setup buttons — small Start anchored bottom, optional L/R arrows centered
    // layout: . L . . . . . . . R .   (arrow row, vertical center)
    //         . . . . . S . . . . .   (start row, bottom)
    // hit-test return: -1=left, 0=start, 1=right, -99=none

    protected virtual int HybridStartW => 1;
    protected virtual int HybridStartH => 1;
    protected virtual int HybridArrowRow => VisRows / 2;
    protected virtual int HybridStartX => (VisCols - HybridStartW) / 2;
    protected virtual int HybridStartY => VisRows - HybridStartH - 3;
    protected virtual int HybridLeftX => 1;
    protected virtual int HybridRightX => VisCols - 2;

    public void RenderHybridStart(Color startColor)
        => DrawHybridButtons(startColor, default, false);

    public void RenderHybridButtons(Color startColor, Color arrowColor)
        => DrawHybridButtons(startColor, arrowColor, true);

    void DrawHybridButtons(Color startColor, Color arrowColor, bool hasArrows)
    {
        if (hasArrows) {
            SetTileColorByVisual(HybridLeftX, HybridArrowRow, arrowColor);
            SetTileColorByVisual(HybridRightX, HybridArrowRow, arrowColor);
        }
        for (int dy = 0; dy < HybridStartH; dy++)
            for (int dx = 0; dx < HybridStartW; dx++)
                SetTileColorByVisual(HybridStartX + dx, HybridStartY + dy, startColor);
    }

    public int GetHybridButton(Vector2Int coord, bool hasArrows)
    {
        if (hasArrows) {
            if (coord.x == HybridLeftX && coord.y == HybridArrowRow) return -1;
            if (coord.x == HybridRightX && coord.y == HybridArrowRow) return 1;
        }
        int sx = coord.x - HybridStartX;
        int sy = coord.y - HybridStartY;
        if (sx >= 0 && sx < HybridStartW && sy >= 0 && sy < HybridStartH) return 0;
        return -99;
    }

    public bool IsHybridStart(int flatIndex)
        => GetHybridButton(FlatToVisual(flatIndex), false) == 0;

    // start-only big button — bottom-anchored 2x4 block
    // used by games with a single confirm (no mode select)

    protected virtual int StartOnlyW => 4;
    protected virtual int StartOnlyH => 2;
    protected virtual int StartOnlyX => (VisCols - StartOnlyW) / 2;
    protected virtual int StartOnlyY => VisRows - StartOnlyH - 3;

    public void RenderStartOnlyButton(Color color)
    {
        for (int dy = 0; dy < StartOnlyH; dy++)
            for (int dx = 0; dx < StartOnlyW; dx++)
                SetTileColorByVisual(StartOnlyX + dx, StartOnlyY + dy, color);
    }

    public void ClearStartOnlyButton()
    {
        for (int dy = 0; dy < StartOnlyH; dy++)
            for (int dx = 0; dx < StartOnlyW; dx++)
                SetTileColorByVisual(StartOnlyX + dx, StartOnlyY + dy, Color.black);
    }

    public bool IsStartOnlyButton(Vector2Int coord)
    {
        int sx = coord.x - StartOnlyX;
        int sy = coord.y - StartOnlyY;
        return sx >= 0 && sx < StartOnlyW && sy >= 0 && sy < StartOnlyH;
    }

    public bool IsStartOnlyButton(int flatIndex)
        => IsStartOnlyButton(FlatToVisual(flatIndex));

    // START block in the middle of the floor (Pilot 2026-10-22): the select screens whose picks
    // moved to the tablet (Luck Castle / Quiz Zone / Rhythm Stomp) show only this.

    const int CenterStartW = 4;
    const int CenterStartH = 2;
    int CenterStartX => (VisCols - CenterStartW) / 2;
    int CenterStartY => (VisRows - CenterStartH) / 2;

    public void RenderCenterStartButton(Color color)
    {
        for (int dy = 0; dy < CenterStartH; dy++)
            for (int dx = 0; dx < CenterStartW; dx++)
                SetTileColorByVisual(CenterStartX + dx, CenterStartY + dy, color);
    }

    public bool IsCenterStartButton(Vector2Int coord)
    {
        int sx = coord.x - CenterStartX;
        int sy = coord.y - CenterStartY;
        return sx >= 0 && sx < CenterStartW && sy >= 0 && sy < CenterStartH;
    }

    // dpad — full 4-direction cross, up/down 2x1 horizontal, left/right 1x2 vertical
    // up/down rendered always but only hit-test when DpadVerticalInteractable is true
    // (e.g. QuizZone's 2D mode). default: render visible, hit-test ignored.
    // arrows are vertically centered (decoupled from Start position).
    // layout (VisRows=12, VisCols=10, with StartOnlyY=10):
    //   row 3: . . . . ↑ ↑ . . . .
    //   row 4: . . . ← . . → . . .
    //   row 5: . . . ← . . → . . .
    //   row 6: . . . . ↓ ↓ . . . .
    //   row 7-9: . . . . . . . . . .   (gap)
    //   row 10-11: . . . S S S S . . .   (start 4x2, bottom)
    // hit-test: -1=left/up, 1=right/down, 0=start, -99=none

    protected virtual bool DpadVerticalInteractable => false;

    protected virtual int DpadVCenterX => (VisCols - 2) / 2;
    protected virtual int DpadUpY    => (VisRows - 4) / 2;
    protected virtual int DpadLRTopY => DpadUpY + 1;
    protected virtual int DpadLRBotY => DpadUpY + 2;
    protected virtual int DpadDownY  => DpadUpY + 3;
    protected virtual int DpadLeftX  => DpadVCenterX - 1;
    protected virtual int DpadRightX => DpadVCenterX + 2;

    public void RenderDpad(Color arrowColor)
    {
        int cx = DpadVCenterX;
        // up / down (2 horizontal tiles each) — rendered for visual cross
        SetTileColorByVisual(cx,     DpadUpY,   arrowColor);
        SetTileColorByVisual(cx + 1, DpadUpY,   arrowColor);
        SetTileColorByVisual(cx,     DpadDownY, arrowColor);
        SetTileColorByVisual(cx + 1, DpadDownY, arrowColor);
        // left / right (2 vertical tiles each)
        SetTileColorByVisual(DpadLeftX,  DpadLRTopY, arrowColor);
        SetTileColorByVisual(DpadLeftX,  DpadLRBotY, arrowColor);
        SetTileColorByVisual(DpadRightX, DpadLRTopY, arrowColor);
        SetTileColorByVisual(DpadRightX, DpadLRBotY, arrowColor);
    }

    public void RenderDpadStart(Color arrowColor, Color startColor)
    {
        RenderDpad(arrowColor);
        RenderStartOnlyButton(startColor);
    }

    public virtual int GetDpadButton(Vector2Int coord)
    {
        int cx = DpadVCenterX;
        int x = coord.x, y = coord.y;
        if (DpadVerticalInteractable) {
            if (y == DpadUpY   && (x == cx || x == cx + 1)) return -1;
            if (y == DpadDownY && (x == cx || x == cx + 1)) return 1;
        }
        if (x == DpadLeftX  && (y == DpadLRTopY || y == DpadLRBotY)) return -1;
        if (x == DpadRightX && (y == DpadLRTopY || y == DpadLRBotY)) return 1;
        if (IsStartOnlyButton(coord)) return 0;
        return -99;
    }

    // glyph rendering (4x5 pixel font on grid)

    const int GlyphW = 4;
    const int GlyphH = 5;

    static readonly bool[,] Glyph1 = {
        { false, true,  true,  false },
        { false, false, true,  false },
        { false, false, true,  false },
        { false, false, true,  false },
        { false, true,  true,  true },
    };
    static readonly bool[,] Glyph2 = {
        { true,  true,  true,  true },
        { false, false, false, true },
        { true,  true,  true,  true },
        { true,  false, false, false },
        { true,  true,  true,  true },
    };
    static readonly bool[,] Glyph3 = {
        { true,  true,  true,  true },
        { false, false, false, true },
        { false, true,  true,  true },
        { false, false, false, true },
        { true,  true,  true,  true },
    };
    static readonly bool[,] GlyphG = {
        { true,  true,  true,  true },
        { true,  false, false, false },
        { true,  false, true,  true },
        { true,  false, false, true },
        { true,  true,  true,  true },
    };
    static readonly bool[,] GlyphO = {
        { true,  true,  true,  true },
        { true,  false, false, true },
        { true,  false, false, true },
        { true,  false, false, true },
        { true,  true,  true,  true },
    };

    public void SetAllTilesBlack()
    {
        if (_allTiles == null) return;
        for (int i = 0; i < _allTiles.Length; i++) {
            if (_allTiles[i] != null) {
                _allTiles[i].StopAllTileAnimations();
                _allTiles[i].SetColor(Color.black);
            }
        }
    }

    public void RenderDigitOnGrid(int digit, Color color, int rotCcw = 0)
    {
        if (_allTiles == null) return;

        bool[,] glyph;
        switch (digit) {
            case 1: glyph = Glyph1; break;
            case 2: glyph = Glyph2; break;
            case 3: glyph = Glyph3; break;
            default: return;
        }

        glyph = RotateGlyph(glyph, rotCcw);
        int gH = glyph.GetLength(0);
        int gW = glyph.GetLength(1);

        SetAllTilesBlack();
        RenderGlyph(glyph, (VisCols - gW) / 2, (VisRows - gH) / 2, color);
    }

    public void RenderGoOnGrid(Color color, int rotCcw = 0)
    {
        if (_allTiles == null) return;
        SetAllTilesBlack();

        // Build the "GO" composite (G + gap + O horizontally), then rotate
        // the whole composition as one block so glyph spacing stays intact.
        bool[,] go = ComposeGo();
        go = RotateGlyph(go, rotCcw);
        int gH = go.GetLength(0);
        int gW = go.GetLength(1);

        RenderGlyph(go, (VisCols - gW) / 2, (VisRows - gH) / 2, color);
    }

    static bool[,] ComposeGo()
    {
        int totalW = GlyphW + 1 + GlyphW;
        var buf = new bool[GlyphH, totalW];
        for (int r = 0; r < GlyphH; r++)
        for (int c = 0; c < GlyphW; c++)
        {
            buf[r, c] = GlyphG[r, c];
            buf[r, c + GlyphW + 1] = GlyphO[r, c];
        }
        return buf;
    }

    // rotCcw: 0/1/2/3 = 0°/90°CCW/180°/270°CCW. 3 == 90°CW.
    static bool[,] RotateGlyph(bool[,] src, int rotCcw)
    {
        rotCcw = ((rotCcw % 4) + 4) % 4;
        if (rotCcw == 0) return src;
        int h = src.GetLength(0);
        int w = src.GetLength(1);
        bool[,] dst;
        switch (rotCcw)
        {
            case 1: // 90° CCW: new[w-1-c, r] = old[r, c]
                dst = new bool[w, h];
                for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    dst[w - 1 - c, r] = src[r, c];
                return dst;
            case 2: // 180°
                dst = new bool[h, w];
                for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    dst[h - 1 - r, w - 1 - c] = src[r, c];
                return dst;
            default: // 3 = 90° CW: new[c, h-1-r] = old[r, c]
                dst = new bool[w, h];
                for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    dst[c, h - 1 - r] = src[r, c];
                return dst;
        }
    }

    void RenderGlyph(bool[,] glyph, int ox, int oy, Color color)
    {
        int rows = glyph.GetLength(0);
        int cols = glyph.GetLength(1);

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            if (!glyph[r, c]) continue;
            int idx = VisualToFlatIndex(ox + c, oy + r);
            if (idx < 0) continue;
            var tile = GetTileByFlatIndex(idx);
            if (tile != null) tile.SetColor(color);
        }
    }
}
