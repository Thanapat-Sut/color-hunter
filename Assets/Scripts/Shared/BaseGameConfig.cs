using UnityEngine;

public abstract class BaseGameConfig : ScriptableObject
{
    [Header("Grid (physical: strips x pixels)")]
    public int gridWidth = 5;
    public int gridHeight = 40;

    [Header("Grid - Two Lane (physical: strips x pixels)")]
    public int twoLaneGridWidth = 0;
    public int twoLaneGridHeight = 0;

    [Header("Tile Layout")]
    public float tileSize = 1f;
    public float tileSpacing = 0f;

    [Header("Camera")]
    [Range(0.3f, 0.9f)]
    public float gridViewportHeight = 0.65f;

    [Header("Tile Colors — Base")]
    public Color floorColor = Color.black;

    [Header("Floor Buttons (LED Tile)")]
    public Color startButtonConfirmColor = new Color(0f, 0.8f, 0.4f, 1f);
    public Color arrowColor = new Color(0f, 0f, 1f, 1f);
    public Color restartButtonColor = new Color(0f, 0.8f, 0.4f, 1f);
    public float startButtonDelay = 2f;
    public float gameOverButtonDelay = 5f;
    public float dpadButtonDelay = 1f;

    public virtual int TotalTiles => gridWidth * gridHeight;
    public virtual int VisualCols => gridWidth * 2;
    public virtual int VisualRows => gridHeight / 2;

    // dual-lane helpers
    public int GetGridWidth(bool isDualLane) => isDualLane ? twoLaneGridWidth : gridWidth;
    public int GetGridHeight(bool isDualLane) => isDualLane ? twoLaneGridHeight : gridHeight;
    public int GetTotalTiles(bool isDualLane) => GetGridWidth(isDualLane) * GetGridHeight(isDualLane);
    public int GetVisualCols(bool isDualLane) => GetGridWidth(isDualLane) * 2;
    public int GetVisualRows(bool isDualLane) => GetGridHeight(isDualLane) / 2;
}
